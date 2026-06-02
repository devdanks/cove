using Cove.Core.Entities;

namespace Cove.Core.Interfaces;

/// <summary>Filter criteria for querying tag applications.</summary>
public sealed class TagApplicationFilter
{
    public AffinityHostType? HostType { get; init; }
    public int? HostId { get; init; }
    public string? SourceKey { get; init; }
    public IReadOnlyList<string>? ModelKeys { get; init; }
}

/// <summary>
/// Generic repository for tag application CRUD, including cleanup of orphaned
/// scene/image tag links when AI-sourced tag applications are removed.
/// Available to any extension that writes AI-generated or automated tag associations.
/// </summary>
public interface ITagApplicationRepository
{
    Task<IReadOnlyList<TagApplication>> FindAsync(TagApplicationFilter filter, CancellationToken ct = default);
    void Add(TagApplication tagApplication);
    void RemoveRange(IEnumerable<TagApplication> tagApplications);

    /// <summary>
    /// After removing tag applications for <paramref name="entityIds"/>, removes any
    /// SceneTag or ImageTag join rows whose tag has no remaining application for that entity.
    /// Call this before SaveChangesAsync when replacing AI-generated tag applications.
    /// </summary>
    Task RemoveOrphanedTagLinksAsync(AffinityHostType hostType,
        IReadOnlyList<int> entityIds, string sourceKey, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
