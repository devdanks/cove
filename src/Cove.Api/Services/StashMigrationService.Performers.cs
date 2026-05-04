using Microsoft.Data.Sqlite;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<Dictionary<int, int>> ImportPerformersAsync(SqliteConnection conn, Dictionary<string, string> blobMap, Dictionary<int, int> tagIdMap, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
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

        _logger.LogInformation("Importing {Total} performers...", rows.Count);
        var idMap = new Dictionary<int, int>(rows.Count);
        progress.Report(startProgress, "Importing performers...");
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
                Rating = row.Rating,
                Details = row.Details,
                IgnoreAutoTag = row.IgnoreAutoTag,
                ImageBlobId = GetBlobId(blobMap, row.ImageBlob),
            };
            _db.ChangeTracker.Clear();
            var transaction = string.Equals(_db.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal)
                ? null
                : await _db.Database.BeginTransactionAsync(ct);
            try
            {
                _db.Performers.Add(entity);
                await _db.SaveChangesAsync(ct);

                await SaveImportedPerformerChildrenAsync(entity.Id, performerUrls, performerAliases, performerTags, performerRemoteIds, ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                if (transaction is not null)
                {
                    try
                    {
                        await transaction.RollbackAsync(ct);
                    }
                    catch
                    {
                    }
                }

                _db.ChangeTracker.Clear();
                _logger.LogError(ex,
                    "Failed importing performer {StashPerformerId} '{PerformerName}' with URLs [{Urls}], aliases [{Aliases}], and remote IDs [{RemoteIds}]",
                    row.Id,
                    row.Name,
                    string.Join(", ", performerUrls),
                    string.Join(", ", performerAliases),
                    string.Join(", ", performerRemoteIds.Select(id => $"{id.Ep}:{id.Rid}")));
                throw;
            }
            finally
            {
                if (transaction is not null)
                    await transaction.DisposeAsync();
            }

            idMap[row.Id] = entity.Id;
            _db.ChangeTracker.Clear();

            if (idMap.Count % 100 == 0 || idMap.Count == rows.Count)
            {
                ReportPhase(progress, startProgress, endProgress, idMap.Count, rows.Count, $"Importing performers ({idMap.Count}/{rows.Count})");
                _logger.LogInformation("Imported {Count}/{Total} performers...", idMap.Count, rows.Count);
            }
        }
        _logger.LogInformation("Imported {Count} performers", idMap.Count);
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