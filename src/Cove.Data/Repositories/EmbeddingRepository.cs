using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Cove.Data.Repositories;

public class EmbeddingRepository : IEmbeddingRepository
{
    private readonly CoveContext _db;
    public EmbeddingRepository(CoveContext db) => _db = db;

    public async Task<IReadOnlyList<Embedding>> FindAsync(EmbeddingFilter filter, CancellationToken ct = default)
    {
        var query = _db.Embeddings.AsQueryable();

        if (filter.HostType.HasValue)
            query = query.Where(e => e.HostType == filter.HostType.Value);
        if (filter.HostId.HasValue)
            query = query.Where(e => e.HostId == filter.HostId.Value);
        if (filter.HostIds != null && filter.HostIds.Count > 0)
            query = query.Where(e => filter.HostIds.Contains(e.HostId));
        if (filter.SourceKey != null)
            query = query.Where(e => e.SourceKey == filter.SourceKey);
        if (filter.Kind != null)
            query = query.Where(e => e.Kind == filter.Kind);
        if (filter.KindFamily != null)
            query = query.Where(e => e.KindFamily == filter.KindFamily);
        if (filter.Modality.HasValue)
            query = query.Where(e => e.Modality == filter.Modality.Value);
        if (filter.IsSemantic.HasValue)
            query = query.Where(e => e.IsSemantic == filter.IsSemantic.Value);
        if (filter.SectionIndexGreaterThan.HasValue)
            query = query.Where(e => e.SectionIndex > filter.SectionIndexGreaterThan.Value);

        return await query.AsNoTracking().ToListAsync(ct);
    }

    public void Add(Embedding embedding) => _db.Embeddings.Add(embedding);

    public void RemoveRange(IEnumerable<Embedding> embeddings) => _db.Embeddings.RemoveRange(embeddings);

    public async Task UpdateHostIdAsync(EmbeddingHostType hostType, string sourceKey,
        IReadOnlyList<int> oldHostIds, int newHostId, CancellationToken ct = default)
    {
        await _db.Embeddings
            .Where(e => e.HostType == hostType && e.SourceKey == sourceKey && oldHostIds.Contains(e.HostId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.HostId, newHostId), ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}
