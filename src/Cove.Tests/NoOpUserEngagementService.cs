using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using System.Text.Json;

namespace Cove.Tests;

internal sealed class NoOpUserEngagementService : IUserEngagementService
{
    public Task<UserEngagementSnapshot?> GetSnapshotAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<Dictionary<string, int>?> GetRatingsByAspectAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
        => Task.FromResult<Dictionary<string, int>?>([]);

    public Task<Dictionary<int, UserEngagementSnapshot>> GetSnapshotsAsync(AffinityHostType hostType, IEnumerable<int> hostIds, CancellationToken cancellationToken = default)
        => Task.FromResult(new Dictionary<int, UserEngagementSnapshot>());

    public Task<Dictionary<int, UserEngagementSnapshot>> GetSceneSnapshotsAsync(IEnumerable<int> sceneIds, CancellationToken cancellationToken = default)
        => Task.FromResult(new Dictionary<int, UserEngagementSnapshot>());

    public Task<UserEngagementSnapshot?> SetFavoriteAsync(AffinityHostType hostType, int hostId, bool isFavorite, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> SetRatingAsync(AffinityHostType hostType, int hostId, int? value, string aspect = "overall", CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<bool> RecordInteractionAsync(InteractionHostType hostType, int hostId, InteractionKind kind, JsonElement? meta = null, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> RecordPlaybackIntervalsAsync(PlaybackIntervalsRequestDto dto, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<EngagementInteractionDto>> GetInteractionsAsync(InteractionHostType? hostType = null, int? hostId = null, int limit = 100, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<EngagementInteractionDto>>([]);

    public Task<UserEngagementSnapshot?> RecordScenePlayAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> DeleteScenePlayAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> ResetScenePlayAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> IncrementSceneLikeAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> DecrementSceneLikeAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> ResetSceneLikeAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> IncrementImageLikeAsync(int imageId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> DecrementImageLikeAsync(int imageId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> ResetImageLikeAsync(int imageId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> ResetSceneActivityAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<UserEngagementSnapshot?> SetSceneRatingAsync(int sceneId, int? value, string aspect = "overall", CancellationToken cancellationToken = default)
        => Task.FromResult<UserEngagementSnapshot?>(null);

    public Task<SceneHistoryDto?> GetSceneHistoryAsync(int sceneId, CancellationToken cancellationToken = default)
        => Task.FromResult<SceneHistoryDto?>(null);
}