using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Cove.Data.Repositories;

public class TagApplicationRepository : ITagApplicationRepository
{
    private readonly CoveContext _db;
    public TagApplicationRepository(CoveContext db) => _db = db;

    public async Task<IReadOnlyList<TagApplication>> FindAsync(TagApplicationFilter filter, CancellationToken ct = default)
    {
        var query = _db.TagApplications.AsQueryable();

        if (filter.HostType.HasValue)
            query = query.Where(ta => ta.HostType == filter.HostType.Value);
        if (filter.HostId.HasValue)
            query = query.Where(ta => ta.HostId == filter.HostId.Value);
        if (filter.SourceKey != null)
            query = query.Where(ta => ta.SourceKey == filter.SourceKey);
        if (filter.ModelKeys != null && filter.ModelKeys.Count > 0)
            query = query.Where(ta => filter.ModelKeys.Contains(ta.ModelKey));

        return await query.AsNoTracking().ToListAsync(ct);
    }

    public void Add(TagApplication tagApplication) => _db.TagApplications.Add(tagApplication);

    public void RemoveRange(IEnumerable<TagApplication> tagApplications) => _db.TagApplications.RemoveRange(tagApplications);

    public async Task RemoveOrphanedTagLinksAsync(AffinityHostType hostType,
        IReadOnlyList<int> entityIds, string sourceKey, CancellationToken ct = default)
    {
        if (hostType == AffinityHostType.Scene)
        {
            var sceneTags = _db.Set<SceneTag>();
            var orphanedTagIds = await sceneTags
                .Where(st => entityIds.Contains(st.SceneId)
                    && !_db.TagApplications.Any(ta =>
                        ta.HostType == AffinityHostType.Scene
                        && ta.HostId == st.SceneId
                        && ta.TagId == st.TagId
                        && ta.SourceKey == sourceKey))
                .ToListAsync(ct);
            sceneTags.RemoveRange(orphanedTagIds);
        }
        else if (hostType == AffinityHostType.Image)
        {
            var imageTags = _db.Set<ImageTag>();
            var orphanedTagIds = await imageTags
                .Where(it => entityIds.Contains(it.ImageId)
                    && !_db.TagApplications.Any(ta =>
                        ta.HostType == AffinityHostType.Image
                        && ta.HostId == it.ImageId
                        && ta.TagId == it.TagId
                        && ta.SourceKey == sourceKey))
                .ToListAsync(ct);
            imageTags.RemoveRange(orphanedTagIds);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}
