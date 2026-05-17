using Cove.Api.Controllers;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Data;
using Cove.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests;

public class EntityDetailCountControllerTests
{
    [Fact]
    public async Task PerformerDetail_UsesLiveUsageCountsInsteadOfStoredCounters()
    {
        await using var context = CreateContext();

        var performer = new Performer { Name = "Performer" };
        var scene = new Scene { Title = "Scene" };
        var image = new Image { Title = "Image" };
        var gallery = new Gallery { Title = "Gallery" };
        var group = new Group { Name = "Group" };
        context.AddRange(performer, scene, image, gallery, group);
        await context.SaveChangesAsync();

        context.AddRange(
            new ScenePerformer { SceneId = scene.Id, PerformerId = performer.Id },
            new ImagePerformer { ImageId = image.Id, PerformerId = performer.Id },
            new GalleryPerformer { GalleryId = gallery.Id, PerformerId = performer.Id },
            new GroupItem
            {
                GroupId = group.Id,
                Kind = GroupItemKind.Scene,
                HostType = "scene",
                HostId = scene.Id,
                SceneId = scene.Id,
            });
        await context.SaveChangesAsync();

        var storedPerformer = await context.Performers.SingleAsync(candidate => candidate.Id == performer.Id);
        storedPerformer.SceneCount = 0;
        storedPerformer.ImageCount = 0;
        storedPerformer.GalleryCount = 0;
        await context.SaveChangesAsync();

        var controller = new PerformersController(
            new PerformerRepository(context),
            null!,
            null!,
            context,
            null!,
            null!);

        var detailResult = await controller.GetById(performer.Id, CancellationToken.None);
        var detail = Assert.IsType<OkObjectResult>(detailResult.Result).Value as PerformerDto;
        Assert.NotNull(detail);
        Assert.Equal(1, detail!.SceneCount);
        Assert.Equal(1, detail.ImageCount);
        Assert.Equal(1, detail.GalleryCount);
        Assert.Equal(1, detail.GroupCount);
    }

    [Fact]
    public async Task StudioDetail_UsesLiveUsageCountsInsteadOfStoredCounters()
    {
        await using var context = CreateContext();

        var studio = new Studio { Name = "Studio" };
        var childStudio = new Studio { Name = "Child", Parent = studio };
        var scene = new Scene { Title = "Scene", Studio = studio };
        var image = new Image { Title = "Image", Studio = studio };
        var gallery = new Gallery { Title = "Gallery", Studio = studio };
        var group = new Group { Name = "Group", Studio = studio };
        var performerA = new Performer { Name = "A" };
        var performerB = new Performer { Name = "B" };
        context.AddRange(studio, childStudio, scene, image, gallery, group, performerA, performerB);
        await context.SaveChangesAsync();

        context.AddRange(
            new ScenePerformer { SceneId = scene.Id, PerformerId = performerA.Id },
            new ScenePerformer { SceneId = scene.Id, PerformerId = performerB.Id });
        await context.SaveChangesAsync();

        var storedStudio = await context.Studios.SingleAsync(candidate => candidate.Id == studio.Id);
        storedStudio.SceneCount = 0;
        storedStudio.ImageCount = 0;
        storedStudio.GalleryCount = 0;
        storedStudio.GroupCount = 0;
        storedStudio.PerformerCount = 0;
        storedStudio.ChildStudioCount = 0;
        await context.SaveChangesAsync();

        var controller = new StudiosController(
            new StudioRepository(context),
            null!,
            context,
            null!,
            null!);

        var detailResult = await controller.GetById(studio.Id, CancellationToken.None);
        var detail = Assert.IsType<OkObjectResult>(detailResult.Result).Value as StudioDto;
        Assert.NotNull(detail);
        Assert.Equal(1, detail!.SceneCount);
        Assert.Equal(1, detail.ImageCount);
        Assert.Equal(1, detail.GalleryCount);
        Assert.Equal(1, detail.GroupCount);
        Assert.Equal(2, detail.PerformerCount);
        Assert.Equal(1, detail.ChildStudioCount);
    }

    private static CoveContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"entity-detail-counts-{Guid.NewGuid():N}")
            .Options;

        return new TestCoveContext(options);
    }

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}