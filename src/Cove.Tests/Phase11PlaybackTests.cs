using Cove.Api.Controllers;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
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

    private static PlaybackController CreateController(CoveContext context, CurrentPrincipalAccessor principalAccessor)
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