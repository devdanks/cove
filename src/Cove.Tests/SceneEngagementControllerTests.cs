using Cove.Api.Controllers;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
using Cove.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Cove.Tests;

public class SceneEngagementControllerTests
{
    [Fact]
    public async Task SceneActivityAndRating_AreScopedToCurrentUser()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;

        context.Scenes.Add(new Scene { Title = "Scoped Scene" });
        await context.SaveChangesAsync();
        var sceneId = await context.Scenes.Select(scene => scene.Id).SingleAsync();

        var scenesController = CreateScenesController(context, principalAccessor);
        var playbackController = CreatePlaybackController(context, principalAccessor);

        principalAccessor.Set(CreatePrincipal(7));
        var sessionId = Guid.NewGuid();

        // Record a play (view count)
        Assert.IsType<NoContentResult>(await scenesController.RecordPlay(sceneId, CancellationToken.None));

        // Send first set of intervals: watched 42.5–48.0 (5.5s), paused
        Assert.IsType<NoContentResult>(await playbackController.RecordIntervals(new PlaybackIntervalsRequestDto(
            "scene", sceneId, sessionId, 120.0, 48.0, "paused",
            [new PlaybackIntervalInputDto(42.5, 48.0)]), CancellationToken.None));

        // Send second set: watched 73.5–120.0 (46.5s), ended at full duration → IsCompleted = true
        Assert.IsType<NoContentResult>(await playbackController.RecordIntervals(new PlaybackIntervalsRequestDto(
            "scene", sceneId, sessionId, 120.0, 120.0, "ended",
            [new PlaybackIntervalInputDto(73.5, 120.0)]), CancellationToken.None));

        var incrementResult = await scenesController.IncrementLike(sceneId, CancellationToken.None);
        var incrementOk = Assert.IsType<OkObjectResult>(incrementResult.Result);
        Assert.Equal(1, Assert.IsType<int>(incrementOk.Value));

        var ratingResult = await scenesController.SetRating(sceneId, new SceneRatingDto(88), CancellationToken.None);
        var ratingOk = Assert.IsType<OkObjectResult>(ratingResult.Result);
        Assert.Equal(88, Assert.IsType<int>(ratingOk.Value));

        var audioRatingResult = await scenesController.SetRating(sceneId, new SceneRatingDto(35, "audio"), CancellationToken.None);
        var audioRatingOk = Assert.IsType<OkObjectResult>(audioRatingResult.Result);
        Assert.Equal(88, Assert.IsType<int>(audioRatingOk.Value));

        var ratingsResult = await scenesController.GetRatings(sceneId, CancellationToken.None);
        var ratingsOk = Assert.IsType<OkObjectResult>(ratingsResult.Result);
        var ratingsDto = Assert.IsType<EntityRatingsDto>(ratingsOk.Value);
        Assert.Equal(88, ratingsDto.Ratings["overall"]);
        Assert.Equal(35, ratingsDto.Ratings["audio"]);

        var userOneResult = await scenesController.GetById(sceneId, CancellationToken.None);
        var userOneOk = Assert.IsType<OkObjectResult>(userOneResult.Result);
        var userOneScene = Assert.IsType<SceneDto>(userOneOk.Value);
        Assert.Equal(88, userOneScene.Rating);
        Assert.Equal(120.0, userOneScene.ResumeTime);
        Assert.Equal(52.0, userOneScene.PlayDuration, precision: 5);  // 5.5 + 46.5
        Assert.Equal(2, userOneScene.PlayCount);
        Assert.Equal(1, userOneScene.LikeCounter);

        var historyResult = await scenesController.GetHistory(sceneId, CancellationToken.None);
        var historyOk = Assert.IsType<OkObjectResult>(historyResult.Result);
        var history = Assert.IsType<SceneHistoryDto>(historyOk.Value);
        Assert.Single(history.PlayHistory);
        Assert.Single(history.LikeHistory);
        Assert.NotNull(history.AllTimeWatchedIntervals);
        Assert.Equal(2, history.AllTimeWatchedIntervals!.Count);
        Assert.Equal(42.5, history.AllTimeWatchedIntervals[0].StartSec);
        Assert.Equal(48.0, history.AllTimeWatchedIntervals[0].EndSec);
        Assert.Equal(73.5, history.AllTimeWatchedIntervals[1].StartSec);
        Assert.Equal(120.0, history.AllTimeWatchedIntervals[1].EndSec);
        Assert.Equal(52.0, history.TotalDistinctWatchedSec!.Value, precision: 5);
        Assert.NotNull(history.Sessions);
        var sessionHistory = Assert.Single(history.Sessions!);
        Assert.Equal(sessionId, sessionHistory.SessionId);
        Assert.True(sessionHistory.IsCompleted);
        Assert.Equal(52.0, sessionHistory.TotalWatchedSec, precision: 5);
        Assert.Equal(2, sessionHistory.Intervals.Count);

        context.ChangeTracker.Clear();
        principalAccessor.Set(CreatePrincipal(9));

        var userTwoResult = await scenesController.GetById(sceneId, CancellationToken.None);
        var userTwoOk = Assert.IsType<OkObjectResult>(userTwoResult.Result);
        var userTwoScene = Assert.IsType<SceneDto>(userTwoOk.Value);
        Assert.Null(userTwoScene.Rating);
        Assert.Equal(0d, userTwoScene.ResumeTime);
        Assert.Equal(0d, userTwoScene.PlayDuration);
        Assert.Equal(0, userTwoScene.PlayCount);
        Assert.Equal(0, userTwoScene.LikeCounter);

        var userTwoRatingsResult = await scenesController.GetRatings(sceneId, CancellationToken.None);
        var userTwoRatingsOk = Assert.IsType<OkObjectResult>(userTwoRatingsResult.Result);
        var userTwoRatingsDto = Assert.IsType<EntityRatingsDto>(userTwoRatingsOk.Value);
        Assert.Empty(userTwoRatingsDto.Ratings);

        var affinityRows = await context.UserEntityAffinities.IgnoreQueryFilters().ToListAsync();
        var affinity = Assert.Single(affinityRows);
        Assert.Equal(7, affinity.UserId);
        Assert.Equal(2, affinity.ViewCount);
        Assert.Equal(1, affinity.LikeCount);
        Assert.Equal(1, affinity.CompleteCount);
        Assert.Equal(52.0, affinity.TotalConsumedSec, precision: 5);

        var playbackSessions = await context.PlaybackSessions.IgnoreQueryFilters().ToListAsync();
        var playbackSession = Assert.Single(playbackSessions);
        Assert.Equal(7, playbackSession.UserId);
        Assert.Equal(InteractionHostType.Scene, playbackSession.HostType);
        Assert.Equal(sceneId, playbackSession.HostId);
        Assert.Equal(sessionId, playbackSession.SessionId);
        Assert.True(playbackSession.IsCompleted);
        Assert.Equal(52.0, playbackSession.TotalWatchedSec, precision: 5);
        Assert.Equal(120.0, playbackSession.LastPositionSec);

        var playbackIntervals = await context.PlaybackIntervals.IgnoreQueryFilters().OrderBy(iv => iv.StartSec).ToListAsync();
        Assert.Equal(2, playbackIntervals.Count);
        Assert.Equal(playbackSession.Id, playbackIntervals[0].PlaybackSessionId);
        Assert.Equal(7, playbackIntervals[0].UserId);
        Assert.Equal(42.5, playbackIntervals[0].StartSec);
        Assert.Equal(48.0, playbackIntervals[0].EndSec);
        Assert.Equal(73.5, playbackIntervals[1].StartSec);
        Assert.Equal(120.0, playbackIntervals[1].EndSec);

        var ratingRows = await context.Ratings.IgnoreQueryFilters().OrderBy(rating => rating.Aspect).ToListAsync();
        Assert.Equal(2, ratingRows.Count);
        Assert.Collection(
            ratingRows,
            rating =>
            {
                Assert.Equal(7, rating.UserId);
                Assert.Equal("audio", rating.Aspect);
                Assert.Equal(35, rating.Value);
            },
            rating =>
            {
                Assert.Equal(7, rating.UserId);
                Assert.Equal("overall", rating.Aspect);
                Assert.Equal(88, rating.Value);
            });
    }

    private static ScenesController CreateScenesController(CoveContext context, CurrentPrincipalAccessor principalAccessor)
    {
        var repository = new SceneRepository(context);
        var engagementService = new UserEngagementService(context, principalAccessor);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new ScenesController(repository, context, null!, null!, null!, memoryCache, null!, null!, null!, engagementService, null, principalAccessor);
    }

    private static PlaybackController CreatePlaybackController(CoveContext context, CurrentPrincipalAccessor principalAccessor)
    {
        var engagementService = new UserEngagementService(context, principalAccessor);
        return new PlaybackController(engagementService, principalAccessor);
    }

    private static CovePrincipal CreatePrincipal(int userId) => new()
    {
        UserId = userId,
        Username = $"user-{userId}",
        Kind = PrincipalKind.User,
        Roles = new HashSet<string>(),
        Permissions = new HashSet<string>
        {
            Permissions.ScenesRead,
        },
    };

    private static async Task<TestContextScope> CreateContextAsync()
    {
        var principalAccessor = new CurrentPrincipalAccessor();
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;

        var context = new SceneEngagementTestContext(options, principalAccessor);
        await context.Database.EnsureCreatedAsync();
        return new TestContextScope(context, connection, principalAccessor);
    }

    private sealed class SceneEngagementTestContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor principalAccessor) : CoveContext(options, principalAccessor)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Scene>().Ignore(scene => scene.CustomFields);
            modelBuilder.Entity<Performer>().Ignore(performer => performer.CustomFields);
            modelBuilder.Entity<Tag>().Ignore(tag => tag.CustomFields);
            modelBuilder.Entity<Studio>().Ignore(studio => studio.CustomFields);
            modelBuilder.Entity<Gallery>().Ignore(gallery => gallery.CustomFields);
            modelBuilder.Entity<Image>().Ignore(image => image.CustomFields);
            modelBuilder.Entity<Group>().Ignore(group => group.CustomFields);
            modelBuilder.Entity<Face>().Ignore(face => face.CustomFields);
        }
    }

    private sealed class TestContextScope(CoveContext context, SqliteConnection connection, CurrentPrincipalAccessor principalAccessor) : IAsyncDisposable
    {
        public CoveContext Context { get; } = context;

        public CurrentPrincipalAccessor PrincipalAccessor { get; } = principalAccessor;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
