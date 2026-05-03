using Cove.Core.DTOs;
using Cove.Core.Entities;
using System.Text.Json;

namespace Cove.Core.Interfaces;

public sealed record UserEngagementSnapshot(
    bool IsFavorite,
    int? Rating,
    double ResumeTime,
    double PlayDuration,
    int PlayCount,
    DateTime? LastPlayedAt,
    int OCount,
    int CompleteCount);

public interface IUserEngagementService
{
    Task<UserEngagementSnapshot?> GetSnapshotAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default);

    Task<Dictionary<string, int>?> GetRatingsByAspectAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default);

    Task<Dictionary<int, UserEngagementSnapshot>> GetSnapshotsAsync(AffinityHostType hostType, IEnumerable<int> hostIds, CancellationToken cancellationToken = default);

    Task<Dictionary<int, UserEngagementSnapshot>> GetSceneSnapshotsAsync(IEnumerable<int> sceneIds, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> SetFavoriteAsync(AffinityHostType hostType, int hostId, bool isFavorite, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> SetRatingAsync(AffinityHostType hostType, int hostId, int? value, string aspect = "overall", CancellationToken cancellationToken = default);

    Task<bool> RecordInteractionAsync(InteractionHostType hostType, int hostId, InteractionKind kind, JsonElement? meta = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EngagementInteractionDto>> GetInteractionsAsync(InteractionHostType? hostType = null, int? hostId = null, int limit = 100, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> RecordScenePlayAsync(int sceneId, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> DeleteScenePlayAsync(int sceneId, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> ResetScenePlayAsync(int sceneId, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> IncrementSceneOAsync(int sceneId, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> DecrementSceneOAsync(int sceneId, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> ResetSceneOAsync(int sceneId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a batch of contiguous watched intervals for a playback session.
    /// Creates or updates the PlaybackSession, inserts PlaybackInterval rows, recomputes per-session
    /// TotalWatchedSec from the merged interval set, and updates UserEntityAffinity accordingly.
    /// </summary>
    Task<bool> RecordPlaybackIntervalsAsync(PlaybackIntervalsRequestDto dto, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> ResetSceneActivityAsync(int sceneId, CancellationToken cancellationToken = default);

    Task<UserEngagementSnapshot?> SetSceneRatingAsync(int sceneId, int? value, string aspect = "overall", CancellationToken cancellationToken = default);

    Task<SceneHistoryDto?> GetSceneHistoryAsync(int sceneId, CancellationToken cancellationToken = default);
}