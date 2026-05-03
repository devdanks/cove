using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Cove.Data.Services;

public sealed class UserEngagementService(CoveContext db, ICurrentPrincipalAccessor principalAccessor) : IUserEngagementService
{
    private static readonly UserEngagementSnapshot EmptySnapshot = new(false, null, 0d, 0d, 0, null, 0, 0);

    public async Task<UserEngagementSnapshot?> GetSnapshotAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
    {
        if (!await EntityExistsAsync(hostType, hostId, cancellationToken))
            return null;

        var snapshots = await GetSnapshotsAsync(hostType, [hostId], cancellationToken);
        return snapshots.GetValueOrDefault(hostId) ?? EmptySnapshot;
    }

    public async Task<Dictionary<string, int>?> GetRatingsByAspectAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
    {
        if (!await EntityExistsAsync(hostType, hostId, cancellationToken))
            return null;

        var userId = principalAccessor.Current?.UserId;
        if (!userId.HasValue)
            return [];

        var ratingHostType = ToRatingHostType(hostType);
        var ratings = await db.Ratings
            .Where(rating => rating.UserId == userId.Value && rating.HostType == ratingHostType && rating.HostId == hostId)
            .OrderBy(rating => rating.Aspect)
            .ToListAsync(cancellationToken);

        return ratings.ToDictionary(rating => rating.Aspect, rating => rating.Value);
    }

    public async Task<Dictionary<int, UserEngagementSnapshot>> GetSnapshotsAsync(AffinityHostType hostType, IEnumerable<int> hostIds, CancellationToken cancellationToken = default)
    {
        var ids = hostIds.Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        var userId = principalAccessor.Current?.UserId;
        if (!userId.HasValue)
            return ids.ToDictionary(id => id, _ => EmptySnapshot);

        var ratingHostType = ToRatingHostType(hostType);
        var affinities = await db.UserEntityAffinities
            .Where(affinity => affinity.UserId == userId.Value && affinity.HostType == hostType && ids.Contains(affinity.HostId))
            .ToDictionaryAsync(affinity => affinity.HostId, cancellationToken);

        var ratings = await db.Ratings
            .Where(rating => rating.UserId == userId.Value && rating.HostType == ratingHostType && rating.Aspect == "overall" && ids.Contains(rating.HostId))
            .ToDictionaryAsync(rating => rating.HostId, cancellationToken);

        return ids.ToDictionary(id => id, id => ToSnapshot(affinities.GetValueOrDefault(id), ratings.GetValueOrDefault(id)));
    }

    public Task<Dictionary<int, UserEngagementSnapshot>> GetSceneSnapshotsAsync(IEnumerable<int> sceneIds, CancellationToken cancellationToken = default)
        => GetSnapshotsAsync(AffinityHostType.Scene, sceneIds, cancellationToken);

    public async Task<UserEngagementSnapshot?> SetFavoriteAsync(AffinityHostType hostType, int hostId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (!await EntityExistsAsync(hostType, hostId, cancellationToken))
            return null;

        var affinity = await GetOrCreateAffinityAsync(hostType, hostId, cancellationToken);
        if (affinity != null)
        {
            affinity.IsFavorite = isFavorite;
            affinity.FavoritedAt = isFavorite ? DateTime.UtcNow : null;
        }

        await MirrorLegacyFavoriteAsync(hostType, hostId, isFavorite, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await GetSnapshotsAsync(hostType, [hostId], cancellationToken)).GetValueOrDefault(hostId) ?? EmptySnapshot;
    }

    public async Task<UserEngagementSnapshot?> SetRatingAsync(AffinityHostType hostType, int hostId, int? value, string aspect = "overall", CancellationToken cancellationToken = default)
    {
        if (hostType == AffinityHostType.Scene)
            return await SetSceneRatingAsync(hostId, value, aspect, cancellationToken);

        if (!await EntityExistsAsync(hostType, hostId, cancellationToken))
            return null;

        var normalizedAspect = NormalizeAspect(aspect);

        var userId = principalAccessor.Current?.UserId;
        if (userId.HasValue)
        {
            var ratingHostType = ToRatingHostType(hostType);
            var existing = await db.Ratings.FirstOrDefaultAsync(
                rating => rating.UserId == userId.Value && rating.HostType == ratingHostType && rating.HostId == hostId && rating.Aspect == normalizedAspect,
                cancellationToken);

            if (!value.HasValue)
            {
                if (existing != null)
                    db.Ratings.Remove(existing);
            }
            else if (existing == null)
            {
                db.Ratings.Add(new Rating
                {
                    UserId = userId.Value,
                    HostType = ratingHostType,
                    HostId = hostId,
                    Aspect = normalizedAspect,
                    Value = Math.Clamp(value.Value, 0, 100),
                });
            }
            else
            {
                existing.Value = Math.Clamp(value.Value, 0, 100);
            }
        }

        if (IsOverallAspect(normalizedAspect))
            await MirrorLegacyRatingAsync(hostType, hostId, value, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await GetSnapshotsAsync(hostType, [hostId], cancellationToken)).GetValueOrDefault(hostId) ?? EmptySnapshot;
    }

    public async Task<bool> RecordInteractionAsync(InteractionHostType hostType, int hostId, InteractionKind kind, JsonElement? meta = null, CancellationToken cancellationToken = default)
    {
        var userId = principalAccessor.Current?.UserId;
        if (!userId.HasValue)
            return false;

        var normalizedHostId = InteractionValueMapper.RequiresConcreteHost(hostType) ? hostId : 0;
        if (InteractionValueMapper.RequiresConcreteHost(hostType) && !await InteractionHostExistsAsync(hostType, normalizedHostId, cancellationToken))
            return false;

        db.Interactions.Add(new Interaction
        {
            UserId = userId.Value,
            HostType = hostType,
            HostId = normalizedHostId,
            Kind = kind,
            At = DateTime.UtcNow,
            Meta = CloneJsonDocument(meta),
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<EngagementInteractionDto>> GetInteractionsAsync(InteractionHostType? hostType = null, int? hostId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var userId = principalAccessor.Current?.UserId;
        if (!userId.HasValue)
            return [];

        var normalizedLimit = Math.Clamp(limit, 1, 500);
        var query = db.Interactions
            .Where(interaction => interaction.UserId == userId.Value);

        if (hostType.HasValue)
            query = query.Where(interaction => interaction.HostType == hostType.Value);

        if (hostId.HasValue)
            query = query.Where(interaction => interaction.HostId == hostId.Value);

        return await query
            .OrderByDescending(interaction => interaction.At)
            .ThenByDescending(interaction => interaction.Id)
            .Take(normalizedLimit)
            .Select(interaction => ToEngagementInteractionDto(interaction))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserEngagementSnapshot?> RecordScenePlayAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var now = DateTime.UtcNow;
        var affinity = await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken);
        if (affinity != null)
        {
            affinity.ViewCount++;
            affinity.LastConsumedAt = now;
        }

        db.Set<ScenePlayHistory>().Add(new ScenePlayHistory { SceneId = sceneId, PlayedAt = now });
        scene.PlayCount = affinity?.ViewCount ?? (scene.PlayCount + 1);
        scene.LastPlayedAt = affinity?.LastConsumedAt ?? now;
        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, affinity, cancellationToken);
    }

    public async Task<UserEngagementSnapshot?> DeleteScenePlayAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var affinity = await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken, createIfMissing: false);
        if (affinity != null)
        {
            affinity.ViewCount = Math.Max(0, affinity.ViewCount - 1);

            // Remove the most recent playback session for this user+scene
            var lastPlaybackSession = await db.PlaybackSessions
                .Where(session => session.UserId == affinity.UserId && session.HostType == InteractionHostType.Scene && session.HostId == sceneId)
                .OrderByDescending(session => session.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (lastPlaybackSession != null)
                db.PlaybackSessions.Remove(lastPlaybackSession);

            affinity.LastConsumedAt = await db.PlaybackSessions
                .Where(session => session.UserId == affinity.UserId && session.HostType == InteractionHostType.Scene && session.HostId == sceneId)
                .OrderByDescending(session => session.StartedAt)
                .Select(session => (DateTime?)session.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Remove the most recent global play history entry for this scene
        var lastPlayHistory = await db.Set<ScenePlayHistory>()
            .Where(h => h.SceneId == sceneId)
            .OrderByDescending(h => h.PlayedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (lastPlayHistory != null)
            db.Set<ScenePlayHistory>().Remove(lastPlayHistory);

        scene.PlayCount = affinity?.ViewCount ?? 0;
        scene.LastPlayedAt = affinity?.LastConsumedAt;
        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, affinity, cancellationToken);
    }

    public async Task<UserEngagementSnapshot?> ResetScenePlayAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var affinity = await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken);
        if (affinity != null)
        {
            affinity.ViewCount = 0;
            affinity.CompleteCount = 0;
            affinity.TotalConsumedSec = 0;
            affinity.LastPositionSec = 0;
            affinity.LastConsumedAt = null;

            var playbackSessions = await db.PlaybackSessions
                .Where(session => session.UserId == affinity.UserId && session.HostType == InteractionHostType.Scene && session.HostId == sceneId)
                .ToListAsync(cancellationToken);
            db.PlaybackSessions.RemoveRange(playbackSessions);
        }

        var allPlayHistory = await db.Set<ScenePlayHistory>()
            .Where(h => h.SceneId == sceneId)
            .ToListAsync(cancellationToken);
        db.Set<ScenePlayHistory>().RemoveRange(allPlayHistory);

        scene.PlayCount = 0;
        scene.PlayDuration = 0;
        scene.ResumeTime = 0;
        scene.LastPlayedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, affinity, cancellationToken);
    }

    public async Task<UserEngagementSnapshot?> IncrementSceneOAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var now = DateTime.UtcNow;
        var affinity = await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken);
        if (affinity != null)
        {
            affinity.OCount++;
            db.Interactions.Add(new Interaction
            {
                UserId = affinity.UserId,
                HostType = InteractionHostType.Scene,
                HostId = sceneId,
                Kind = InteractionKind.OCount,
                At = now,
            });
        }

        scene.OCounter = affinity?.OCount ?? (scene.OCounter + 1);
        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, affinity, cancellationToken);
    }

    public async Task<UserEngagementSnapshot?> DecrementSceneOAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var affinity = await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken, createIfMissing: false);
        if (affinity != null)
        {
            affinity.OCount = Math.Max(0, affinity.OCount - 1);
            var lastInteraction = await db.Interactions
                .Where(interaction => interaction.UserId == affinity.UserId && interaction.HostType == InteractionHostType.Scene && interaction.HostId == sceneId && interaction.Kind == InteractionKind.OCount)
                .OrderByDescending(interaction => interaction.At)
                .FirstOrDefaultAsync(cancellationToken);
            if (lastInteraction != null)
                db.Interactions.Remove(lastInteraction);
        }

        scene.OCounter = affinity?.OCount ?? 0;
        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, affinity, cancellationToken);
    }

    public async Task<UserEngagementSnapshot?> ResetSceneOAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var affinity = await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken);
        if (affinity != null)
        {
            affinity.OCount = 0;
            var interactions = await db.Interactions
                .Where(interaction => interaction.UserId == affinity.UserId && interaction.HostType == InteractionHostType.Scene && interaction.HostId == sceneId && interaction.Kind == InteractionKind.OCount)
                .ToListAsync(cancellationToken);
            db.Interactions.RemoveRange(interactions);
        }

        scene.OCounter = 0;
        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, affinity, cancellationToken);
    }

    /// <summary>
    /// Record a batch of contiguous watched intervals for a playback session.
    /// Each call appends new PlaybackInterval rows, then recomputes per-session TotalWatchedSec
    /// from the full merged set — no guesswork from position deltas.
    /// Also updates UserEntityAffinity.TotalConsumedSec, LastPositionSec, and LastConsumedAt,
    /// and marks IsCompleted / CompleteCount when the session ends near the media tail.
    /// </summary>
    public async Task<bool> RecordPlaybackIntervalsAsync(PlaybackIntervalsRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (!InteractionValueMapper.TryParseHostType(dto.HostType, out var hostType))
            return false;
        if (!await InteractionHostExistsAsync(hostType, dto.HostId, cancellationToken))
            return false;

        var userId = principalAccessor.Current?.UserId;
        if (!userId.HasValue)
            return false;

        var now = DateTime.UtcNow;
        if (!TryParseSessionState(dto.State, out var state))
            state = PlaybackSessionState.Active;

        var session = await db.PlaybackSessions
            .Include(s => s.Intervals)
            .FirstOrDefaultAsync(
                s => s.UserId == userId.Value && s.SessionId == dto.SessionId,
                cancellationToken);

        if (session == null)
        {
            session = new PlaybackSession
            {
                UserId = userId.Value,
                HostType = hostType,
                HostId = dto.HostId,
                SessionId = dto.SessionId,
                StartedAt = now,
                LastSeenAt = now,
            };
            db.PlaybackSessions.Add(session);
        }

        // Append new intervals (validate and clamp)
        var mediaDuration = dto.MediaDurationSec > 0 ? dto.MediaDurationSec : session.MediaDurationSec;
        foreach (var incoming in dto.Intervals)
        {
            var start = Math.Max(0d, incoming.StartSec);
            var end = mediaDuration > 0 ? Math.Min(incoming.EndSec, mediaDuration) : incoming.EndSec;
            end = Math.Max(start, end);
            if (end <= start) continue;

            db.PlaybackIntervals.Add(new PlaybackInterval
            {
                Session = session,
                UserId = userId.Value,
                HostType = hostType,
                HostId = dto.HostId,
                StartSec = start,
                EndSec = end,
                RecordedAt = now,
            });
        }

        // Recompute TotalWatchedSec from the FULL merged interval set for this session
        // We need to flush the new intervals first so they appear in the in-memory list
        var allIntervals = session.Intervals
            .Concat(db.ChangeTracker.Entries<PlaybackInterval>()
                .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added && e.Entity.Session == session)
                .Select(e => e.Entity));
        var prevTotal = session.TotalWatchedSec;
        session.TotalWatchedSec = ComputeMergedWatchedSec(allIntervals);

        // Update session state fields
        session.State = state;
        session.LastSeenAt = now;
        if (mediaDuration > 0) session.MediaDurationSec = mediaDuration;
        if (dto.CurrentPositionSec >= 0) session.LastPositionSec = dto.CurrentPositionSec;

        // Check completion: ended state AND current position is >= 90% of media duration
        var wasCompleted = session.IsCompleted;
        if (state == PlaybackSessionState.Ended && mediaDuration > 0 && dto.CurrentPositionSec >= mediaDuration * 0.90)
        {
            session.IsCompleted = true;
            session.EndedAt ??= now;
        }
        else if (state == PlaybackSessionState.Ended || state == PlaybackSessionState.Abandoned)
        {
            session.EndedAt ??= now;
        }

        // Update affinity
        var affinityHostType = hostType == InteractionHostType.Scene ? AffinityHostType.Scene : (AffinityHostType?)null;
        if (affinityHostType.HasValue)
        {
            var affinity = await GetOrCreateAffinityAsync(affinityHostType.Value, dto.HostId, cancellationToken);
            if (affinity != null)
            {
                var delta = session.TotalWatchedSec - prevTotal;
                if (delta > 0d)
                    affinity.TotalConsumedSec = Math.Max(0d, affinity.TotalConsumedSec + delta);

                if (dto.CurrentPositionSec >= 0)
                    affinity.LastPositionSec = dto.CurrentPositionSec;
                affinity.LastConsumedAt = now;

                if (!wasCompleted && session.IsCompleted)
                    affinity.CompleteCount++;

                // Update scene-level resume/duration cache
                if (hostType == InteractionHostType.Scene)
                {
                    var scene = await db.Scenes.FirstOrDefaultAsync(sc => sc.Id == dto.HostId, cancellationToken);
                    if (scene != null)
                    {
                        scene.ResumeTime = dto.CurrentPositionSec;
                        scene.PlayDuration = affinity.TotalConsumedSec;
                        scene.LastPlayedAt = now;
                    }
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static double ComputeMergedWatchedSec(IEnumerable<PlaybackInterval> intervals)
    {
        var sorted = intervals.OrderBy(i => i.StartSec).ThenBy(i => i.EndSec).ToList();
        var total = 0d;
        var curStart = double.MinValue;
        var curEnd = double.MinValue;
        foreach (var iv in sorted)
        {
            if (iv.EndSec <= iv.StartSec) continue;
            if (iv.StartSec > curEnd)
            {
                total += Math.Max(0d, curEnd - curStart);
                curStart = iv.StartSec;
                curEnd = iv.EndSec;
            }
            else
            {
                curEnd = Math.Max(curEnd, iv.EndSec);
            }
        }
        total += Math.Max(0d, curEnd - curStart);
        return total;
    }

    private static bool TryParseSessionState(string? state, out PlaybackSessionState parsed)
    {
        parsed = PlaybackSessionState.Active;
        return (state?.Trim().ToLowerInvariant()) switch
        {
            "active" => Assign(PlaybackSessionState.Active, out parsed),
            "paused" => Assign(PlaybackSessionState.Paused, out parsed),
            "ended" => Assign(PlaybackSessionState.Ended, out parsed),
            "abandoned" => Assign(PlaybackSessionState.Abandoned, out parsed),
            _ => false,
        };

        static bool Assign(PlaybackSessionState val, out PlaybackSessionState p) { p = val; return true; }
    }

    public async Task<UserEngagementSnapshot?> ResetSceneActivityAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var affinity = await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken);
        if (affinity != null)
        {
            affinity.LastPositionSec = 0d;
            affinity.TotalConsumedSec = 0d;
            affinity.LastConsumedAt = null;

            var playbackSessions = await db.PlaybackSessions
                .Where(session => session.UserId == affinity.UserId && session.HostType == InteractionHostType.Scene && session.HostId == sceneId)
                .ToListAsync(cancellationToken);
            db.PlaybackSessions.RemoveRange(playbackSessions);
        }

        scene.ResumeTime = 0d;
        scene.PlayDuration = 0d;
        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, affinity, cancellationToken);
    }

    public async Task<UserEngagementSnapshot?> SetSceneRatingAsync(int sceneId, int? value, string aspect = "overall", CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.FirstOrDefaultAsync(item => item.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;

        var normalizedAspect = NormalizeAspect(aspect);

        var userId = principalAccessor.Current?.UserId;
        if (userId.HasValue)
        {
            var existing = await db.Ratings.FirstOrDefaultAsync(
                rating => rating.UserId == userId.Value && rating.HostType == RatingHostType.Scene && rating.HostId == sceneId && rating.Aspect == normalizedAspect,
                cancellationToken);

            if (!value.HasValue)
            {
                if (existing != null)
                    db.Ratings.Remove(existing);
            }
            else if (existing == null)
            {
                db.Ratings.Add(new Rating
                {
                    UserId = userId.Value,
                    HostType = RatingHostType.Scene,
                    HostId = sceneId,
                    Aspect = normalizedAspect,
                    Value = Math.Clamp(value.Value, 0, 100),
                });
            }
            else
            {
                existing.Value = Math.Clamp(value.Value, 0, 100);
            }
        }

        if (IsOverallAspect(normalizedAspect))
            scene.Rating = value.HasValue ? Math.Clamp(value.Value, 0, 100) : null;

        await db.SaveChangesAsync(cancellationToken);

        return await BuildSceneSnapshotAsync(sceneId, scene, null, cancellationToken);
    }

    public async Task<SceneHistoryDto?> GetSceneHistoryAsync(int sceneId, CancellationToken cancellationToken = default)
    {
        var sceneExists = await db.Scenes.AnyAsync(item => item.Id == sceneId, cancellationToken);
        if (!sceneExists)
            return null;

        var userId = principalAccessor.Current?.UserId;
        if (!userId.HasValue)
        {
            var playHistory = await db.Set<ScenePlayHistory>()
                .Where(history => history.SceneId == sceneId)
                .OrderByDescending(history => history.PlayedAt)
                .Select(history => history.PlayedAt.ToString("o"))
                .ToListAsync(cancellationToken);
            var oHistory = await db.Set<SceneOHistory>()
                .Where(history => history.SceneId == sceneId)
                .OrderByDescending(history => history.OccurredAt)
                .Select(history => history.OccurredAt.ToString("o"))
                .ToListAsync(cancellationToken);
            var events = playHistory
                .Select(date => (At: date, Event: new InteractionEventDto("playStart", date)))
                .Concat(oHistory.Select(date => (At: date, Event: new InteractionEventDto("oCount", date))))
                .OrderByDescending(item => item.At, StringComparer.Ordinal)
                .Select(item => item.Event)
                .ToList();
            return new SceneHistoryDto(playHistory, oHistory, events);
        }

        var interactions = await db.Interactions
            .Where(interaction => interaction.UserId == userId.Value && interaction.HostType == InteractionHostType.Scene && interaction.HostId == sceneId)
            .OrderByDescending(interaction => interaction.At)
            .ToListAsync(cancellationToken);
        var playbackSessions = await db.PlaybackSessions
            .Include(session => session.Intervals)
            .Where(session => session.UserId == userId.Value && session.HostType == InteractionHostType.Scene && session.HostId == sceneId)
            .OrderByDescending(session => session.StartedAt)
            .ToListAsync(cancellationToken);

        var playHistoryForUser = await db.Set<ScenePlayHistory>()
            .Where(history => history.SceneId == sceneId)
            .OrderByDescending(history => history.PlayedAt)
            .Select(history => history.PlayedAt.ToString("o"))
            .ToListAsync(cancellationToken);
        var oHistoryForUser = interactions
            .Where(interaction => interaction.Kind == InteractionKind.OCount)
            .Select(interaction => interaction.At.ToString("o"))
            .ToList();
        var eventsForUser = interactions
            .Select(ToInteractionEventDto)
            .ToList();
        var allIntervals = playbackSessions
            .SelectMany(session => session.Intervals)
            .OrderBy(iv => iv.StartSec)
            .ToList();
        var allTimeWatchedIntervals = allIntervals
            .Select(ToPlaybackIntervalDto)
            .ToList();
        var totalDistinctWatchedSec = ComputeMergedWatchedSec(allIntervals);
        var sessionsForUser = playbackSessions
            .Select(ToScenePlaybackSessionDto)
            .ToList();
        return new SceneHistoryDto(playHistoryForUser, oHistoryForUser, eventsForUser, allTimeWatchedIntervals, totalDistinctWatchedSec, sessionsForUser);
    }

    private Task<UserEntityAffinity?> GetOrCreateSceneAffinityAsync(int sceneId, CancellationToken cancellationToken, bool createIfMissing = true)
        => GetOrCreateAffinityAsync(AffinityHostType.Scene, sceneId, cancellationToken, createIfMissing);

    private async Task<UserEntityAffinity?> GetOrCreateAffinityAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken, bool createIfMissing = true)
    {
        var userId = principalAccessor.Current?.UserId;
        if (!userId.HasValue)
            return null;

        var affinity = await db.UserEntityAffinities.FirstOrDefaultAsync(
            item => item.UserId == userId.Value && item.HostType == hostType && item.HostId == hostId,
            cancellationToken);

        if (affinity == null && createIfMissing)
        {
            affinity = new UserEntityAffinity
            {
                UserId = userId.Value,
                HostType = hostType,
                HostId = hostId,
            };
            db.UserEntityAffinities.Add(affinity);
        }

        return affinity;
    }

    private async Task<UserEngagementSnapshot> BuildSceneSnapshotAsync(int sceneId, Scene scene, UserEntityAffinity? affinity, CancellationToken cancellationToken)
    {
        var userId = principalAccessor.Current?.UserId;
        Rating? rating = null;
        if (userId.HasValue)
        {
            rating = await db.Ratings.FirstOrDefaultAsync(
                item => item.UserId == userId.Value && item.HostType == RatingHostType.Scene && item.HostId == sceneId && item.Aspect == "overall",
                cancellationToken);
        }

        affinity ??= await GetOrCreateSceneAffinityAsync(sceneId, cancellationToken, createIfMissing: false);
        return ToSnapshot(affinity, rating, scene);
    }

    private async Task<bool> EntityExistsAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken)
        => hostType switch
        {
            AffinityHostType.Scene => await db.Scenes.AnyAsync(scene => scene.Id == hostId, cancellationToken),
            AffinityHostType.Image => await db.Images.AnyAsync(image => image.Id == hostId, cancellationToken),
            AffinityHostType.Performer => await db.Performers.AnyAsync(performer => performer.Id == hostId, cancellationToken),
            AffinityHostType.Face => await db.Faces.AnyAsync(face => face.Id == hostId, cancellationToken),
            AffinityHostType.Tag => await db.Tags.AnyAsync(tag => tag.Id == hostId, cancellationToken),
            AffinityHostType.Studio => await db.Studios.AnyAsync(studio => studio.Id == hostId, cancellationToken),
            AffinityHostType.Gallery => await db.Galleries.AnyAsync(gallery => gallery.Id == hostId, cancellationToken),
            AffinityHostType.Group => await db.Groups.AnyAsync(group => group.Id == hostId, cancellationToken),
            _ => false,
        };

    private async Task<bool> InteractionHostExistsAsync(InteractionHostType hostType, int hostId, CancellationToken cancellationToken)
        => hostType switch
        {
            InteractionHostType.Scene => await db.Scenes.AnyAsync(scene => scene.Id == hostId, cancellationToken),
            InteractionHostType.Image => await db.Images.AnyAsync(image => image.Id == hostId, cancellationToken),
            InteractionHostType.Performer => await db.Performers.AnyAsync(performer => performer.Id == hostId, cancellationToken),
            InteractionHostType.Tag => await db.Tags.AnyAsync(tag => tag.Id == hostId, cancellationToken),
            InteractionHostType.Face => await db.Faces.AnyAsync(face => face.Id == hostId, cancellationToken),
            InteractionHostType.Segment => await db.Segments.AnyAsync(segment => segment.Id == hostId, cancellationToken),
            InteractionHostType.Studio => await db.Studios.AnyAsync(studio => studio.Id == hostId, cancellationToken),
            InteractionHostType.Gallery => await db.Galleries.AnyAsync(gallery => gallery.Id == hostId, cancellationToken),
            InteractionHostType.Group => await db.Groups.AnyAsync(group => group.Id == hostId, cancellationToken),
            InteractionHostType.Search => true,
            InteractionHostType.Collection => true,
            _ => false,
        };

    private async Task MirrorLegacyFavoriteAsync(AffinityHostType hostType, int hostId, bool isFavorite, CancellationToken cancellationToken)
    {
        switch (hostType)
        {
            case AffinityHostType.Performer:
                var performer = await db.Performers.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (performer != null) performer.Favorite = isFavorite;
                break;
            case AffinityHostType.Tag:
                var tag = await db.Tags.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (tag != null) tag.Favorite = isFavorite;
                break;
            case AffinityHostType.Studio:
                var studio = await db.Studios.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (studio != null) studio.Favorite = isFavorite;
                break;
        }
    }

    private async Task MirrorLegacyRatingAsync(AffinityHostType hostType, int hostId, int? value, CancellationToken cancellationToken)
    {
        switch (hostType)
        {
            case AffinityHostType.Image:
                var image = await db.Images.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (image != null) image.Rating = value;
                break;
            case AffinityHostType.Performer:
                var performer = await db.Performers.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (performer != null) performer.Rating = value;
                break;
            case AffinityHostType.Studio:
                var studio = await db.Studios.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (studio != null) studio.Rating = value;
                break;
            case AffinityHostType.Gallery:
                var gallery = await db.Galleries.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (gallery != null) gallery.Rating = value;
                break;
            case AffinityHostType.Group:
                var group = await db.Groups.FirstOrDefaultAsync(item => item.Id == hostId, cancellationToken);
                if (group != null) group.Rating = value;
                break;
        }
    }

    private static RatingHostType ToRatingHostType(AffinityHostType hostType) => hostType switch
    {
        AffinityHostType.Scene => RatingHostType.Scene,
        AffinityHostType.Image => RatingHostType.Image,
        AffinityHostType.Performer => RatingHostType.Performer,
        AffinityHostType.Face => RatingHostType.Face,
        AffinityHostType.Tag => RatingHostType.Tag,
        AffinityHostType.Studio => RatingHostType.Studio,
        AffinityHostType.Gallery => RatingHostType.Gallery,
        AffinityHostType.Group => RatingHostType.Group,
        _ => throw new ArgumentOutOfRangeException(nameof(hostType), hostType, null),
    };

    private static string NormalizeAspect(string? aspect)
    {
        var normalized = string.IsNullOrWhiteSpace(aspect) ? "overall" : aspect.Trim();
        return IsOverallAspect(normalized) ? "overall" : normalized;
    }

    private static bool IsOverallAspect(string aspect)
        => string.Equals(aspect, "overall", StringComparison.OrdinalIgnoreCase);

    private static InteractionEventDto ToInteractionEventDto(Interaction interaction)
        => new(
            InteractionValueMapper.ToName(interaction.Kind),
            interaction.At.ToString("o"),
            interaction.Meta == null ? null : interaction.Meta.RootElement.Clone());

    private static EngagementInteractionDto ToEngagementInteractionDto(Interaction interaction)
        => new(
            interaction.Id,
            InteractionValueMapper.ToName(interaction.HostType),
            InteractionValueMapper.RequiresConcreteHost(interaction.HostType) ? interaction.HostId : null,
            InteractionValueMapper.ToName(interaction.Kind),
            interaction.At.ToString("o"),
            interaction.Meta == null ? null : interaction.Meta.RootElement.Clone());

    private static ScenePlaybackSessionDto ToScenePlaybackSessionDto(PlaybackSession session)
        => new(
            session.SessionId,
            session.StartedAt.ToString("o"),
            session.LastSeenAt.ToString("o"),
            session.EndedAt?.ToString("o"),
            session.State.ToString().ToLowerInvariant(),
            session.MediaDurationSec,
            session.TotalWatchedSec,
            session.LastPositionSec,
            session.IsCompleted,
            session.Intervals
                .OrderBy(iv => iv.StartSec)
                .Select(ToPlaybackIntervalDto)
                .ToList());

    private static PlaybackIntervalDto ToPlaybackIntervalDto(PlaybackInterval iv)
        => new(iv.StartSec, iv.EndSec, iv.RecordedAt.ToString("o"));

    private static JsonDocument? CloneJsonDocument(JsonElement? element)
        => element.HasValue ? JsonDocument.Parse(element.Value.GetRawText()) : null;

    private static UserEngagementSnapshot ToSnapshot(UserEntityAffinity? affinity, Rating? rating, Scene? scene = null) => new(
        affinity?.IsFavorite ?? false,
        rating?.Value ?? scene?.Rating,
        affinity?.LastPositionSec ?? scene?.ResumeTime ?? 0d,
        affinity?.TotalConsumedSec ?? scene?.PlayDuration ?? 0d,
        affinity?.ViewCount ?? scene?.PlayCount ?? 0,
        affinity?.LastConsumedAt ?? scene?.LastPlayedAt,
        affinity?.OCount ?? scene?.OCounter ?? 0,
        affinity?.CompleteCount ?? 0);
}