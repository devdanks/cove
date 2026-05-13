using System.Text.Json;
using Cove.Api.Controllers;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Tests;

public class SegmentCoreControllerTests
{
    [Fact]
    public async Task SceneSegmentsController_CanCreateUpdateListAndDeleteSceneSegments()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Segment Scene" };
        var tag = new Tag { Name = "Face" };
        context.Scenes.Add(scene);
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        var spanResolver = new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions()));
        var controller = new SceneSegmentsController(context, spanResolver);
        var createDto = new SegmentCreateDto(
            12.5,
            18.25,
            tag.Id,
            "face",
            99,
            ParseJson("{" + "\"confidencePeaks\":[0.91,0.96]}"),
            "ext:ai.faces",
            "run-1",
            0.96f,
            "Lead face",
            "#ffaa00");

        var createResult = await controller.Create(scene.Id, createDto, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var createdDto = Assert.IsType<SegmentDto>(created.Value);
        Assert.Equal(scene.Id, createdDto.HostId);
        Assert.Equal(tag.Id, createdDto.TagId);
        Assert.Equal("Face", createdDto.TagName);
        Assert.Equal("face", createdDto.Kind);
        Assert.Equal("ext:ai.faces", createdDto.SourceKey);
        Assert.True(createdDto.Payload.HasValue);
        Assert.Equal(JsonValueKind.Array, createdDto.Payload.Value.GetProperty("confidencePeaks").ValueKind);

        var listResult = await controller.GetByScene(scene.Id, CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var listed = Assert.IsAssignableFrom<IReadOnlyList<SegmentDto>>(listOk.Value);
        var listedSegment = Assert.Single(listed);
        Assert.Equal(createdDto.Id, listedSegment.Id);

        var updateDto = new SegmentUpdateDto(
            13.0,
            null,
            null,
            "face.track",
            100,
            ParseJson("{" + "\"frame\":321}"),
            "user",
            null,
            0.75f,
            "Updated face",
            null);

        var updateResult = await controller.Update(scene.Id, createdDto.Id, updateDto, CancellationToken.None);
        var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updatedDto = Assert.IsType<SegmentDto>(updateOk.Value);
        Assert.Equal(13.0, updatedDto.StartSec);
        Assert.Null(updatedDto.EndSec);
        Assert.Null(updatedDto.TagId);
        Assert.Equal("face.track", updatedDto.Kind);
        Assert.Equal("user", updatedDto.SourceKey);
        Assert.True(updatedDto.Payload.HasValue);
        Assert.Equal(321, updatedDto.Payload.Value.GetProperty("frame").GetInt32());

        var deleteResult = await controller.Delete(scene.Id, createdDto.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);

        var finalListResult = await controller.GetByScene(scene.Id, CancellationToken.None);
        var finalListOk = Assert.IsType<OkObjectResult>(finalListResult.Result);
        var finalList = Assert.IsAssignableFrom<IReadOnlyList<SegmentDto>>(finalListOk.Value);
        Assert.Empty(finalList);
    }

    [Fact]
    public async Task SceneSegmentsController_CanResolveSceneSpansAndSpanDetail()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Resolved Segment Scene" };
        var tag = new Tag { Name = "Highlights" };
        context.Scenes.Add(scene);
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        var profile = new SegmentDisplayProfile
        {
            Name = "Strict",
            UserId = 7,
            IsDefault = true,
            Version = 1,
        };
        context.SegmentDisplayProfiles.Add(profile);
        await context.SaveChangesAsync();

        context.SegmentDisplayRules.Add(new SegmentDisplayRule
        {
            ProfileId = profile.Id,
            UserId = 7,
            SourceKey = "ext:ai.%",
            MergeGapSec = 1.0,
            Visible = true,
        });
        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 10,
                EndSec = 12,
                TagId = tag.Id,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 12.5,
                EndSec = 14,
                TagId = tag.Id,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 20,
                EndSec = 21,
                TagId = tag.Id,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            });
        await context.SaveChangesAsync();

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(CreatePrincipal(7));
        var spanResolver = new SegmentSpanResolver(context, principalAccessor, new MemoryCache(new MemoryCacheOptions()));
        var controller = new SceneSegmentsController(context, spanResolver);

        var spansResult = await controller.GetSpans(scene.Id, profile.Id, CancellationToken.None);
        var spansOk = Assert.IsType<OkObjectResult>(spansResult.Result);
        var spansDto = Assert.IsType<SceneResolvedSpansDto>(spansOk.Value);
        Assert.Equal(profile.Id, spansDto.ProfileId);
        Assert.Equal(2, spansDto.Spans.Count);
        var mergedSpan = spansDto.Spans[0];
        Assert.Equal(10, mergedSpan.StartSec);
        Assert.Equal(14, mergedSpan.EndSec);
        Assert.Equal(2, mergedSpan.SegmentIds.Count);

        var detailResult = await controller.GetSpanDetail(scene.Id, mergedSpan.SpanKey, profile.Id, CancellationToken.None);
        var detailOk = Assert.IsType<OkObjectResult>(detailResult.Result);
        var detailDto = Assert.IsType<ResolvedSpanDetailDto>(detailOk.Value);
        Assert.Equal(scene.Id, detailDto.SceneId);
        Assert.Equal("Resolved Segment Scene", detailDto.SceneTitle);
        Assert.Equal(2, detailDto.Intervals.Count);
        Assert.Equal(10, detailDto.Intervals[0].StartSec);
        Assert.Equal(12, detailDto.Intervals[0].EndSec);
        Assert.Equal(12.5, detailDto.Intervals[1].StartSec);
        Assert.Equal(14, detailDto.Intervals[1].EndSec);
    }

    [Fact]
    public async Task SceneSegmentsController_CanQueryDerivedSpans()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Derived Segment Scene" };
        context.Scenes.Add(scene);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 10,
                EndSec = 12,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 20,
                EndSec = 22,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 11,
                EndSec = 13,
                Kind = "user.face",
                SourceKey = "user",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 21,
                EndSec = 25,
                Kind = "user.face",
                SourceKey = "user",
            });
        await context.SaveChangesAsync();

        var spanResolver = new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions()));
        var controller = new SceneSegmentsController(context, spanResolver);

        var queryResult = await controller.QuerySpans(scene.Id, new SegmentSpanQueryRequestDto(
            null,
            "intersection",
            [
                new SegmentSpanOperandDto("ext:ai.faces", null, null, null),
                new SegmentSpanOperandDto("user", null, null, null),
            ],
            0,
            0), CancellationToken.None);
        var queryOk = Assert.IsType<OkObjectResult>(queryResult.Result);
        var queryDto = Assert.IsType<ResolvedSpanListDto>(queryOk.Value);
        Assert.Equal(2, queryDto.Spans.Count);
        Assert.Equal(11, queryDto.Spans[0].StartSec);
        Assert.Equal(12, queryDto.Spans[0].EndSec);
        Assert.Equal(21, queryDto.Spans[1].StartSec);
        Assert.Equal(22, queryDto.Spans[1].EndSec);
    }

    [Fact]
    public async Task SceneSegmentsController_TagLevelOverride_WinsOverHiddenRuleAndSetsAppearance()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Tag override scene" };
        var tag = new Tag
        {
            Name = "Highlight",
            ShowAsSegment = true,
            SegmentColorOverride = "#22cc88",
            SegmentLaneOverride = 3,
        };
        context.AddRange(scene, tag);
        await context.SaveChangesAsync();

        var profile = new SegmentDisplayProfile
        {
            Name = "Default",
            UserId = 7,
            IsDefault = true,
            Version = 1,
        };
        context.SegmentDisplayProfiles.Add(profile);
        await context.SaveChangesAsync();

        context.SegmentDisplayRules.Add(new SegmentDisplayRule
        {
            ProfileId = profile.Id,
            UserId = 7,
            TagId = tag.Id,
            Visible = false,
            Lane = 9,
            ColorOverride = "#ff0000",
        });
        context.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = scene.Id,
            StartSec = 5,
            EndSec = 7,
            TagId = tag.Id,
            Kind = "action",
            SourceKey = "ext:test",
        });
        await context.SaveChangesAsync();

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(CreatePrincipal(7));
        var controller = new SceneSegmentsController(context, new SegmentSpanResolver(context, principalAccessor, new MemoryCache(new MemoryCacheOptions())));

        var spansResult = await controller.GetSpans(scene.Id, profile.Id, CancellationToken.None);
        var spansOk = Assert.IsType<OkObjectResult>(spansResult.Result);
        var spansDto = Assert.IsType<SceneResolvedSpansDto>(spansOk.Value);
        var span = Assert.Single(spansDto.Spans);
        Assert.Equal("#22cc88", span.ColorHint);
        Assert.Equal(3, span.Lane);
    }

    [Fact]
    public async Task SceneSegmentsController_QueryDerivedSpansSupportsSecondaryTagsAndRefIds()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Derived Identity Segment Scene" };
        var primaryTag = new Tag { Name = "Primary" };
        var secondaryTag = new Tag { Name = "Secondary" };
        context.Scenes.Add(scene);
        context.Tags.AddRange(primaryTag, secondaryTag);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 10,
                EndSec = 14,
                TagId = primaryTag.Id,
                Kind = "face",
                RefId = 42,
                Payload = JsonDocument.Parse($"{{\"secondaryTagIds\":[{secondaryTag.Id}]}}"),
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 20,
                EndSec = 24,
                TagId = primaryTag.Id,
                Kind = "face",
                RefId = 99,
                Payload = JsonDocument.Parse($"{{\"secondaryTagIds\":[{secondaryTag.Id}]}}"),
                SourceKey = "ext:ai.faces",
            });
        await context.SaveChangesAsync();

        var spanResolver = new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions()));
        var controller = new SceneSegmentsController(context, spanResolver);

        var queryResult = await controller.QuerySpans(scene.Id, new SegmentSpanQueryRequestDto(
            null,
            "intersection",
            [
                new SegmentSpanOperandDto(null, null, [secondaryTag.Id], null),
                new SegmentSpanOperandDto(null, null, null, null, [42]),
            ],
            0,
            0), CancellationToken.None);

        var queryOk = Assert.IsType<OkObjectResult>(queryResult.Result);
        var queryDto = Assert.IsType<ResolvedSpanListDto>(queryOk.Value);
        var span = Assert.Single(queryDto.Spans);
        Assert.Equal(10, span.StartSec);
        Assert.Equal(14, span.EndSec);
        Assert.Single(span.SegmentIds);
    }

    [Fact]
    public async Task QueryDerivedSpans_CanBeLoadedByDetailAndSnapshotted()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Derived Detail Scene", MaxDuration = 120 };
        var group = new Group { Name = "Derived Query Compilation" };
        context.Scenes.Add(scene);
        context.Groups.Add(group);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 10,
                EndSec = 12,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 11,
                EndSec = 13,
                Kind = "user.face",
                SourceKey = "user",
            });
        await context.SaveChangesAsync();

        var spanResolver = new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions()));
        var sceneController = new SceneSegmentsController(context, spanResolver);

        var queryResult = await sceneController.QuerySpans(scene.Id, new SegmentSpanQueryRequestDto(
            null,
            "intersection",
            [
                new SegmentSpanOperandDto("ext:ai.faces", null, null, null),
                new SegmentSpanOperandDto("user", null, null, null),
            ],
            0,
            0), CancellationToken.None);
        var queryOk = Assert.IsType<OkObjectResult>(queryResult.Result);
        var queryDto = Assert.IsType<ResolvedSpanListDto>(queryOk.Value);
        var derivedSpan = Assert.Single(queryDto.Spans);
        Assert.StartsWith("dq-intersection-", derivedSpan.SpanKey, StringComparison.Ordinal);

        var detailResult = await sceneController.GetSpanDetail(scene.Id, derivedSpan.SpanKey, null, CancellationToken.None);
        var detailOk = Assert.IsType<OkObjectResult>(detailResult.Result);
        var detailDto = Assert.IsType<ResolvedSpanDetailDto>(detailOk.Value);
        Assert.Equal(scene.Id, detailDto.SceneId);
        Assert.Equal("Derived Detail Scene", detailDto.SceneTitle);
        Assert.Equal("derived", detailDto.Span.SourceKey);
        Assert.Equal("intersection", detailDto.Span.Kind);
        Assert.Single(detailDto.Intervals);
        Assert.Equal(11, detailDto.Intervals[0].StartSec);
        Assert.Equal(12, detailDto.Intervals[0].EndSec);

        var groupController = new GroupItemsController(context, spanResolver);
        var createFromSpansResult = await groupController.CreateFromSpans(group.Id, new GroupItemsFromSpansDto([
            new GroupItemSpanInputDto(derivedSpan.SpanKey, scene.Id, null, null, null, null)
        ]), CancellationToken.None);
        var createFromSpansOk = Assert.IsType<OkObjectResult>(createFromSpansResult.Result);
        var createdItems = Assert.IsAssignableFrom<IReadOnlyList<GroupItemDto>>(createFromSpansOk.Value);
        var createdItem = Assert.Single(createdItems);
        Assert.Equal(GroupItemKind.SceneRange, createdItem.Kind);
        Assert.Equal(11, createdItem.StartSec);
        Assert.Equal(12, createdItem.EndSec);
        Assert.Equal(derivedSpan.SpanKey, createdItem.SourceSpanKey);
        Assert.NotNull(createdItem.SourceProfileId);
        Assert.NotNull(createdItem.SnapshotAt);
    }

    [Fact]
    public async Task GroupItemsController_CanSnapshotExplicitDerivedQueryDescriptor()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Explicit Derived Query Scene", MaxDuration = 120 };
        var group = new Group { Name = "Explicit Derived Query Group" };
        context.Scenes.Add(scene);
        context.Groups.Add(group);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 10,
                EndSec = 12,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 11,
                EndSec = 13,
                Kind = "user.face",
                SourceKey = "user",
            });
        await context.SaveChangesAsync();

        var spanResolver = new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions()));
        var controller = new GroupItemsController(context, spanResolver);
        var derivedQuery = new SegmentSpanDerivedQueryDto(
            "intersection",
            [
                new SegmentSpanOperandDto("ext:ai.faces", null, null, null),
                new SegmentSpanOperandDto("user", null, null, null),
            ],
            0,
            0);

        var createResult = await controller.CreateFromSpans(group.Id, new GroupItemsFromSpansDto([
            new GroupItemSpanInputDto(null, scene.Id, null, null, "Intersection snapshot", null, derivedQuery)
        ]), CancellationToken.None);

        var createOk = Assert.IsType<OkObjectResult>(createResult.Result);
        var createdItems = Assert.IsAssignableFrom<IReadOnlyList<GroupItemDto>>(createOk.Value);
        var createdItem = Assert.Single(createdItems);
        Assert.Equal(GroupItemKind.SceneRange, createdItem.Kind);
        Assert.Equal(11, createdItem.StartSec);
        Assert.Equal(12, createdItem.EndSec);
        Assert.StartsWith("dq-intersection-", createdItem.SourceSpanKey, StringComparison.Ordinal);
        Assert.NotNull(createdItem.SourceQueryJson);

        var roundTrippedQuery = JsonSerializer.Deserialize<SegmentSpanDerivedQueryDto>(createdItem.SourceQueryJson!);
        Assert.NotNull(roundTrippedQuery);
        Assert.Equal("intersection", roundTrippedQuery.Operator);
        Assert.Equal(2, roundTrippedQuery.Operands.Count);
    }

    [Fact]
    public async Task SegmentsController_CanListAndLoadTopLevelSceneSegments()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        var scene = new Scene { Title = "Library Scene" };
        var otherScene = new Scene { Title = "Other Scene" };
        var tag = new Tag { Name = "Highlight" };
        context.Scenes.AddRange(scene, otherScene);
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 15,
                EndSec = 24,
                TagId = tag.Id,
                Kind = "intro",
                Title = "Opening beat",
                SourceKey = "user",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = otherScene.Id,
                StartSec = 30,
                EndSec = 40,
                Kind = "action",
                Title = "Other beat",
                SourceKey = "ext:ai.tags",
            });
        await context.SaveChangesAsync();

        var controller = new SegmentsController(
            context,
            new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions())),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        var listResult = await controller.List(q: "Opening", ids: null, sceneId: null, sceneIds: null, sceneTitle: null, tagId: null, tagIds: null, kind: null, sourceKey: null, tagged: null, minConfidence: null, minDurationSec: null, sort: null, direction: null, page: 1, perPage: 20, cancellationToken: CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var page = Assert.IsType<PaginatedResponse<SegmentRecordDto>>(listOk.Value);
        Assert.Equal(1, page.TotalCount);
        var listed = Assert.Single(page.Items);
        Assert.Equal(scene.Id, listed.HostId);
        Assert.Equal("Library Scene", listed.HostTitle);
        Assert.Equal(tag.Id, listed.TagId);
        Assert.Equal("Highlight", listed.TagName);

        var detailResult = await controller.GetById(listed.Id, CancellationToken.None);
        var detailOk = Assert.IsType<OkObjectResult>(detailResult.Result);
        var detail = Assert.IsType<SegmentRecordDto>(detailOk.Value);
        Assert.Equal(listed.Id, detail.Id);
        Assert.Equal("Opening beat", detail.Title);
        Assert.Equal("Library Scene", detail.HostTitle);
    }

    [Fact]
    public async Task SegmentsController_ListSupportsFocusedFiltersAndSorting()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        var scene = new Scene { Title = "Filter Scene" };
        var tag = new Tag { Name = "Tagged" };
        context.Scenes.Add(scene);
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 5,
                EndSec = 7,
                Kind = "face",
                Title = "Short beat",
                SourceKey = "ext:ai.faces",
                Confidence = 0.35f,
                UpdatedAt = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 15,
                EndSec = 24,
                TagId = tag.Id,
                Kind = "highlight",
                Title = "Tagged beat",
                SourceKey = "user",
                Confidence = 0.92f,
                UpdatedAt = new DateTime(2024, 1, 3, 12, 0, 0, DateTimeKind.Utc),
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 30,
                EndSec = 50,
                Kind = "highlight",
                Title = "Long beat",
                SourceKey = "ext:ai.tags",
                Confidence = 0.78f,
                UpdatedAt = new DateTime(2024, 1, 4, 12, 0, 0, DateTimeKind.Utc),
            });
        await context.SaveChangesAsync();

        var controller = new SegmentsController(
            context,
            new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions())),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        var filteredResult = await controller.List(q: null, ids: null, sceneId: scene.Id, sceneIds: null, sceneTitle: null, tagId: null, tagIds: null, kind: "highlight", sourceKey: null, tagged: true, minConfidence: 0.8f, minDurationSec: 5, sort: "duration", direction: "desc", page: 1, perPage: 20, cancellationToken: CancellationToken.None);
        var filteredOk = Assert.IsType<OkObjectResult>(filteredResult.Result);
        var filteredPage = Assert.IsType<PaginatedResponse<SegmentRecordDto>>(filteredOk.Value);
        var filtered = Assert.Single(filteredPage.Items);
        Assert.Equal("Tagged beat", filtered.Title);

        var sortedResult = await controller.List(q: null, ids: null, sceneId: scene.Id, sceneIds: null, sceneTitle: null, tagId: null, tagIds: null, kind: null, sourceKey: null, tagged: null, minConfidence: null, minDurationSec: null, sort: "duration", direction: "asc", page: 1, perPage: 20, cancellationToken: CancellationToken.None);
        var sortedOk = Assert.IsType<OkObjectResult>(sortedResult.Result);
        var sortedPage = Assert.IsType<PaginatedResponse<SegmentRecordDto>>(sortedOk.Value);

        Assert.Equal(3, sortedPage.Items.Count);
        Assert.Equal(new[] { "Short beat", "Tagged beat", "Long beat" }, sortedPage.Items.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task SegmentsController_ListsDistinctSourceKeysAndKinds()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        var scene = new Scene { Title = "Distinct Scene" };
        context.Scenes.Add(scene);
        await context.SaveChangesAsync();

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 1,
                EndSec = 3,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 5,
                EndSec = 7,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 10,
                EndSec = 14,
                Kind = "action",
                SourceKey = "ext:ai.actions",
            });
        await context.SaveChangesAsync();

        var controller = new SegmentsController(
            context,
            new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions())),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        var sourceKeysResult = await controller.DistinctSourceKeys(CancellationToken.None);
        var sourceKeysOk = Assert.IsType<OkObjectResult>(sourceKeysResult.Result);
        var sourceKeys = Assert.IsAssignableFrom<IReadOnlyList<SegmentDistinctValueDto>>(sourceKeysOk.Value);
        Assert.Equal("ext:ai.faces", sourceKeys[0].Value);
        Assert.Equal(2, sourceKeys[0].Count);

        var kindsResult = await controller.DistinctKinds(CancellationToken.None);
        var kindsOk = Assert.IsType<OkObjectResult>(kindsResult.Result);
        var kinds = Assert.IsAssignableFrom<IReadOnlyList<SegmentDistinctValueDto>>(kindsOk.Value);
        Assert.Contains(kinds, item => item.Value == "face" && item.Count == 2);
        Assert.Contains(kinds, item => item.Value == "action" && item.Count == 1);
    }

    [Fact]
    public async Task SegmentsController_SearchSpans_PaginatesFlattenedSparseSceneResults()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        var scenes = Enumerable.Range(1, 5)
            .Select(index => new Scene
            {
                Title = $"Scene {index}",
                UpdatedAt = new DateTime(2024, 1, index, 12, 0, 0, DateTimeKind.Utc),
            })
            .ToList();
        context.Scenes.AddRange(scenes);
        await context.SaveChangesAsync();

        var profile = new SegmentDisplayProfile
        {
            Name = "Sparse Pagination",
            IsDefault = true,
            Version = 1,
        };
        context.SegmentDisplayProfiles.Add(profile);
        await context.SaveChangesAsync();

        context.SegmentDisplayRules.Add(new SegmentDisplayRule
        {
            ProfileId = profile.Id,
            SourceKey = "user",
            Visible = true,
        });

        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scenes[0].Id,
                StartSec = 5,
                EndSec = 7,
                Kind = "clip",
                SourceKey = "user",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scenes[2].Id,
                StartSec = 15,
                EndSec = 18,
                Kind = "clip",
                SourceKey = "user",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scenes[4].Id,
                StartSec = 25,
                EndSec = 29,
                Kind = "clip",
                SourceKey = "user",
            });
        await context.SaveChangesAsync();

        using var serviceProvider = CreateSegmentControllerServiceProvider(scope.Connection);
        var controller = new SegmentsController(
            context,
            new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions())),
            serviceProvider.GetRequiredService<IServiceScopeFactory>());

        var page1Result = await controller.SearchSpans(new SegmentSpanSearchRequestDto(profile.Id, null, 1, 1, "title", "asc", null, null, null, null), CancellationToken.None);
        var page2Result = await controller.SearchSpans(new SegmentSpanSearchRequestDto(profile.Id, null, 2, 1, "title", "asc", null, null, null, null), CancellationToken.None);
        var page3Result = await controller.SearchSpans(new SegmentSpanSearchRequestDto(profile.Id, null, 3, 1, "title", "asc", null, null, null, null), CancellationToken.None);

        var page1 = Assert.IsType<SegmentSpanSearchResponseDto>(Assert.IsType<OkObjectResult>(page1Result.Result).Value);
        var page2 = Assert.IsType<SegmentSpanSearchResponseDto>(Assert.IsType<OkObjectResult>(page2Result.Result).Value);
        var page3 = Assert.IsType<SegmentSpanSearchResponseDto>(Assert.IsType<OkObjectResult>(page3Result.Result).Value);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(3, page3.TotalCount);

        Assert.Single(page1.Items);
        Assert.Single(page2.Items);
        Assert.Single(page3.Items);

        Assert.Equal(scenes[0].Id, page1.Items[0].SceneId);
        Assert.Equal(scenes[2].Id, page2.Items[0].SceneId);
        Assert.Equal(scenes[4].Id, page3.Items[0].SceneId);
    }

    [Fact]
    public async Task SceneDetectionsController_CanCreateUpdateListAndDeleteSceneDetections()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Detection Scene" };
        context.Scenes.Add(scene);
        await context.SaveChangesAsync();

        var controller = new SceneDetectionsController(context);
        var createDto = new DetectionCreateDto(
            42.0,
            1920,
            1080,
            "face",
            0.88f,
            100,
            120,
            220,
            260,
            ParseJson("{" + "\"landmarks\":[1,2,3]}"),
            "face",
            12,
            "track-1",
            "ext:ai.faces",
            "run-2");

        var createResult = await controller.Create(scene.Id, createDto, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var createdDto = Assert.IsType<DetectionDto>(created.Value);
        Assert.Equal(scene.Id, createdDto.HostId);
        Assert.Equal("face", createdDto.Class);
        Assert.True(createdDto.Extra.HasValue);
        Assert.Equal("track-1", createdDto.GroupKey);

        var listResult = await controller.GetByScene(scene.Id, CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var listed = Assert.IsAssignableFrom<IReadOnlyList<DetectionDto>>(listOk.Value);
        var listedDetection = Assert.Single(listed);
        Assert.Equal(createdDto.Id, listedDetection.Id);

        var updateDto = new DetectionUpdateDto(
            45.0,
            1920,
            1080,
            "face:refined",
            0.91f,
            110,
            130,
            210,
            240,
            ParseJson("{" + "\"crop\":\"thumb-1\"}"),
            "segment",
            50,
            "track-1",
            "ext:ai.faces",
            "run-3");

        var updateResult = await controller.Update(scene.Id, createdDto.Id, updateDto, CancellationToken.None);
        var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updatedDto = Assert.IsType<DetectionDto>(updateOk.Value);
        Assert.Equal(45.0, updatedDto.ObservedAtSec);
        Assert.Equal("face:refined", updatedDto.Class);
        Assert.True(updatedDto.Extra.HasValue);
        Assert.Equal("thumb-1", updatedDto.Extra.Value.GetProperty("crop").GetString());
        Assert.Equal("segment", updatedDto.RefKind);
        Assert.Equal(50, updatedDto.RefId);

        var deleteResult = await controller.Delete(scene.Id, createdDto.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);
    }

    [Fact]
    public async Task ImageDetectionsController_CanCreateUpdateListAndDeleteImageDetections()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var image = new Image { Title = "Detection Image" };
        context.Images.Add(image);
        await context.SaveChangesAsync();

        var controller = new ImageDetectionsController(context);
        var createDto = new DetectionCreateDto(
            null,
            1200,
            1600,
            "face",
            0.94f,
            80,
            120,
            260,
            320,
            ParseJson("{" + "\"embedding\":\"face-1\"}"),
            "face",
            21,
            "image-track-1",
            "ext:ai.faces",
            "run-image-1");

        var createResult = await controller.Create(image.Id, createDto, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var createdDto = Assert.IsType<DetectionDto>(created.Value);
        Assert.Equal(image.Id, createdDto.HostId);
        Assert.Equal(DetectionHostType.Image, createdDto.HostType);
        Assert.Equal("face", createdDto.RefKind);
        Assert.Equal(21, createdDto.RefId);

        var listResult = await controller.GetByImage(image.Id, CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var listed = Assert.IsAssignableFrom<IReadOnlyList<DetectionDto>>(listOk.Value);
        var listedDetection = Assert.Single(listed);
        Assert.Equal(createdDto.Id, listedDetection.Id);

        var updateDto = new DetectionUpdateDto(
            null,
            1200,
            1600,
            "face:crop",
            0.97f,
            82,
            126,
            252,
            310,
            ParseJson("{" + "\"crop\":\"image-thumb\"}"),
            "face",
            21,
            "image-track-1",
            "ext:ai.faces",
            "run-image-2");

        var updateResult = await controller.Update(image.Id, createdDto.Id, updateDto, CancellationToken.None);
        var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updatedDto = Assert.IsType<DetectionDto>(updateOk.Value);
        Assert.Equal("face:crop", updatedDto.Class);
        Assert.True(updatedDto.Extra.HasValue);
        Assert.Equal("image-thumb", updatedDto.Extra.Value.GetProperty("crop").GetString());

        var deleteResult = await controller.Delete(image.Id, createdDto.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);
    }

    [Fact]
    public async Task SegmentDisplayProfilesController_SeparatesGlobalAndPerUserRules()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var tag = new Tag { Name = "Highlights" };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        var principalAccessor = new CurrentPrincipalAccessor();
        var spanResolver = new SegmentSpanResolver(context, principalAccessor, new MemoryCache(new MemoryCacheOptions()));
        var controller = new SegmentDisplayProfilesController(context, spanResolver, principalAccessor);

        var globalProfilesResult = await controller.List(CancellationToken.None);
        var globalProfilesOk = Assert.IsType<OkObjectResult>(globalProfilesResult.Result);
        var globalProfiles = Assert.IsAssignableFrom<IReadOnlyList<SegmentDisplayProfileDto>>(globalProfilesOk.Value);
        var rawProfile = Assert.Single(globalProfiles, profile => profile.UserId == null && profile.Name == "Raw");

        var globalCreate = await controller.CreateRule(rawProfile.Id, new SegmentDisplayRuleCreateDto(
            "import:stash",
            "tag",
            null,
            "favorites",
            SegmentHostType.Scene,
            true,
            0.5f,
            null,
            3.0,
            false,
            null,
            1,
            10), CancellationToken.None);
        var globalCreated = Assert.IsType<CreatedResult>(globalCreate.Result);
        var globalDto = Assert.IsType<SegmentDisplayRuleDto>(globalCreated.Value);
        Assert.Null(globalDto.UserId);

        principalAccessor.Set(CreatePrincipal(7));

        var createdProfileResult = await controller.Create(new SegmentDisplayProfileCreateDto("Mine", "User profile", false), CancellationToken.None);
        var createdProfileAction = Assert.IsType<CreatedAtActionResult>(createdProfileResult.Result);
        var createdProfile = Assert.IsType<SegmentDisplayProfileDto>(createdProfileAction.Value);

        var userCreate = await controller.CreateRule(createdProfile.Id, new SegmentDisplayRuleCreateDto(
            "ext:ai.faces",
            "face",
            tag.Id,
            null,
            SegmentHostType.Scene,
            false,
            0.8f,
            1.5,
            0.5,
            true,
            "#00ffaa",
            3,
            20), CancellationToken.None);
        var userCreated = Assert.IsType<CreatedResult>(userCreate.Result);
        var userDto = Assert.IsType<SegmentDisplayRuleDto>(userCreated.Value);
        Assert.Equal(7, userDto.UserId);
        Assert.Equal("Highlights", userDto.TagName);

        var globalListResult = await controller.ListRules(rawProfile.Id, CancellationToken.None);
        var globalListOk = Assert.IsType<OkObjectResult>(globalListResult.Result);
        var globalListed = Assert.IsAssignableFrom<IReadOnlyList<SegmentDisplayRuleDto>>(globalListOk.Value);
        var listedGlobalRule = Assert.Single(globalListed);
        Assert.Equal(globalDto.Id, listedGlobalRule.Id);
        Assert.Null(listedGlobalRule.UserId);

        var userListResult = await controller.ListRules(createdProfile.Id, CancellationToken.None);
        var userListOk = Assert.IsType<OkObjectResult>(userListResult.Result);
        var userListed = Assert.IsAssignableFrom<IReadOnlyList<SegmentDisplayRuleDto>>(userListOk.Value);
        var listedUserRule = Assert.Single(userListed);
        Assert.Equal(userDto.Id, listedUserRule.Id);
        Assert.Equal(7, listedUserRule.UserId);

        var updateAttempt = await controller.UpdateRule(rawProfile.Id, globalDto.Id, new SegmentDisplayRuleUpdateDto(
            "import:stash",
            "tag",
            null,
            "favorites",
            SegmentHostType.Scene,
            true,
            0.5f,
            null,
            2.0,
            false,
            null,
            1,
            15), CancellationToken.None);
        Assert.IsType<NotFoundResult>(updateAttempt.Result);

        var updateResult = await controller.UpdateRule(createdProfile.Id, userDto.Id, new SegmentDisplayRuleUpdateDto(
            "ext:ai.faces",
            "face",
            tag.Id,
            null,
            SegmentHostType.Scene,
            true,
            0.7f,
            2.5,
            0.25,
            false,
            "#1144ff",
            4,
            30), CancellationToken.None);
        var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updatedDto = Assert.IsType<SegmentDisplayRuleDto>(updateOk.Value);
        Assert.True(updatedDto.Visible);
        Assert.Equal(4, updatedDto.Lane);
        Assert.Equal("#1144ff", updatedDto.ColorOverride);

        var deleteResult = await controller.DeleteRule(createdProfile.Id, userDto.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);

        var finalListResult = await controller.ListRules(createdProfile.Id, CancellationToken.None);
        var finalListOk = Assert.IsType<OkObjectResult>(finalListResult.Result);
        var finalListed = Assert.IsAssignableFrom<IReadOnlyList<SegmentDisplayRuleDto>>(finalListOk.Value);
        Assert.Empty(finalListed);
    }

    [Fact]
    public async Task SegmentDisplayProfilesController_CreatesUserProfilesAndSwitchesDefault()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(CreatePrincipal(7));

        var spanResolver = new SegmentSpanResolver(context, principalAccessor, new MemoryCache(new MemoryCacheOptions()));
        var controller = new SegmentDisplayProfilesController(context, spanResolver, principalAccessor);

        var initialListResult = await controller.List(CancellationToken.None);
        var initialListOk = Assert.IsType<OkObjectResult>(initialListResult.Result);
        var initialProfiles = Assert.IsAssignableFrom<IReadOnlyList<SegmentDisplayProfileDto>>(initialListOk.Value);
        Assert.Equal(3, initialProfiles.Count);
        Assert.Contains(initialProfiles, item => item.Name == "Raw" && item.UserId == null);
        Assert.Contains(initialProfiles, item => item.Name == "Default" && item.UserId == null && item.IsDefault);
        Assert.Contains(initialProfiles, item => item.Name == "Default" && item.UserId == 7 && item.IsDefault);

        var createResult = await controller.Create(new SegmentDisplayProfileCreateDto("Strict", "Tighter segment display rules", false), CancellationToken.None);
        var createCreated = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var createdProfile = Assert.IsType<SegmentDisplayProfileDto>(createCreated.Value);
        Assert.Equal("Strict", createdProfile.Name);
        Assert.Equal(7, createdProfile.UserId);
        Assert.False(createdProfile.IsDefault);

        var defaultResult = await controller.SetDefault(createdProfile.Id, CancellationToken.None);
        var defaultOk = Assert.IsType<OkObjectResult>(defaultResult.Result);
        var defaultProfile = Assert.IsType<SegmentDisplayProfileDto>(defaultOk.Value);
        Assert.True(defaultProfile.IsDefault);

        var finalListResult = await controller.List(CancellationToken.None);
        var finalListOk = Assert.IsType<OkObjectResult>(finalListResult.Result);
        var finalProfiles = Assert.IsAssignableFrom<IReadOnlyList<SegmentDisplayProfileDto>>(finalListOk.Value);
        Assert.Single(finalProfiles, item => item.UserId == 7 && item.IsDefault);
        Assert.Contains(finalProfiles, item => item.Id == createdProfile.Id && item.IsDefault);
    }

    [Fact]
    public async Task SegmentDisplayProfilesController_PreviewUsesTransientRules()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var scene = new Scene { Title = "Preview Scene" };
        var tag = new Tag { Name = "Highlight" };
        context.AddRange(scene, tag);
        await context.SaveChangesAsync();

        context.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = scene.Id,
            StartSec = 3,
            EndSec = 9,
            TagId = tag.Id,
            Kind = "action",
            SourceKey = "ext:ai.actions",
        });
        await context.SaveChangesAsync();

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(CreatePrincipal(7));

        var controller = new SegmentDisplayProfilesController(
            context,
            new SegmentSpanResolver(context, principalAccessor, new MemoryCache(new MemoryCacheOptions())),
            principalAccessor);

        var previewResult = await controller.Preview(new SegmentDisplayProfilePreviewRequestDto(
            scene.Id,
            [
                new SegmentDisplayRuleCreateDto(
                    "ext:ai.actions",
                    "action",
                    tag.Id,
                    null,
                    SegmentHostType.Scene,
                    true,
                    null,
                    null,
                    0,
                    false,
                    "#33ccaa",
                    2,
                    null),
            ]), CancellationToken.None);

        var previewOk = Assert.IsType<OkObjectResult>(previewResult.Result);
        var preview = Assert.IsType<ResolvedSpanListDto>(previewOk.Value);
        var span = Assert.Single(preview.Spans);
        Assert.Equal(3, span.StartSec);
        Assert.Equal(9, span.EndSec);
        Assert.Equal("#33ccaa", span.ColorHint);
        Assert.Equal(2, span.Lane);
    }

    [Fact]
    public async Task SegmentDisplayProfilesController_NestedRuleMutationsBumpProfileVersion()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var tag = new Tag { Name = "Highlights" };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(CreatePrincipal(9));

        var spanResolver = new SegmentSpanResolver(context, principalAccessor, new MemoryCache(new MemoryCacheOptions()));
        var controller = new SegmentDisplayProfilesController(context, spanResolver, principalAccessor);

        var createProfileResult = await controller.Create(new SegmentDisplayProfileCreateDto("Strict", null, true), CancellationToken.None);
        var createProfileCreated = Assert.IsType<CreatedAtActionResult>(createProfileResult.Result);
        var createdProfile = Assert.IsType<SegmentDisplayProfileDto>(createProfileCreated.Value);
        Assert.Equal(1, createdProfile.Version);

        var createRuleResult = await controller.CreateRule(createdProfile.Id, new SegmentDisplayRuleCreateDto(
            "ext:ai.faces",
            "face",
            tag.Id,
            null,
            SegmentHostType.Scene,
            true,
            0.8f,
            1.5,
            0.5,
            false,
            "#00ffaa",
            2,
            30), CancellationToken.None);
        var createRuleCreated = Assert.IsType<CreatedResult>(createRuleResult.Result);
        var createdRule = Assert.IsType<SegmentDisplayRuleDto>(createRuleCreated.Value);
        Assert.Equal(9, createdRule.UserId);
        Assert.Equal("Highlights", createdRule.TagName);

        var afterCreateResult = await controller.GetById(createdProfile.Id, CancellationToken.None);
        var afterCreateOk = Assert.IsType<OkObjectResult>(afterCreateResult.Result);
        var afterCreateProfile = Assert.IsType<SegmentDisplayProfileDto>(afterCreateOk.Value);
        Assert.Equal(2, afterCreateProfile.Version);

        var updateRuleResult = await controller.UpdateRule(createdProfile.Id, createdRule.Id, new SegmentDisplayRuleUpdateDto(
            "ext:ai.faces",
            "face",
            tag.Id,
            null,
            SegmentHostType.Scene,
            false,
            0.9f,
            2.0,
            0.25,
            true,
            "#1144ff",
            3,
            40), CancellationToken.None);
        var updateRuleOk = Assert.IsType<OkObjectResult>(updateRuleResult.Result);
        var updatedRule = Assert.IsType<SegmentDisplayRuleDto>(updateRuleOk.Value);
        Assert.False(updatedRule.Visible);
        Assert.True(updatedRule.CollapseToInstant);

        var afterUpdateResult = await controller.GetById(createdProfile.Id, CancellationToken.None);
        var afterUpdateOk = Assert.IsType<OkObjectResult>(afterUpdateResult.Result);
        var afterUpdateProfile = Assert.IsType<SegmentDisplayProfileDto>(afterUpdateOk.Value);
        Assert.Equal(3, afterUpdateProfile.Version);

        var deleteResult = await controller.DeleteRule(createdProfile.Id, createdRule.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);

        var afterDeleteResult = await controller.GetById(createdProfile.Id, CancellationToken.None);
        var afterDeleteOk = Assert.IsType<OkObjectResult>(afterDeleteResult.Result);
        var afterDeleteProfile = Assert.IsType<SegmentDisplayProfileDto>(afterDeleteOk.Value);
        Assert.Equal(4, afterDeleteProfile.Version);
    }

    [Fact]
    public async Task GroupItemsController_CanCreateReorderAndBuildPlaybackManifest()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var group = new Group { Name = "Compilation" };
        var sceneA = new Scene { Title = "Scene A", MaxDuration = 120 };
        var sceneB = new Scene { Title = "Scene B", MaxDuration = 90 };
        context.Groups.Add(group);
        context.Scenes.AddRange(sceneA, sceneB);
        await context.SaveChangesAsync();

        var controller = new GroupItemsController(context, new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions())));

        var createSceneResult = await controller.Create(group.Id, new GroupItemCreateDto(
            0,
            GroupItemKind.Scene,
            sceneA.Id,
            null,
            null,
            "Full scene",
            null,
            null,
            null), CancellationToken.None);
        var createSceneCreated = Assert.IsType<CreatedAtActionResult>(createSceneResult.Result);
        var sceneItem = Assert.IsType<GroupItemDto>(createSceneCreated.Value);
        Assert.Equal(GroupItemKind.Scene, sceneItem.Kind);

        var createRangeResult = await controller.Create(group.Id, new GroupItemCreateDto(
            1,
            GroupItemKind.SceneRange,
            sceneB.Id,
            5,
            17,
            "Clip",
            "Notes",
            null,
            null), CancellationToken.None);
        var createRangeCreated = Assert.IsType<CreatedAtActionResult>(createRangeResult.Result);
        var rangeItem = Assert.IsType<GroupItemDto>(createRangeCreated.Value);
        Assert.Equal(5, rangeItem.StartSec);
        Assert.Equal(17, rangeItem.EndSec);

        var reorderResult = await controller.Reorder(group.Id, new GroupItemsReorderDto([rangeItem.Id, sceneItem.Id]), CancellationToken.None);
        Assert.IsType<OkResult>(reorderResult);

        var listResult = await controller.List(group.Id, CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var listed = Assert.IsAssignableFrom<IReadOnlyList<GroupItemDto>>(listOk.Value);
        Assert.Equal(2, listed.Count);
        Assert.Equal(rangeItem.Id, listed[0].Id);
        Assert.Equal(sceneItem.Id, listed[1].Id);

        var manifestResult = await controller.GetPlaybackManifest(group.Id, CancellationToken.None);
        var manifestOk = Assert.IsType<OkObjectResult>(manifestResult.Result);
        var manifest = Assert.IsType<GroupPlaybackManifestDto>(manifestOk.Value);
        Assert.Equal(2, manifest.Items.Count);
        Assert.Equal(sceneB.Id, manifest.Items[0].SceneId);
        Assert.Equal("/api/stream/scene/" + sceneB.Id, manifest.Items[0].Src);
        Assert.Equal(12, manifest.Items[0].DurationSec);
        Assert.Equal(0, manifest.Items[1].StartSec);
        Assert.Null(manifest.Items[1].EndSec);
        Assert.Equal(120, manifest.Items[1].DurationSec);
    }

    [Fact]
    public async Task GroupItemsController_CanCreateAudioAndTextItems()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var group = new Group { Name = "Mixed Media" };
        var audio = new Audio { Title = "Audio Chapter" };
        var text = new TextDocument { Title = "Text Chapter" };
        context.AddRange(group, audio, text);
        await context.SaveChangesAsync();

        var controller = new GroupItemsController(context, new SegmentSpanResolver(context, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions())));

        var createAudioResult = await controller.Create(group.Id, new GroupItemCreateDto(
            0,
            GroupItemKind.Audio,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "audio",
            audio.Id), CancellationToken.None);
        var createAudioCreated = Assert.IsType<CreatedAtActionResult>(createAudioResult.Result);
        var audioItem = Assert.IsType<GroupItemDto>(createAudioCreated.Value);
        Assert.Equal(GroupItemKind.Audio, audioItem.Kind);
        Assert.Equal("audio", audioItem.HostType);
        Assert.Equal(audio.Id, audioItem.HostId);
        Assert.Equal("Audio Chapter", audioItem.Title);

        var createTextResult = await controller.Create(group.Id, new GroupItemCreateDto(
            1,
            GroupItemKind.Text,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "text",
            text.Id), CancellationToken.None);
        var createTextCreated = Assert.IsType<CreatedAtActionResult>(createTextResult.Result);
        var textItem = Assert.IsType<GroupItemDto>(createTextCreated.Value);
        Assert.Equal(GroupItemKind.Text, textItem.Kind);
        Assert.Equal("text", textItem.HostType);
        Assert.Equal(text.Id, textItem.HostId);
        Assert.Equal("Text Chapter", textItem.Title);
    }

    [Fact]
    public async Task GroupItemsController_CanSnapshotResolvedSpans()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var group = new Group { Name = "Compilation" };
        var scene = new Scene { Title = "Scene A", MaxDuration = 120 };
        var tag = new Tag { Name = "Highlights" };
        context.Groups.Add(group);
        context.Scenes.Add(scene);
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        var profile = new SegmentDisplayProfile
        {
            Name = "Strict",
            UserId = 11,
            IsDefault = true,
            Version = 1,
        };
        context.SegmentDisplayProfiles.Add(profile);
        await context.SaveChangesAsync();

        context.SegmentDisplayRules.Add(new SegmentDisplayRule
        {
            ProfileId = profile.Id,
            UserId = 11,
            SourceKey = "ext:ai.faces",
            MergeGapSec = 1.0,
            Visible = true,
        });
        context.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 10,
                EndSec = 12,
                TagId = tag.Id,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 12.5,
                EndSec = 14,
                TagId = tag.Id,
                Kind = "face",
                SourceKey = "ext:ai.faces",
            });
        await context.SaveChangesAsync();

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(CreatePrincipal(11));
        var spanResolver = new SegmentSpanResolver(context, principalAccessor, new MemoryCache(new MemoryCacheOptions()));
        var resolved = await spanResolver.ResolveSceneAsync(scene.Id, profile.Id, CancellationToken.None);
        var span = Assert.Single(resolved.Spans);

        var controller = new GroupItemsController(context, spanResolver);
        var createFromSpansResult = await controller.CreateFromSpans(group.Id, new GroupItemsFromSpansDto([
            new GroupItemSpanInputDto(span.SpanKey, scene.Id, null, null, null, profile.Id)
        ]), CancellationToken.None);
        var createFromSpansOk = Assert.IsType<OkObjectResult>(createFromSpansResult.Result);
        var createdItems = Assert.IsAssignableFrom<IReadOnlyList<GroupItemDto>>(createFromSpansOk.Value);
        var createdItem = Assert.Single(createdItems);
        Assert.Equal(GroupItemKind.SceneRange, createdItem.Kind);
        Assert.Equal(10, createdItem.StartSec);
        Assert.Equal(14, createdItem.EndSec);
        Assert.Equal(span.SpanKey, createdItem.SourceSpanKey);
        Assert.Equal(profile.Id, createdItem.SourceProfileId);
        Assert.NotNull(createdItem.SnapshotAt);
    }

    private static JsonElement ParseJson(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static async Task<TestContextScope> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;

        var context = new SegmentCoreTestContext(options);
        await context.Database.EnsureCreatedAsync();
        return new TestContextScope(context, connection);
    }

    private static ServiceProvider CreateSegmentControllerServiceProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentPrincipalAccessor>(new CurrentPrincipalAccessor());
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddScoped<CoveContext>(_ => new SegmentCoreTestContext(
            new DbContextOptionsBuilder<CoveContext>()
                .UseSqlite(connection)
                .Options));
        services.AddScoped<SegmentSpanResolver>();
        return services.BuildServiceProvider();
    }

    private static CovePrincipal CreatePrincipal(int userId) => new()
    {
        UserId = userId,
        Username = $"user-{userId}",
        Kind = PrincipalKind.User,
        Roles = new HashSet<string>(),
        Permissions = new HashSet<string>
        {
            Permissions.SegmentsRead,
            Permissions.SegmentsWrite,
            Permissions.SegmentsDelete,
        },
    };

    private sealed class SegmentCoreTestContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

        }
    }

    private sealed class TestContextScope : IAsyncDisposable
    {
        public TestContextScope(CoveContext context, SqliteConnection connection)
        {
            Context = context;
            Connection = connection;
        }

        public CoveContext Context { get; }
        public SqliteConnection Connection { get; }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
