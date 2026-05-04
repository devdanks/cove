using Microsoft.Data.Sqlite;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<Dictionary<int, int>> ImportStudiosAsync(SqliteConnection conn, Dictionary<string, string> blobMap, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        var rows = new List<(int Id, string Name, int? ParentId, string? Details, int? Rating, bool Favorite, bool IgnoreAutoTag, string? ImageBlob)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, parent_id, details, rating, favorite, ignore_auto_tag, image_blob FROM studios";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add((r.GetInt32(0), r.GetString(1), ReadIntNull(r, 2), ReadStringNull(r, 3),
                    ReadIntNull(r, 4), ReadBool(r, 5), ReadBool(r, 6), ReadStringNull(r, 7)));
        }
        var urls = await ReadUrlsAsync(conn, "studio_urls", "studio_id", ct);
        var aliases = await ReadAliasesAsync(conn, "studio_aliases", "studio_id", ct);

        var studioStashIds = new Dictionary<int, List<(string Ep, string Rid)>>();
        if (await TableExistsAsync(conn, "studio_stash_ids", ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT studio_id, endpoint, stash_id FROM studio_stash_ids";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var sId = r.GetInt32(0);
                if (!studioStashIds.TryGetValue(sId, out var list)) studioStashIds[sId] = list = [];
                list.Add((r.GetString(1), r.GetString(2)));
            }
        }

        var byId = rows.ToDictionary(r => r.Id);
        var ordered = TopologicalSort(rows.Select(r => r.Id).ToList(),
            id => byId[id].ParentId.HasValue ? [byId[id].ParentId!.Value] : (IEnumerable<int>)[]);

        _logger.LogInformation("Importing {Total} studios...", rows.Count);
        var idMap = new Dictionary<int, int>();
        progress.Report(startProgress, "Importing studios...");
        foreach (var stashId in ordered)
        {
            var row = byId[stashId];
            var remoteIds = studioStashIds.GetValueOrDefault(stashId, [])
                .DistinctBy(s => (s.Ep, s.Rid))
                .Select(s => new StudioRemoteId { Endpoint = s.Ep, RemoteId = s.Rid })
                .ToList();
            var entity = new Studio
            {
                Name = row.Name,
                ParentId = row.ParentId.HasValue && idMap.TryGetValue(row.ParentId.Value, out var pId) ? pId : null,
                Details = row.Details,
                Rating = row.Rating,
                Favorite = row.Favorite,
                IgnoreAutoTag = row.IgnoreAutoTag,
                Organized = false,
                ImageBlobId = GetBlobId(blobMap, row.ImageBlob),
                Urls = urls.GetValueOrDefault(stashId, []).Select(u => new StudioUrl { Url = u }).ToList(),
                Aliases = aliases.GetValueOrDefault(stashId, []).Select(a => new StudioAlias { Alias = a }).ToList(),
                RemoteIds = remoteIds,
            };
            _db.ChangeTracker.Clear();
            _db.Studios.Add(entity);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed importing studio {StashStudioId} '{StudioName}' with remote IDs [{RemoteIds}]",
                    stashId,
                    row.Name,
                    string.Join(", ", remoteIds.Select(id => $"{id.Endpoint}:{id.RemoteId}")));
                throw;
            }
            var coveStudioId = entity.Id;
            idMap[stashId] = coveStudioId;
            _db.ChangeTracker.Clear();

            if (idMap.Count % 25 == 0 || idMap.Count == ordered.Count)
            {
                ReportPhase(progress, startProgress, endProgress, idMap.Count, ordered.Count, $"Importing studios ({idMap.Count}/{ordered.Count})");
                _logger.LogInformation("Imported {Count}/{Total} studios...", idMap.Count, ordered.Count);
            }
        }
        _logger.LogInformation("Imported {Count} studios", idMap.Count);
        return idMap;
    }
}