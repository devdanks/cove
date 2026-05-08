using Cove.Core.DTOs;
using Cove.Core.Entities;

namespace Cove.Core.Interfaces;

public interface ITagProvenanceService
{
    Task RecordAsync(
        AffinityHostType hostType,
        int hostId,
        int tagId,
        string sourceKey,
        string? sourceRunId = null,
        string? modelKey = null,
        float? confidence = null,
        string? contextType = null,
        int? contextId = null,
        double? totalDurationSec = null,
        double? hostDurationSec = null,
        CancellationToken cancellationToken = default);

    Task RecordAsync(
        AffinityHostType hostType,
        int hostId,
        Tag tag,
        string sourceKey,
        string? sourceRunId = null,
        string? modelKey = null,
        float? confidence = null,
        string? contextType = null,
        int? contextId = null,
        double? totalDurationSec = null,
        double? hostDurationSec = null,
        CancellationToken cancellationToken = default);

    Task SyncTagSetAsync(
        AffinityHostType hostType,
        int hostId,
        IReadOnlyCollection<int> previousTagIds,
        IReadOnlyCollection<int> currentTagIds,
        string sourceKey = "user",
        CancellationToken cancellationToken = default);

    Task RemoveForHostAsync(
        AffinityHostType hostType,
        int hostId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, List<TagProvenanceDto>>> GetLookupAsync(
        AffinityHostType hostType,
        int hostId,
        IReadOnlyCollection<int> tagIds,
        CancellationToken cancellationToken = default);
}