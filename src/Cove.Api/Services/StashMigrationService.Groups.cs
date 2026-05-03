using Microsoft.Data.Sqlite;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<Dictionary<int, int>> ImportGroupsAsync(SqliteConnection conn, Dictionary<int, int> studioIdMap, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        var rows = new List<(int Id, string Name, string? Aliases, int? Duration, string? Date,
            int? Rating, int? StudioId, string? Director, string? Description)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, aliases, duration, date, rating, studio_id, director, description FROM groups";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add((r.GetInt32(0), r.GetString(1), ReadStringNull(r, 2), ReadIntNull(r, 3),
                    ReadStringNull(r, 4), ReadIntNull(r, 5), ReadIntNull(r, 6),
                    ReadStringNull(r, 7), ReadStringNull(r, 8)));
        }
        var urls = await ReadUrlsAsync(conn, "group_urls", "group_id", ct);

        var idMap = new Dictionary<int, int>(rows.Count);
        var batchEntities = new List<(int StashId, Cove.Core.Entities.Group Entity)>(100);
        const int GroupBatchSize = 100;
        progress.Report(startProgress, "Importing groups...");
        foreach (var row in rows)
        {
            var entity = new Cove.Core.Entities.Group
            {
                Name = row.Name,
                Aliases = row.Aliases,
                Duration = row.Duration,
                Date = ParseDate(row.Date),
                Rating = row.Rating,
                StudioId = row.StudioId.HasValue && studioIdMap.TryGetValue(row.StudioId.Value, out var sId) ? sId : null,
                Director = row.Director,
                Synopsis = row.Description,
                Urls = urls.GetValueOrDefault(row.Id, []).Select(u => new GroupUrl { Url = u }).ToList(),
            };
            _db.Groups.Add(entity);
            batchEntities.Add((row.Id, entity));

            if (batchEntities.Count >= GroupBatchSize)
            {
                await _db.SaveChangesAsync(ct);

                foreach (var (stashId, group) in batchEntities)
                    idMap[stashId] = group.Id;

                batchEntities.Clear();
                _db.ChangeTracker.Clear();
                ReportPhase(progress, startProgress, endProgress, idMap.Count, rows.Count, $"Importing groups ({idMap.Count}/{rows.Count})");
            }
        }

        if (batchEntities.Count > 0)
        {
            await _db.SaveChangesAsync(ct);

            foreach (var (stashId, group) in batchEntities)
                idMap[stashId] = group.Id;

            batchEntities.Clear();
            _db.ChangeTracker.Clear();
            ReportPhase(progress, startProgress, endProgress, idMap.Count, rows.Count, $"Importing groups ({idMap.Count}/{rows.Count})");
        }
        _logger.LogInformation("Imported {Count} groups", idMap.Count);
        return idMap;
    }
}