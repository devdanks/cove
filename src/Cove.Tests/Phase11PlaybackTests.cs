using Cove.Api.Controllers;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Entities.Auth;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests;

public sealed class Phase11PlaybackTests
{
    [Fact]
    public async Task PlaybackController_PersistsSceneIntervals()
    {
        await using var scope = await CreateContextAsync();
        scope.Context.Scenes.Add(new Scene { Title = "Playback Scene" });
        await scope.Context.SaveChangesAsync();
        var sceneId = await scope.Context.Scenes.Select(scene => scene.Id).SingleAsync();

        scope.PrincipalAccessor.Set(CreatePrincipal(7));
        var controller = CreateController(scope.Context, scope.PrincipalAccessor);
        var sessionId = Guid.NewGuid();

        var result = await controller.RecordIntervals(new PlaybackIntervalsRequestDto(
            "scene",
            sceneId,
            sessionId,
            180.0,
            42.0,
            "paused",
            [new PlaybackIntervalInputDto(0.0, 30.0), new PlaybackIntervalInputDto(30.0, 42.0)]), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        var session = await scope.Context.PlaybackSessions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(7, session.UserId);
        Assert.Equal(InteractionHostType.Scene, session.HostType);
        Assert.Equal(sceneId, session.HostId);
        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal(42.0, session.TotalWatchedSec, precision: 5);
        Assert.Equal(42.0, session.LastPositionSec);
        Assert.Equal(PlaybackSessionState.Paused, session.State);

        var intervals = await scope.Context.PlaybackIntervals.IgnoreQueryFilters().OrderBy(interval => interval.StartSec).ToListAsync();
        Assert.Equal(2, intervals.Count);
        Assert.Equal((0.0, 30.0), (intervals[0].StartSec, intervals[0].EndSec));
        Assert.Equal((30.0, 42.0), (intervals[1].StartSec, intervals[1].EndSec));
    }

    [Fact]
    public async Task PlaybackController_PersistsGroupIntervalsForCompilationSession()
    {
        await using var scope = await CreateContextAsync();
        scope.Context.Groups.Add(new Group { Name = "Compilation playback" });
        await scope.Context.SaveChangesAsync();
        var groupId = await scope.Context.Groups.Select(group => group.Id).SingleAsync();

        scope.PrincipalAccessor.Set(CreatePrincipal(11));
        var controller = CreateController(scope.Context, scope.PrincipalAccessor);
        var sessionId = Guid.NewGuid();

        Assert.IsType<NoContentResult>(await controller.RecordIntervals(new PlaybackIntervalsRequestDto(
            "group",
            groupId,
            sessionId,
            90.0,
            12.0,
            "active",
            [new PlaybackIntervalInputDto(0.0, 12.0)]), CancellationToken.None));

        Assert.IsType<NoContentResult>(await controller.RecordIntervals(new PlaybackIntervalsRequestDto(
            "group",
            groupId,
            sessionId,
            90.0,
            27.0,
            "paused",
            [new PlaybackIntervalInputDto(12.0, 27.0)]), CancellationToken.None));

        var session = await scope.Context.PlaybackSessions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(11, session.UserId);
        Assert.Equal(InteractionHostType.Group, session.HostType);
        Assert.Equal(groupId, session.HostId);
        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal(27.0, session.TotalWatchedSec, precision: 5);
        Assert.Equal(27.0, session.LastPositionSec);
        Assert.Equal(PlaybackSessionState.Paused, session.State);

        var intervals = await scope.Context.PlaybackIntervals.IgnoreQueryFilters().OrderBy(interval => interval.StartSec).ToListAsync();
        Assert.Equal(2, intervals.Count);
        Assert.All(intervals, interval => Assert.Equal(InteractionHostType.Group, interval.HostType));
        Assert.Equal((0.0, 12.0), (intervals[0].StartSec, intervals[0].EndSec));
        Assert.Equal((12.0, 27.0), (intervals[1].StartSec, intervals[1].EndSec));
    }

    [Fact]
    public async Task TrackingDisabled_ReturnsNoContentWithoutWritingInteractionsOrPlayback()
    {
        await using var scope = await CreateContextAsync();
        await AddUserAsync(scope, 21, "{\"tracking\":{\"enabled\":false}}");
        scope.Context.Scenes.Add(new Scene { Title = "Muted tracking" });
        await scope.Context.SaveChangesAsync();
        var sceneId = await scope.Context.Scenes.Select(scene => scene.Id).SingleAsync();

        scope.PrincipalAccessor.Set(CreatePrincipal(21));
        var playbackController = CreateController(scope.Context, scope.PrincipalAccessor);
        var engagementController = CreateEngagementController(scope.Context, scope.PrincipalAccessor);

        var interactionResult = await engagementController.RecordInteraction(
            new EngagementInteractionWriteDto("scene", sceneId, "pageVisit"),
            CancellationToken.None);
        var playbackResult = await playbackController.RecordIntervals(new PlaybackIntervalsRequestDto(
            "scene",
            sceneId,
            Guid.NewGuid(),
            120.0,
            40.0,
            "ended",
            [new PlaybackIntervalInputDto(0.0, 40.0)]), CancellationToken.None);

        Assert.IsType<NoContentResult>(interactionResult);
        Assert.IsType<NoContentResult>(playbackResult);
        Assert.Empty(await scope.Context.Interactions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await scope.Context.PlaybackSessions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await scope.Context.UserEntityAffinities.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ScenePlayback_CountsViewAtMinViewSecondsWithoutCompletion()
    {
        await using var scope = await CreateContextAsync();
        await AddUserAsync(scope, 22);
        scope.Context.Scenes.Add(new Scene { Title = "Threshold view" });
        await scope.Context.SaveChangesAsync();
        var sceneId = await scope.Context.Scenes.Select(scene => scene.Id).SingleAsync();

        scope.PrincipalAccessor.Set(CreatePrincipal(22));
        var controller = CreateController(scope.Context, scope.PrincipalAccessor);

        Assert.IsType<NoContentResult>(await controller.RecordIntervals(new PlaybackIntervalsRequestDto(
            "scene",
            sceneId,
            Guid.NewGuid(),
            100.0,
            35.0,
            "ended",
            [new PlaybackIntervalInputDto(0.0, 35.0)]), CancellationToken.None));

        var affinity = await scope.Context.UserEntityAffinities.IgnoreQueryFilters().SingleAsync();
        var session = await scope.Context.PlaybackSessions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1, affinity.ViewCount);
        Assert.Equal(0, affinity.CompleteCount);
        Assert.True(session.CountsAsView);
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public async Task ImageDetailDwell_CountsPageVisitAndViewForUser()
    {
        await using var scope = await CreateContextAsync();
        await AddUserAsync(scope, 23);
        scope.Context.Images.Add(new Image { Title = "Dwell image" });
        await scope.Context.SaveChangesAsync();
        var imageId = await scope.Context.Images.Select(image => image.Id).SingleAsync();

        scope.PrincipalAccessor.Set(CreatePrincipal(23, Permissions.ImagesRead));
        var playbackController = CreateController(scope.Context, scope.PrincipalAccessor);
        var engagementController = CreateEngagementController(scope.Context, scope.PrincipalAccessor);

        Assert.IsType<NoContentResult>(await engagementController.RecordInteraction(
            new EngagementInteractionWriteDto("image", imageId, "pageVisit"),
            CancellationToken.None));
        Assert.IsType<NoContentResult>(await playbackController.RecordIntervals(new PlaybackIntervalsRequestDto(
            "image",
            imageId,
            Guid.NewGuid(),
            6.0,
            6.0,
            "ended",
            [new PlaybackIntervalInputDto(0.0, 6.0)]), CancellationToken.None));

        var affinity = await scope.Context.UserEntityAffinities.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(AffinityHostType.Image, affinity.HostType);
        Assert.Equal(imageId, affinity.HostId);
        Assert.Equal(1, affinity.PageVisitCount);
        Assert.Equal(1, affinity.ViewCount);
    }

    [Fact]
    public async Task FinalLongLastSession_AwardsDerivedLikeAndInteraction()
    {
        await using var scope = await CreateContextAsync();
        await AddUserAsync(scope, 24);
        scope.Context.Scenes.Add(new Scene { Title = "Long session" });
        await scope.Context.SaveChangesAsync();
        var sceneId = await scope.Context.Scenes.Select(scene => scene.Id).SingleAsync();

        scope.PrincipalAccessor.Set(CreatePrincipal(24));
        var controller = CreateController(scope.Context, scope.PrincipalAccessor);

        Assert.IsType<NoContentResult>(await controller.RecordIntervals(new PlaybackIntervalsRequestDto(
            "scene",
            sceneId,
            Guid.NewGuid(),
            180.0,
            65.0,
            "ended",
            [new PlaybackIntervalInputDto(0.0, 65.0)]), CancellationToken.None));

        var affinity = await scope.Context.UserEntityAffinities.IgnoreQueryFilters().SingleAsync();
        var derivedLike = await scope.Context.Interactions.IgnoreQueryFilters().SingleAsync(interaction => interaction.Kind == InteractionKind.DerivedLike);
        Assert.Equal(1, affinity.DerivedLikeCount);
        Assert.Equal(InteractionHostType.Scene, derivedLike.HostType);
        Assert.Equal(sceneId, derivedLike.HostId);
        Assert.Equal(24, derivedLike.UserId);
    }

    private static PlaybackController CreateController(CoveContext context, CurrentPrincipalAccessor principalAccessor)
    {
        var engagementService = new UserEngagementService(context, principalAccessor);
        return new PlaybackController(engagementService, principalAccessor);
    }

    private static EntityEngagementController CreateEngagementController(CoveContext context, CurrentPrincipalAccessor principalAccessor)
    {
        var engagementService = new UserEngagementService(context, principalAccessor);
        return new EntityEngagementController(engagementService, principalAccessor);
    }

    private static CovePrincipal CreatePrincipal(int userId, params string[] permissions) => new()
    {
        UserId = userId,
        Username = $"user-{userId}",
        Kind = PrincipalKind.User,
        Roles = new HashSet<string>(),
        Permissions = new HashSet<string>
        {
            Permissions.ScenesRead,
        }.Concat(permissions).ToHashSet(),
    };

    private static async Task AddUserAsync(TestContextScope scope, int userId, string? uiPreferencesJson = null)
    {
        scope.Context.Users.Add(new User
        {
            Id = userId,
            Username = $"user-{userId}",
            PasswordHash = "test",
            UiPreferencesJson = uiPreferencesJson,
        });
        await scope.Context.SaveChangesAsync();
    }

    private static async Task<TestContextScope> CreateContextAsync()
    {
        var principalAccessor = new CurrentPrincipalAccessor();
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;

        var context = new PlaybackTestContext(options, principalAccessor);
        await context.Database.EnsureCreatedAsync();
        return new TestContextScope(context, connection, principalAccessor);
    }

    private sealed class PlaybackTestContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor principalAccessor) : CoveContext(options, principalAccessor)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Scene>().Ignore(scene => scene.CustomFields);
            modelBuilder.Entity<Image>().Ignore(image => image.CustomFields);
            modelBuilder.Entity<Tag>().Ignore(tag => tag.CustomFields);
            modelBuilder.Entity<Studio>().Ignore(studio => studio.CustomFields);
            modelBuilder.Entity<Performer>().Ignore(performer => performer.CustomFields);
            modelBuilder.Entity<Gallery>().Ignore(gallery => gallery.CustomFields);
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
            PrincipalAccessor.Set(null);
        }
    }
}