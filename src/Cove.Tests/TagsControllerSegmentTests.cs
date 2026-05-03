using Cove.Api.Controllers;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests;

public class TagsControllerSegmentTests
{
    [Fact]
    public async Task TagDetail_UsesSceneSegmentCountsAndReturnsTagSegments()
    {
        await using var context = CreateContext();
        var tag = new Tag { Name = "Body" };
        var otherTag = new Tag { Name = "Other" };
        var scene = new Scene { Title = "Imported Scene" };
        context.AddRange(tag, otherTag, scene);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                TagId = tag.Id,
                Kind = "tag",
                SourceKey = "import:stash-ai-server",
                StartSec = 8.0,
                EndSec = 11.0,
                Title = "AI body",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                TagId = tag.Id,
                Kind = "tag",
                SourceKey = "user",
                StartSec = 15.0,
                EndSec = 18.0,
                Title = "Manual body",
            },
            new Segment
            {
                HostType = SegmentHostType.Image,
                HostId = 999,
                TagId = tag.Id,
                Kind = "tag",
                SourceKey = "import:image",
                StartSec = 0,
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                TagId = otherTag.Id,
                Kind = "tag",
                SourceKey = "import:stash-ai-server",
                StartSec = 20.0,
                EndSec = 25.0,
            });
        await context.SaveChangesAsync();

        var controller = new TagsController(null!, context, null!);

        var detailResult = await controller.GetById(tag.Id, CancellationToken.None);
        var detail = Assert.IsType<OkObjectResult>(detailResult.Result).Value as TagDetailDto;
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.SegmentCount);

        var segmentsResult = await controller.GetSegments(tag.Id, 100, CancellationToken.None);
        var segments = Assert.IsType<OkObjectResult>(segmentsResult.Result).Value as IReadOnlyList<TagSegmentWallDto>;
        Assert.NotNull(segments);
        Assert.Equal(2, segments!.Count);
        Assert.All(segments, segment =>
        {
            Assert.Equal(scene.Id, segment.SceneId);
            Assert.Equal(scene.Title, segment.SceneTitle);
        });
    }

    [Fact]
    public async Task TagSceneMarkerCount_TracksSceneSegmentsInsteadOfLegacyMarkers()
    {
        await using var context = CreateContext();
        var tag = new Tag { Name = "Body" };
        var scene = new Scene { Title = "Imported Scene" };
        context.AddRange(tag, scene);
        await context.SaveChangesAsync();

        var segment = new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = scene.Id,
            TagId = tag.Id,
            Kind = "tag",
            SourceKey = "user",
            StartSec = 12.0,
            EndSec = 20.0,
        };

        context.Segments.Add(segment);
        await context.SaveChangesAsync();

        var addedTag = await context.Tags.AsNoTracking().SingleAsync(candidate => candidate.Id == tag.Id);
        Assert.Equal(1, addedTag.SceneMarkerCount);

        context.Segments.Remove(segment);
        await context.SaveChangesAsync();

        var removedTag = await context.Tags.AsNoTracking().SingleAsync(candidate => candidate.Id == tag.Id);
        Assert.Equal(0, removedTag.SceneMarkerCount);
    }

    [Fact]
    public async Task TagDetail_RoundTripsPlayerBarOverrides()
    {
        await using var context = CreateContext();
        var tag = new Tag
        {
            Name = "Body",
            ShowAsSegment = true,
            SegmentColorOverride = "#44aaee",
            SegmentLaneOverride = 2,
        };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        var controller = new TagsController(null!, context, null!);

        var detailResult = await controller.GetById(tag.Id, CancellationToken.None);
        var detailOk = Assert.IsType<OkObjectResult>(detailResult.Result);
        var detail = Assert.IsType<TagDetailDto>(detailOk.Value);
        Assert.True(detail.ShowAsSegment);
        Assert.Equal("#44aaee", detail.SegmentColorOverride);
        Assert.Equal(2, detail.SegmentLaneOverride);

        var updateResult = await controller.Update(tag.Id, new TagUpdateDto(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null), CancellationToken.None);
        var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updated = Assert.IsType<TagDetailDto>(updateOk.Value);
        Assert.False(updated.ShowAsSegment);
        Assert.Null(updated.SegmentColorOverride);
        Assert.Null(updated.SegmentLaneOverride);
    }

    [Fact]
    public async Task GetMarkerStrings_UsesSceneSegmentTitlesInsteadOfLegacyMarkers()
    {
        await using var context = CreateContext();
        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = 1,
                SourceKey = "user",
                StartSec = 5.0,
                Title = "Manual body",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = 2,
                SourceKey = "user",
                StartSec = 15.0,
                Title = "Manual body",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = 3,
                SourceKey = "user",
                StartSec = 25.0,
                Title = "AI body",
            },
            new Segment
            {
                HostType = SegmentHostType.Image,
                HostId = 4,
                SourceKey = "user",
                StartSec = 0,
                Title = "Image-only title",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = 5,
                SourceKey = "user",
                StartSec = 35.0,
                Title = null,
            });
        await context.SaveChangesAsync();

        var controller = new TagsController(null!, context, null!);

        var alphabeticalResult = await controller.GetMarkerStrings(null, null, CancellationToken.None);
        var alphabetical = Assert.IsType<OkObjectResult>(alphabeticalResult.Result).Value as List<string>;
        Assert.NotNull(alphabetical);
        Assert.Equal(["AI body", "Manual body"], alphabetical);

        var countedResult = await controller.GetMarkerStrings(null, "count", CancellationToken.None);
        var counted = Assert.IsType<OkObjectResult>(countedResult.Result).Value as List<string>;
        Assert.NotNull(counted);
        Assert.Equal("Manual body", counted![0]);
        Assert.DoesNotContain("Image-only title", counted);

        var filteredResult = await controller.GetMarkerStrings("manual", null, CancellationToken.None);
        var filtered = Assert.IsType<OkObjectResult>(filteredResult.Result).Value as List<string>;
        Assert.NotNull(filtered);
        Assert.Equal(["Manual body"], filtered);
    }

    private static CoveContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"tags-controller-segments-{Guid.NewGuid():N}")
            .Options;

        return new TestCoveContext(options);
    }

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options) : CoveContext(options)
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
}