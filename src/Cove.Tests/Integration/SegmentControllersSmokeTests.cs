using System.Net.Http.Json;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests.Integration;

public sealed class GroupItemsControllerSmokeTests
{
    [Fact]
    public async Task CreateFromSpans_DerivedBranch_ReturnsOk()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        var (groupId, sceneId) = await factory.WithDbContextAsync(async db =>
        {
            var scene = new Scene { Title = "Explicit Derived Query Scene", MaxDuration = 120 };
            var group = new Group { Name = "Explicit Derived Query Group" };
            db.Scenes.Add(scene);
            db.Groups.Add(group);
            await db.SaveChangesAsync();

            db.Segments.AddRange(
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
            await db.SaveChangesAsync();
            return (group.Id, scene.Id);
        });

        var request = new GroupItemsFromSpansDto([
            new GroupItemSpanInputDto(
                null,
                sceneId,
                null,
                null,
                "Intersection snapshot",
                null,
                new SegmentSpanDerivedQueryDto(
                    "intersection",
                    [
                        new SegmentSpanOperandDto("ext:ai.faces", null, null, null),
                        new SegmentSpanOperandDto("user", null, null, null),
                    ],
                    0,
                    0))
        ]);

        using var client = factory.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/groups/{groupId}/items/from-spans", request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadApiJsonAsync<List<GroupItemDto>>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload);
        Assert.Equal(GroupItemKind.SceneRange, item.Kind);
        Assert.StartsWith("dq-intersection-", item.SourceSpanKey, StringComparison.Ordinal);

        await factory.WithDbContextAsync(async db =>
        {
            Assert.Single(await db.GroupItems.ToListAsync());
        });
    }
}

public sealed class SceneSegmentsControllerSmokeTests
{
    [Fact]
    public async Task List_ReturnsOk()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        var sceneId = await factory.WithDbContextAsync(async db =>
        {
            var scene = new Scene { Title = "Segment Scene" };
            db.Scenes.Add(scene);
            await db.SaveChangesAsync();

            db.Segments.Add(new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 12.5,
                EndSec = 18.25,
                Kind = "face",
                SourceKey = "ext:ai.faces",
                SourceRunId = "run-1",
                Confidence = 0.96f,
                Title = "Lead face",
                ColorHint = "#ffaa00",
            });
            await db.SaveChangesAsync();
            return scene.Id;
        });

        using var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/scenes/{sceneId}/segments");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadApiJsonAsync<List<SegmentDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload);
    }
}

public sealed class SegmentDisplayProfilesControllerSmokeTests
{
    [Fact]
    public async Task List_And_Preview_ReturnOk()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        var sceneId = await factory.WithDbContextAsync(async db =>
        {
            var scene = new Scene { Title = "Preview Scene" };
            var tag = new Tag { Name = "Highlight" };
            db.AddRange(scene, tag);
            await db.SaveChangesAsync();

            db.Segments.Add(new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 3,
                EndSec = 9,
                TagId = tag.Id,
                Kind = "action",
                SourceKey = "ext:ai.actions",
            });
            await db.SaveChangesAsync();
            return scene.Id;
        });

        using var client = factory.CreateAuthenticatedClient();

        var listResponse = await client.GetAsync("/api/segment-display-profiles");
        listResponse.EnsureSuccessStatusCode();
        var profiles = await listResponse.Content.ReadApiJsonAsync<List<SegmentDisplayProfileDto>>();
        Assert.NotNull(profiles);
        Assert.NotEmpty(profiles);

        var previewResponse = await client.PostAsJsonAsync("/api/segment-display-profiles/preview", new SegmentDisplayProfilePreviewRequestDto(
            sceneId,
            [
                new SegmentDisplayRuleCreateDto(
                    "ext:ai.actions",
                    "action",
                    null,
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
            ]));
        previewResponse.EnsureSuccessStatusCode();

        var preview = await previewResponse.Content.ReadApiJsonAsync<ResolvedSpanListDto>();
        Assert.NotNull(preview);
        Assert.Single(preview.Spans);
    }
}

public sealed class SegmentsControllerSmokeTests
{
    [Fact]
    public async Task List_Distincts_And_SpansSearch_ReturnOk()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        var (sceneId, profileId) = await factory.WithDbContextAsync(async db =>
        {
            var scene = new Scene
            {
                Title = "Library Scene",
                MaxDuration = 120,
                UpdatedAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            };
            db.Scenes.Add(scene);
            await db.SaveChangesAsync();

            var profile = new SegmentDisplayProfile
            {
                Name = "Search Profile",
                UserId = CoveWebApplicationFactory.TestUserId,
                IsDefault = true,
                Version = 1,
            };
            db.SegmentDisplayProfiles.Add(profile);
            await db.SaveChangesAsync();

            db.SegmentDisplayRules.Add(new SegmentDisplayRule
            {
                ProfileId = profile.Id,
                UserId = CoveWebApplicationFactory.TestUserId,
                SourceKey = "user",
                Visible = true,
            });
            db.Segments.AddRange(
                new Segment
                {
                    HostType = SegmentHostType.Scene,
                    HostId = scene.Id,
                    StartSec = 5,
                    EndSec = 7,
                    Kind = "clip",
                    Title = "User span",
                    SourceKey = "user",
                },
                new Segment
                {
                    HostType = SegmentHostType.Scene,
                    HostId = scene.Id,
                    StartSec = 10,
                    EndSec = 14,
                    Kind = "action",
                    Title = "AI span",
                    SourceKey = "ext:ai.actions",
                });
            await db.SaveChangesAsync();

            return (scene.Id, profile.Id);
        });

        using var client = factory.CreateAuthenticatedClient();

        var listResponse = await client.GetAsync($"/api/segments?sceneId={sceneId}&page=1&perPage=20");
        listResponse.EnsureSuccessStatusCode();
        var listPayload = await listResponse.Content.ReadApiJsonAsync<PaginatedResponse<SegmentRecordDto>>();
        Assert.NotNull(listPayload);
        Assert.Equal(2, listPayload.TotalCount);

        var distinctsResponse = await client.GetAsync("/api/segments/source-keys/distinct");
        distinctsResponse.EnsureSuccessStatusCode();
        var distincts = await distinctsResponse.Content.ReadApiJsonAsync<List<SegmentDistinctValueDto>>();
        Assert.NotNull(distincts);
        Assert.Contains(distincts, item => item.Value == "user");

        var spansResponse = await client.PostAsJsonAsync("/api/segments/spans/search", new SegmentSpanSearchRequestDto(
            profileId,
            null,
            1,
            10,
            "title",
            "asc",
            null,
            null,
            [sceneId],
            null));
        spansResponse.EnsureSuccessStatusCode();
        var spansPayload = await spansResponse.Content.ReadApiJsonAsync<SegmentSpanSearchResponseDto>();
        Assert.NotNull(spansPayload);
        Assert.Equal(2, spansPayload.Items.Count);
        Assert.Contains(spansPayload.Items, item => item.Span.SourceKey == "user");
        Assert.Contains(spansPayload.Items, item => item.Span.SourceKey == "ext:ai.actions");
    }
}