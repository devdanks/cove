using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Cove.Data;

/// <summary>
/// Schema C Stage 1 dual-write service. Writes are idempotent and silent on conflict.
/// </summary>
public sealed class EntityIdentifierService : IEntityIdentifierService
{
    private readonly CoveContext _db;

    public EntityIdentifierService(CoveContext db)
    {
        _db = db;
    }

    public async Task SyncAsync(string entityKind, int entityId, string scheme, IEnumerable<string> values, string? source = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityKind)) throw new ArgumentException("entityKind required", nameof(entityKind));
        if (entityId <= 0) throw new ArgumentException("entityId must be positive", nameof(entityId));
        if (string.IsNullOrWhiteSpace(scheme)) throw new ArgumentException("scheme required", nameof(scheme));

        // Build the desired set: dedupe by NormalizedValue.
        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim();
            var norm = scheme == IdentifierSchemes.Url ? NormalizeUrl(trimmed) : trimmed.ToLowerInvariant();
            if (string.IsNullOrEmpty(norm)) continue;
            desired[norm] = trimmed;
        }

        var existing = await _db.EntityIdentifiers
            .Where(e => e.EntityKind == entityKind && e.EntityId == entityId
                && e.Scheme == scheme && e.Source == source)
            .ToListAsync(ct);

        // Remove rows whose NormalizedValue is no longer desired.
        var existingByNorm = existing.GroupBy(e => e.NormalizedValue, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in existing)
        {
            if (!desired.ContainsKey(row.NormalizedValue))
                _db.EntityIdentifiers.Remove(row);
        }

        // Add rows for any newly desired norm.
        foreach (var (norm, value) in desired)
        {
            if (existingByNorm.ContainsKey(norm)) continue;
            _db.EntityIdentifiers.Add(new EntityIdentifier
            {
                EntityKind = entityKind,
                EntityId = entityId,
                Scheme = scheme,
                Value = value,
                NormalizedValue = norm,
                Source = source,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAllAsync(string entityKind, int entityId, CancellationToken ct = default)
    {
        await _db.EntityIdentifiers
            .Where(e => e.EntityKind == entityKind && e.EntityId == entityId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// URL normalization mirroring PerformerScrapeService.NormalizeUrlKey:
    /// strip "www.", lower-case host+path+query, drop trailing slash.
    /// </summary>
    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.Length == 0) path = "/";
            var query = uri.Query;
            return string.Concat(host.ToLowerInvariant(), path.ToLowerInvariant(), query.ToLowerInvariant());
        }
        return trimmed.TrimEnd('/').ToLowerInvariant();
    }
}
