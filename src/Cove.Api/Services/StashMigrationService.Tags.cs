using Microsoft.Data.Sqlite;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<Dictionary<int, int>> ImportTagsAsync(SqliteConnection conn, Dictionary<string, string> blobMap, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        var rows = new List<(int Id, string Name, string? SortName, string? Description, bool Favorite, bool IgnoreAutoTag, string? ImageBlob)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, sort_name, description, favorite, ignore_auto_tag, image_blob FROM tags";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add((r.GetInt32(0), r.GetString(1), ReadStringNull(r, 2), ReadStringNull(r, 3),
                    ReadBool(r, 4), ReadBool(r, 5), ReadStringNull(r, 6)));
        }
        var aliases = await ReadAliasesAsync(conn, "tag_aliases", "tag_id", ct);

        var tagParents = new Dictionary<int, List<int>>();
        if (await TableExistsAsync(conn, "tags_relations", ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT parent_id, child_id FROM tags_relations";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var pId = r.GetInt32(0);
                var cId = r.GetInt32(1);
                if (!tagParents.TryGetValue(cId, out var list)) tagParents[cId] = list = [];
                list.Add(pId);
            }
        }

        var byId = rows.ToDictionary(r => r.Id);
        var ordered = TopologicalSort(rows.Select(r => r.Id).ToList(), id => tagParents.GetValueOrDefault(id, []));

        _logger.LogInformation("Importing {Total} tags...", rows.Count);
        var idMap = new Dictionary<int, int>();
        progress.Report(startProgress, "Importing tags...");
        foreach (var stashId in ordered)
        {
            var row = byId[stashId];
            var entity = new Tag
            {
                Name = row.Name,
                SortName = row.SortName,
                Description = row.Description,
                Favorite = row.Favorite,
                IgnoreAutoTag = row.IgnoreAutoTag,
                ImageBlobId = GetBlobId(blobMap, row.ImageBlob),
                Aliases = aliases.GetValueOrDefault(stashId, []).Select(a => new TagAlias { Alias = a }).ToList(),
            };
            _db.Tags.Add(entity);
            await _db.SaveChangesAsync(ct);
            idMap[stashId] = entity.Id;

            if (idMap.Count % 200 == 0 || idMap.Count == ordered.Count)
            {
                ReportPhase(progress, startProgress, endProgress, idMap.Count, ordered.Count, $"Importing tags ({idMap.Count}/{ordered.Count})");
                _logger.LogInformation("Imported {Count}/{Total} tags...", idMap.Count, ordered.Count);
            }
        }

        if (tagParents.Count > 0)
        {
            foreach (var (childStashId, parentStashIds) in tagParents)
            {
                if (!idMap.TryGetValue(childStashId, out var childCoveId)) continue;
                foreach (var parentStashId in parentStashIds)
                {
                    if (!idMap.TryGetValue(parentStashId, out var parentCoveId)) continue;
                    _db.Set<TagParent>().Add(new TagParent { ParentId = parentCoveId, ChildId = childCoveId });
                }
            }
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Imported {Count} tags", idMap.Count);
        return idMap;
    }
}