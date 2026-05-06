using Microsoft.Data.Sqlite;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<Dictionary<int, int>> ImportPerformersAsync(SqliteConnection conn, Dictionary<string, string> blobMap, Dictionary<int, int> tagIdMap, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var rows = new List<(int Id, string Name, string? Disambiguation, string? Gender, string? Birthdate,
            string? Ethnicity, string? Country, string? EyeColor, string? HairColor, int? Height, int? Weight,
            string? Measurements, string? FakeTits, double? PenisLength, string? Circumcised,
            string? CareerLength, string? DeathDate,
            string? Tattoos, string? Piercings, bool Favorite, int? Rating, string? Details,
            bool IgnoreAutoTag, string? ImageBlob)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, name, disambiguation, gender, birthdate, ethnicity, country, eye_color,
                hair_color, height, weight, measurements, fake_tits, penis_length, circumcised, career_length,
                death_date, tattoos, piercings, favorite, rating, details, ignore_auto_tag, image_blob
                FROM performers";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                rows.Add((r.GetInt32(0), r.GetString(1), ReadStringNull(r, 2), ReadStringNull(r, 3),
                    ReadStringNull(r, 4), ReadStringNull(r, 5), ReadStringNull(r, 6), ReadStringNull(r, 7),
                    ReadStringNull(r, 8), ReadIntNull(r, 9), ReadIntNull(r, 10), ReadStringNull(r, 11),
                    ReadStringNull(r, 12), r.IsDBNull(13) ? null : (double?)r.GetDouble(13),
                    ReadStringNull(r, 14), ReadStringNull(r, 15), ReadStringNull(r, 16),
                    ReadStringNull(r, 17), ReadStringNull(r, 18), ReadBool(r, 19), ReadIntNull(r, 20),
                    ReadStringNull(r, 21), ReadBool(r, 22), ReadStringNull(r, 23)));
        }
        var urls = await ReadUrlsAsync(conn, "performer_urls", "performer_id", ct);
        var aliases = await ReadAliasesAsync(conn, "performer_aliases", "performer_id", ct);
        var performerTagMap = await ReadJunctionAsync(conn, "performers_tags", "performer_id", "tag_id", ct);

        var performerStashIds = new Dictionary<int, List<(string Ep, string Rid)>>();
        if (await TableExistsAsync(conn, "performer_stash_ids", ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT performer_id, endpoint, stash_id FROM performer_stash_ids";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var pId = r.GetInt32(0);
                if (!performerStashIds.TryGetValue(pId, out var list)) performerStashIds[pId] = list = [];
                list.Add((r.GetString(1), r.GetString(2)));
            }
        }

        var idMap = new Dictionary<int, int>(rows.Count);
        const int PerformerBatchSize = 500;
        var pendingBatch = new List<(int StashId, Performer Entity)>(PerformerBatchSize);
        progress.Report(startProgress, "Importing performers...");
        _logger.LogDebug(
            "[StashTiming] phase=performers checkpoint=loaded rows={Rows} urlOwners={UrlOwners} aliasOwners={AliasOwners} tagOwners={TagOwners} remoteIdOwners={RemoteIdOwners} elapsedMs={ElapsedMilliseconds:F0}",
            rows.Count,
            urls.Count,
            aliases.Count,
            performerTagMap.Count,
            performerStashIds.Count,
            stopwatch.Elapsed.TotalMilliseconds);

        async Task FlushPerformerBatchAsync()
        {
            if (pendingBatch.Count == 0)
                return;

            await _db.SaveChangesAsync(ct);
            foreach (var (stashId, entity) in pendingBatch)
                idMap[stashId] = entity.Id;

            pendingBatch.Clear();
            _db.ChangeTracker.Clear();
            ReportPhase(progress, startProgress, endProgress, idMap.Count, rows.Count, $"Importing performers ({idMap.Count}/{rows.Count})");
            _logger.LogDebug(
                "[StashTiming] phase=performers checkpoint=batch imported={Imported} total={Total} elapsedMs={ElapsedMilliseconds:F0}",
                idMap.Count,
                rows.Count,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        foreach (var row in rows)
        {
            var performerUrls = urls.GetValueOrDefault(row.Id, [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var performerAliases = aliases.GetValueOrDefault(row.Id, [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var performerTags = performerTagMap.GetValueOrDefault(row.Id, [])
                .Where(tagIdMap.ContainsKey)
                .Distinct()
                .Select(tagId => tagIdMap[tagId])
                .ToList();
            var performerRemoteIds = performerStashIds.GetValueOrDefault(row.Id, [])
                .DistinctBy(s => (s.Ep, s.Rid))
                .ToList();
            var (careerStart, careerEnd) = ParseCareerLength(row.CareerLength);
            var entity = new Performer
            {
                Name = row.Name,
                Disambiguation = row.Disambiguation,
                Gender = ParseGender(row.Gender),
                Birthdate = ParseDate(row.Birthdate),
                Ethnicity = row.Ethnicity,
                Country = row.Country,
                EyeColor = row.EyeColor,
                HairColor = row.HairColor,
                HeightCm = row.Height,
                Weight = row.Weight,
                Measurements = row.Measurements,
                FakeTits = row.FakeTits,
                PenisLength = row.PenisLength,
                Circumcised = ParseCircumcised(row.Circumcised),
                CareerStart = careerStart,
                CareerEnd = careerEnd,
                DeathDate = ParseDate(row.DeathDate),
                Tattoos = row.Tattoos,
                Piercings = row.Piercings,
                Favorite = row.Favorite,
                Details = row.Details,
                IgnoreAutoTag = row.IgnoreAutoTag,
                ImageBlobId = GetBlobId(blobMap, row.ImageBlob),
                Urls = performerUrls.Select(url => new PerformerUrl { Url = url }).ToList(),
                Aliases = performerAliases.Select(alias => new PerformerAlias { Alias = alias }).ToList(),
                PerformerTags = performerTags.Select(tagId => new PerformerTag { TagId = tagId }).ToList(),
                RemoteIds = performerRemoteIds.Select(remoteId => new PerformerRemoteId { Endpoint = remoteId.Ep, RemoteId = remoteId.Rid }).ToList(),
            };
            _db.Performers.Add(entity);
            pendingBatch.Add((row.Id, entity));

            if (pendingBatch.Count >= PerformerBatchSize)
                await FlushPerformerBatchAsync();
        }

        await FlushPerformerBatchAsync();
        await AddImportedOverallRatingsAsync(
            rows.Select(row => new ImportedRatingSeed(row.Id, row.Rating)),
            idMap,
            RatingHostType.Performer,
            ct);

        _logger.LogInformation("Imported {Count} performers in {Elapsed}", idMap.Count, stopwatch.Elapsed);
        return idMap;
    }

    private async Task SaveImportedPerformerChildrenAsync(
        int performerId,
        IReadOnlyCollection<string> performerUrls,
        IReadOnlyCollection<string> performerAliases,
        IReadOnlyCollection<int> performerTags,
        IReadOnlyCollection<(string Ep, string Rid)> performerRemoteIds,
        CancellationToken ct)
    {
        if (performerUrls.Count > 0)
        {
            _db.ChangeTracker.Clear();
            _db.Set<PerformerUrl>().AddRange(performerUrls.Select(url => new PerformerUrl
            {
                PerformerId = performerId,
                Url = url,
            }));
            await _db.SaveChangesAsync(ct);
        }

        if (performerAliases.Count > 0)
        {
            _db.ChangeTracker.Clear();
            _db.Set<PerformerAlias>().AddRange(performerAliases.Select(alias => new PerformerAlias
            {
                PerformerId = performerId,
                Alias = alias,
            }));
            await _db.SaveChangesAsync(ct);
        }

        if (performerTags.Count > 0)
        {
            _db.ChangeTracker.Clear();
            _db.Set<PerformerTag>().AddRange(performerTags.Select(tagId => new PerformerTag
            {
                PerformerId = performerId,
                TagId = tagId,
            }));
            await _db.SaveChangesAsync(ct);
        }

        if (performerRemoteIds.Count > 0)
        {
            _db.ChangeTracker.Clear();
            _db.Set<PerformerRemoteId>().AddRange(performerRemoteIds.Select(remoteId => new PerformerRemoteId
            {
                PerformerId = performerId,
                Endpoint = remoteId.Ep,
                RemoteId = remoteId.Rid,
            }));
            await _db.SaveChangesAsync(ct);
        }

        _db.ChangeTracker.Clear();
    }
}