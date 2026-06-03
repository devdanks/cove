using Microsoft.Data.Sqlite;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<Dictionary<int, int>> ImportGroupsAsync(SqliteConnection conn, Dictionary<string, string> blobMap, Dictionary<int, int> studioIdMap, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var hasFrontImageBlob = await ColumnExistsAsync(conn, "groups", "front_image_blob", ct);
        var hasBackImageBlob = await ColumnExistsAsync(conn, "groups", "back_image_blob", ct);
        var rows = new List<(int Id, string Name, string? Aliases, int? Duration, string? Date,
            int? Rating, int? StudioId, string? Director, string? Description, string? FrontImageBlob, string? BackImageBlob)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT id, name, aliases, duration, date, rating, studio_id, director, description,
                       {(hasFrontImageBlob ? "front_image_blob" : "NULL")} AS front_image_blob,
                       {(hasBackImageBlob ? "back_image_blob" : "NULL")} AS back_image_blob
                FROM groups
                """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add((r.GetInt32(0), r.GetString(1), ReadStringNull(r, 2), ReadIntNull(r, 3),
                    ReadStringNull(r, 4), ReadIntNull(r, 5), ReadIntNull(r, 6),
                    ReadStringNull(r, 7), ReadStringNull(r, 8), ReadStringNull(r, 9), ReadStringNull(r, 10)));
        }
        var urls = await ReadUrlsAsync(conn, "group_urls", "group_id", ct);
        var sceneCounts = await ReadGroupSceneCountsAsync(conn, ct);
        var importUnits = BuildGroupImportUnits(rows, urls, sceneCounts);

        var idMap = new Dictionary<int, int>(rows.Count);
        const int GroupBatchSize = 500;
        var batchEntities = new List<(IReadOnlyList<int> StashIds, Cove.Core.Entities.Group Entity)>(GroupBatchSize);
        progress.Report(startProgress, "Importing groups...");
        _logger.LogDebug(
            "[StashTiming] phase=groups checkpoint=loaded rows={Rows} units={Units} urlOwners={UrlOwners} elapsedMs={ElapsedMilliseconds:F0}",
            rows.Count,
            importUnits.Count,
            urls.Count,
            stopwatch.Elapsed.TotalMilliseconds);
        foreach (var unit in importUnits)
        {
            var entity = new Cove.Core.Entities.Group
            {
                Name = unit.Name,
                Aliases = unit.Aliases,
                Duration = unit.Duration,
                Date = ParseDate(unit.Date),
                StudioId = unit.StudioId.HasValue && studioIdMap.TryGetValue(unit.StudioId.Value, out var sId) ? sId : null,
                Director = unit.Director,
                Synopsis = unit.Description,
                FrontImageBlobId = GetBlobId(blobMap, unit.FrontImageBlob),
                BackImageBlobId = GetBlobId(blobMap, unit.BackImageBlob),
                Urls = unit.Urls.Select(u => new GroupUrl { Url = u }).ToList(),
            };
            _db.Groups.Add(entity);
            batchEntities.Add((unit.StashIds, entity));

            if (batchEntities.Count >= GroupBatchSize)
            {
                await _db.SaveChangesAsync(ct);

                foreach (var (stashIds, group) in batchEntities)
                {
                    foreach (var stashId in stashIds)
                        idMap[stashId] = group.Id;
                }

                batchEntities.Clear();
                _db.ChangeTracker.Clear();
                ReportPhase(progress, startProgress, endProgress, idMap.Count, rows.Count, $"Importing groups ({idMap.Count}/{rows.Count})");
                _logger.LogDebug(
                    "[StashTiming] phase=groups checkpoint=batch imported={Imported} total={Total} elapsedMs={ElapsedMilliseconds:F0}",
                    idMap.Count,
                    rows.Count,
                    stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        if (batchEntities.Count > 0)
        {
            await _db.SaveChangesAsync(ct);

            foreach (var (stashIds, group) in batchEntities)
            {
                foreach (var stashId in stashIds)
                    idMap[stashId] = group.Id;
            }

            batchEntities.Clear();
            _db.ChangeTracker.Clear();
            ReportPhase(progress, startProgress, endProgress, idMap.Count, rows.Count, $"Importing groups ({idMap.Count}/{rows.Count})");
        }
        await AddImportedOverallRatingsAsync(
            rows.Select(row => new ImportedRatingSeed(row.Id, row.Rating)),
            idMap,
            RatingHostType.Group,
            ct);

        _logger.LogInformation("Imported {SourceCount} Stash groups into {GroupCount} Cove groups in {Elapsed}", idMap.Count, importUnits.Count, stopwatch.Elapsed);
        return idMap;
    }

    private sealed record StashGroupImportUnit(
        IReadOnlyList<int> StashIds,
        string Name,
        string? Aliases,
        int? Duration,
        string? Date,
        int? Rating,
        int? StudioId,
        string? Director,
        string? Description,
        string? FrontImageBlob,
        string? BackImageBlob,
        IReadOnlyList<string> Urls);

    private static async Task<Dictionary<int, int>> ReadGroupSceneCountsAsync(SqliteConnection conn, CancellationToken ct)
    {
        var result = new Dictionary<int, int>();
        if (!await TableExistsAsync(conn, "groups_scenes", ct))
            return result;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT group_id, COUNT(*) FROM groups_scenes GROUP BY group_id";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result[r.GetInt32(0)] = r.GetInt32(1);

        return result;
    }

    private static List<StashGroupImportUnit> BuildGroupImportUnits(
        IReadOnlyList<(int Id, string Name, string? Aliases, int? Duration, string? Date,
            int? Rating, int? StudioId, string? Director, string? Description, string? FrontImageBlob, string? BackImageBlob)> rows,
        IReadOnlyDictionary<int, List<string>> urls,
        IReadOnlyDictionary<int, int> sceneCounts)
    {
        var units = new List<StashGroupImportUnit>(rows.Count);
        foreach (var duplicateSet in rows.GroupBy(GetGroupDuplicateKey))
        {
            var duplicateRows = duplicateSet.ToList();
            var shouldMerge = duplicateRows.Count > 1
                && duplicateRows.Any(row => sceneCounts.GetValueOrDefault(row.Id) > 0)
                && duplicateRows.Any(row => !string.IsNullOrWhiteSpace(row.FrontImageBlob) || !string.IsNullOrWhiteSpace(row.BackImageBlob));

            if (!shouldMerge)
            {
                foreach (var row in duplicateRows)
                    units.Add(CreateGroupImportUnit([row], urls));
                continue;
            }

            units.Add(CreateGroupImportUnit(
                duplicateRows
                    .OrderByDescending(row => sceneCounts.GetValueOrDefault(row.Id))
                    .ThenBy(row => row.Id)
                    .ToList(),
                urls));
        }

        return units;
    }

    private static StashGroupImportUnit CreateGroupImportUnit(
        IReadOnlyList<(int Id, string Name, string? Aliases, int? Duration, string? Date,
            int? Rating, int? StudioId, string? Director, string? Description, string? FrontImageBlob, string? BackImageBlob)> rows,
        IReadOnlyDictionary<int, List<string>> urls)
    {
        var canonical = rows[0];
        return new StashGroupImportUnit(
            rows.Select(row => row.Id).ToArray(),
            canonical.Name,
            canonical.Aliases,
            canonical.Duration,
            canonical.Date,
            canonical.Rating,
            canonical.StudioId,
            canonical.Director,
            canonical.Description,
            rows.Select(row => row.FrontImageBlob).FirstOrDefault(blob => !string.IsNullOrWhiteSpace(blob)),
            rows.Select(row => row.BackImageBlob).FirstOrDefault(blob => !string.IsNullOrWhiteSpace(blob)),
            rows.SelectMany(row => urls.GetValueOrDefault(row.Id, []))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static (string Name, string? Aliases, int? Duration, string? Date, int? Rating, int? StudioId, string? Director, string? Description) GetGroupDuplicateKey(
        (int Id, string Name, string? Aliases, int? Duration, string? Date, int? Rating, int? StudioId, string? Director, string? Description, string? FrontImageBlob, string? BackImageBlob) row)
        => (row.Name, row.Aliases, row.Duration, row.Date, row.Rating, row.StudioId, row.Director, row.Description);
}
