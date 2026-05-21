using Cove.Api.Controllers;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests;

public class DynamicGroupsAndBookmarksTests
{
    [Fact]
    public async Task BookmarkToggleAndBatch_AreUserScoped()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        context.Scenes.Add(new Scene { Title = "Saved Scene" });
        await context.SaveChangesAsync();
        var sceneId = await context.Scenes.Select(scene => scene.Id).SingleAsync();
        var controller = new BookmarksController(context, principalAccessor);

        principalAccessor.Set(CreatePrincipal(7));
        var saveResult = await controller.Toggle(new BookmarkToggleDto(AffinityHostType.Scene, sceneId, true), CancellationToken.None);
        var saveOk = Assert.IsType<OkObjectResult>(saveResult.Result);
        var saveState = Assert.IsType<BookmarkStateDto>(saveOk.Value);
        Assert.True(saveState.Saved);

        var userBatchResult = await controller.Batch(new BookmarkBatchRequestDto(AffinityHostType.Scene, [sceneId]), CancellationToken.None);
        var userBatchOk = Assert.IsType<OkObjectResult>(userBatchResult.Result);
        var userStates = Assert.IsAssignableFrom<IReadOnlyList<BookmarkStateDto>>(userBatchOk.Value);
        Assert.True(userStates.Single().Saved);

        context.ChangeTracker.Clear();
        principalAccessor.Set(CreatePrincipal(9));
        var otherBatchResult = await controller.Batch(new BookmarkBatchRequestDto(AffinityHostType.Scene, [sceneId]), CancellationToken.None);
        var otherBatchOk = Assert.IsType<OkObjectResult>(otherBatchResult.Result);
        var otherStates = Assert.IsAssignableFrom<IReadOnlyList<BookmarkStateDto>>(otherBatchOk.Value);
        Assert.False(otherStates.Single().Saved);

        Assert.Equal(1, await context.UserBookmarks.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SaveForLaterDynamicGroup_ResolvesBookmarkedItemsNewestFirst()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        principalAccessor.Set(CreatePrincipal(7));

        var firstScene = new Scene { Title = "First" };
        var secondScene = new Scene { Title = "Second" };
        var group = new Group { Name = "Save for Later", Kind = GroupKind.Dynamic, QuerySourceKey = DynamicGroupResolver.SaveForLaterSourceKey };
        context.AddRange(firstScene, secondScene, group);
        await context.SaveChangesAsync();
        context.UserBookmarks.AddRange(
            new UserBookmark { UserId = 7, HostType = AffinityHostType.Scene, HostId = firstScene.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-10) },
            new UserBookmark { UserId = 7, HostType = AffinityHostType.Scene, HostId = secondScene.Id, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, principalAccessor);
        var items = await resolver.ResolveDtosAsync(group.Id, forceRefresh: true, CancellationToken.None);

        Assert.Equal(["Second", "First"], items.Select(item => item.Title ?? string.Empty).ToArray());
        Assert.All(items, item => Assert.Equal("scene", item.HostType));
        Assert.Equal(2, await context.Groups.Where(item => item.Id == group.Id).Select(item => item.CachedItemCount).SingleAsync());
    }

    [Fact]
    public async Task ContinueWatchingDynamicGroup_ExcludesCompletedScenes()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        principalAccessor.Set(CreatePrincipal(7));

        var unfinished = new Scene { Title = "Unfinished", MaxDuration = 100 };
        var complete = new Scene { Title = "Complete", MaxDuration = 100 };
        var group = new Group { Name = "Continue Watching", Kind = GroupKind.Dynamic, QuerySourceKey = DynamicGroupResolver.ContinueWatchingSourceKey };
        context.AddRange(unfinished, complete, group);
        await context.SaveChangesAsync();
        context.UserEntityAffinities.AddRange(
            new UserEntityAffinity { UserId = 7, HostType = AffinityHostType.Scene, HostId = unfinished.Id, LastConsumedAt = DateTime.UtcNow, LastPositionSec = 42, TotalConsumedSec = 42 },
            new UserEntityAffinity { UserId = 7, HostType = AffinityHostType.Scene, HostId = complete.Id, LastConsumedAt = DateTime.UtcNow, LastPositionSec = 98, TotalConsumedSec = 96 });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, principalAccessor);
        var items = await resolver.ResolveDtosAsync(group.Id, forceRefresh: true, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Unfinished", item.Title);
        Assert.Equal(unfinished.Id, item.SceneId);
    }

    [Fact]
    public async Task DynamicGroupPagination_ReturnsRequestedPageAndTotal()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        principalAccessor.Set(CreatePrincipal(7));

        var firstScene = new Scene { Title = "First" };
        var secondScene = new Scene { Title = "Second" };
        var thirdScene = new Scene { Title = "Third" };
        var group = new Group { Name = "Save for Later", Kind = GroupKind.Dynamic, QuerySourceKey = DynamicGroupResolver.SaveForLaterSourceKey };
        context.AddRange(firstScene, secondScene, thirdScene, group);
        await context.SaveChangesAsync();
        context.UserBookmarks.AddRange(
            new UserBookmark { UserId = 7, HostType = AffinityHostType.Scene, HostId = firstScene.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-30) },
            new UserBookmark { UserId = 7, HostType = AffinityHostType.Scene, HostId = secondScene.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-20) },
            new UserBookmark { UserId = 7, HostType = AffinityHostType.Scene, HostId = thirdScene.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-10) });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, principalAccessor);
        var page = await resolver.ResolvePageDtosAsync(group.Id, new FindFilter { Page = 2, PerPage = 2 }, forceRefresh: true, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Page);
        var item = Assert.Single(page.Items);
        Assert.Equal("First", item.Title);
    }

    [Fact]
    public async Task EnsureBuiltInGroupsAsync_CreatesMissingBuiltInGroups()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var resolver = CreateResolver(context, scope.PrincipalAccessor);

        await resolver.EnsureBuiltInGroupsAsync(CancellationToken.None);

        var groups = await context.Groups
            .OrderBy(group => group.Name)
            .Select(group => new { group.Name, group.QuerySourceKey, group.Kind })
            .ToListAsync();

        Assert.Equal(
            [
                ("Continue Watching", DynamicGroupResolver.ContinueWatchingSourceKey, GroupKind.Dynamic),
                ("Save for Later", DynamicGroupResolver.SaveForLaterSourceKey, GroupKind.Dynamic),
                ("Watch History", DynamicGroupResolver.WatchHistorySourceKey, GroupKind.Dynamic),
            ],
            groups.Select(group => (group.Name, group.QuerySourceKey, group.Kind)).ToArray());
    }

    [Fact]
    public async Task FilterDynamicGroupSource_UsesSavedSceneFilter()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        principalAccessor.Set(CreatePrincipal(7));

        var included = new Scene { Title = "Included", Organized = true };
        var excluded = new Scene { Title = "Excluded", Organized = false };
        var group = new Group
        {
            Name = "Organized Scenes",
            Kind = GroupKind.Dynamic,
            QuerySourceKey = DynamicGroupResolver.FilterSourceKey,
            QueryJson = "{\"entityType\":\"scene\",\"findFilter\":{\"sort\":\"title\",\"direction\":\"asc\"},\"objectFilter\":{\"organized\":true}}",
        };
        context.AddRange(included, excluded, group);
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, principalAccessor, includeFilterSource: true);
        var page = await resolver.ResolvePageDtosAsync(group.Id, new FindFilter { Page = 1, PerPage = 10 }, forceRefresh: true, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(included.Id, item.SceneId);
        Assert.Equal("Included", item.Title);
    }

    [Fact]
    public async Task FilterDynamicGroupSource_UsesUppercasePerformerCriterion()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        principalAccessor.Set(CreatePrincipal(7));

        var performer = new Performer { Name = "Matched Performer" };
        var included = new Scene { Title = "Included" };
        included.ScenePerformers.Add(new ScenePerformer { Performer = performer });
        var excluded = new Scene { Title = "Excluded" };
        context.AddRange(included, excluded);
        await context.SaveChangesAsync();

        var group = new Group
        {
            Name = "Performer Scenes",
            Kind = GroupKind.Dynamic,
            QuerySourceKey = DynamicGroupResolver.FilterSourceKey,
            QueryJson = "{\"entityTypes\":[\"scene\"],\"findFilters\":{\"scene\":{\"sort\":\"title\",\"direction\":\"asc\"}},\"objectFilters\":{\"scene\":{\"performersCriterion\":{\"value\":[" + performer.Id + "],\"modifier\":\"INCLUDES_ALL\"}}}}",
        };
        context.Add(group);
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, principalAccessor, includeFilterSource: true);
        var page = await resolver.ResolvePageDtosAsync(group.Id, new FindFilter { Page = 1, PerPage = 10 }, forceRefresh: true, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(included.Id, item.SceneId);
        Assert.Equal("Included", item.Title);
    }

    [Fact]
    public async Task FilterDynamicGroupSource_ReturnsTotalAcrossEntityTypesWhenPageIsFilled()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        principalAccessor.Set(CreatePrincipal(7));

        for (var index = 0; index < 40; index++)
            context.Scenes.Add(new Scene { Title = $"Scene {index:D2}" });
        for (var index = 0; index < 25; index++)
            context.Images.Add(new Image { Title = $"Image {index:D2}" });

        var group = new Group
        {
            Name = "Mixed Dynamic",
            Kind = GroupKind.Dynamic,
            QuerySourceKey = DynamicGroupResolver.FilterSourceKey,
            QueryJson = "{\"entityTypes\":[\"scene\",\"image\"],\"findFilters\":{\"scene\":{\"sort\":\"title\",\"direction\":\"asc\"},\"image\":{\"sort\":\"title\",\"direction\":\"asc\"}}}",
        };
        context.Groups.Add(group);
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, principalAccessor, includeFilterSource: true);
        var page = await resolver.ResolvePageDtosAsync(group.Id, new FindFilter { Page = 1, PerPage = 40 }, forceRefresh: true, CancellationToken.None);

        Assert.Equal(65, page.TotalCount);
        Assert.Equal(40, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal("scene", item.HostType));
    }

    [Fact]
    public async Task SnapshotDynamicGroup_WritesStaticGroupItems()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        var principalAccessor = scope.PrincipalAccessor;
        principalAccessor.Set(CreatePrincipal(7));

        var scene = new Scene { Title = "Snapshot Scene" };
        var group = new Group { Name = "Saved Snapshot", Kind = GroupKind.Dynamic, QuerySourceKey = DynamicGroupResolver.SaveForLaterSourceKey };
        context.AddRange(scene, group);
        await context.SaveChangesAsync();
        context.UserBookmarks.Add(new UserBookmark { UserId = 7, HostType = AffinityHostType.Scene, HostId = scene.Id, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, principalAccessor);
        await resolver.SnapshotAsync(group.Id, CancellationToken.None);

        var updatedGroup = await context.Groups.Include(item => item.GroupItems).SingleAsync(item => item.Id == group.Id);
        Assert.Equal(GroupKind.Static, updatedGroup.Kind);
        Assert.Null(updatedGroup.QuerySourceKey);
        var item = Assert.Single(updatedGroup.GroupItems);
        Assert.Equal("scene", item.HostType);
        Assert.Equal(scene.Id, item.HostId);
        Assert.Equal(scene.Id, item.SceneId);
        Assert.Equal(GroupItemKind.Scene, item.Kind);
    }

    [Fact]
    public async Task GroupRepository_SortOrderSort_UsesManualOrder()
    {
        await using var scope = CreateContext();
        var context = scope.Context;
        context.Groups.AddRange(
            new Group { Name = "Second", SortOrder = 20 },
            new Group { Name = "First", SortOrder = 10 },
            new Group { Name = "Third", SortOrder = 30 });
        await context.SaveChangesAsync();

        var repository = new GroupRepository(context);
        var (items, totalCount) = await repository.FindAsync(null, new FindFilter { Sort = "sort_order", Direction = SortDirection.Asc, Page = 1, PerPage = 10 }, CancellationToken.None);

        Assert.Equal(3, totalCount);
        Assert.Equal(["First", "Second", "Third"], items.Select(group => group.Name).ToArray());
    }

    private static DynamicGroupResolver CreateResolver(CoveContext context, CurrentPrincipalAccessor principalAccessor, bool includeFilterSource = false)
    {
        var sources = new List<IDynamicGroupSource>
        {
            new SaveForLaterDynamicGroupSource(context),
            new WatchHistoryDynamicGroupSource(context),
            new ContinueWatchingDynamicGroupSource(context),
        };
        if (includeFilterSource)
            sources.Add(new FilterDynamicGroupSource(context, new SceneRepository(context), new ImageRepository(context)));

        return new DynamicGroupResolver(context, sources, principalAccessor);
    }

    private static CovePrincipal CreatePrincipal(int userId) => new()
    {
        UserId = userId,
        Username = $"user-{userId}",
        Kind = PrincipalKind.User,
        Roles = new HashSet<string>(),
        Permissions = new HashSet<string> { Permissions.All },
    };

    private static TestContextScope CreateContext()
    {
        var principalAccessor = new CurrentPrincipalAccessor();
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"dynamic-groups-{Guid.NewGuid():N}")
            .Options;
        return new TestContextScope(new DynamicGroupTestContext(options, principalAccessor), principalAccessor);
    }

    private sealed class DynamicGroupTestContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor principalAccessor) : CoveContext(options, principalAccessor);

    private sealed class TestContextScope(CoveContext context, CurrentPrincipalAccessor principalAccessor) : IAsyncDisposable
    {
        public CoveContext Context { get; } = context;
        public CurrentPrincipalAccessor PrincipalAccessor { get; } = principalAccessor;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            PrincipalAccessor.Set(null);
        }
    }
}
