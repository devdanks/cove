using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Cove.Data.Repositories;

public class FaceRepository : IFaceRepository
{
    private readonly CoveContext _db;
    public FaceRepository(CoveContext db) => _db = db;

    public async Task<IReadOnlyList<Face>> FindFacesAsync(FaceFilter filter, bool tracking = false, CancellationToken ct = default)
    {
        var query = _db.Faces.AsQueryable();

        if (filter.PrimarySourceKeys != null && filter.PrimarySourceKeys.Count > 0)
            query = query.Where(f => f.PrimarySourceKey != null && filter.PrimarySourceKeys.Contains(f.PrimarySourceKey));
        if (filter.Ids != null && filter.Ids.Count > 0)
            query = query.Where(f => filter.Ids.Contains(f.Id));
        if (filter.HasPerformer.HasValue)
            query = filter.HasPerformer.Value
                ? query.Where(f => f.PerformerId != null)
                : query.Where(f => f.PerformerId == null);
        if (filter.Ignored.HasValue)
            query = query.Where(f => f.Ignored == filter.Ignored.Value);

        if (!tracking)
            query = query.AsNoTracking();

        return await query.ToListAsync(ct);
    }

    public async Task<Face?> GetFaceAsync(int faceId, bool tracking = true, CancellationToken ct = default)
    {
        var query = _db.Faces.AsQueryable();
        if (!tracking)
            query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(f => f.Id == faceId, ct);
    }

    public async Task<bool> FaceExistsAsync(int faceId, CancellationToken ct = default)
        => await _db.Faces.AnyAsync(f => f.Id == faceId, ct);

    public void AddFace(Face face) => _db.Faces.Add(face);

    public async Task<IReadOnlyList<FaceAppearance>> FindAppearancesAsync(FaceAppearanceFilter filter, CancellationToken ct = default)
    {
        var query = _db.FaceAppearances.AsQueryable();

        if (filter.HostType.HasValue)
            query = query.Where(a => a.HostType == filter.HostType.Value);
        if (filter.HostId.HasValue)
            query = query.Where(a => a.HostId == filter.HostId.Value);
        if (filter.SourceKey != null)
            query = query.Where(a => a.SourceKey == filter.SourceKey);
        if (filter.FaceIds != null && filter.FaceIds.Count > 0)
            query = query.Where(a => filter.FaceIds.Contains(a.FaceId));

        return await query.AsNoTracking().ToListAsync(ct);
    }

    public void AddAppearance(FaceAppearance appearance) => _db.FaceAppearances.Add(appearance);

    public void RemoveAppearances(IEnumerable<FaceAppearance> appearances) => _db.FaceAppearances.RemoveRange(appearances);

    public async Task UpdateAppearanceFaceIdAsync(string sourceKey, IReadOnlyList<int> oldFaceIds, int newFaceId, CancellationToken ct = default)
    {
        await _db.FaceAppearances
            .Where(a => a.SourceKey == sourceKey && oldFaceIds.Contains(a.FaceId))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FaceId, newFaceId), ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}
