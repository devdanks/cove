using Cove.Core.Entities;

namespace Cove.Core.Interfaces;

/// <summary>
/// Schema C Stage 1 dual-write helper. SyncAsync is idempotent: it diffs the supplied
/// values against the existing rows for (entity_kind, entity_id, scheme[, source]) and
/// adds/removes rows so the table reflects the new set. Normalization uses the same
/// rules as PerformerScrapeService.NormalizeUrlKey so duplicates collapse.
/// </summary>
public interface IEntityIdentifierService
{
    Task SyncAsync(string entityKind, int entityId, string scheme, IEnumerable<string> values, string? source = null, CancellationToken ct = default);
    Task RemoveAllAsync(string entityKind, int entityId, CancellationToken ct = default);
}
