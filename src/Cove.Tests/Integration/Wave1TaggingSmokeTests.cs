using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests.Integration;

public sealed class Wave1TaggingSmokeTests
{
    [Fact]
    public async Task TagSearch_WithQuery_ReturnsMatchingItems()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        await factory.WithDbContextAsync(async db =>
        {
            db.Tags.AddRange(
                new Tag { Name = "Searchable Squirting" },
                new Tag { Name = "Unrelated Tag", Description = "Does not match" });
            await db.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/tags?q=squirt&perPage=10&sort=name&direction=asc");
        response.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = payload.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Contains(items, item => item.GetProperty("name").GetString() == "Searchable Squirting");
    }

    [Fact]
    public async Task SceneSearch_WithQuery_ReturnsMatchingItems()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        await factory.WithDbContextAsync(async db =>
        {
            db.Scenes.AddRange(
                new Scene { Title = "Searchable Squirt Scene", Details = "Contains the search term" },
                new Scene { Title = "Other Scene", Details = "Different content" });
            await db.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/scenes?q=squirt&perPage=10&sort=title&direction=asc");
        response.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = payload.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Contains(items, item => item.GetProperty("title").GetString() == "Searchable Squirt Scene");
    }

    [Fact]
    public async Task TagCreate_WithCustomFields_RoundTripsThroughApi()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();
        using var client = factory.CreateAuthenticatedClient();

        var textFieldResponse = await client.PostAsJsonAsync("/api/custom-fields", new
        {
            key = "source_id",
            label = "Source ID",
            type = "text",
            entityTypes = new[] { CustomFieldEntityTypes.Tag },
            filterable = true,
            sortable = true,
        }, IntegrationHttpJson.Options);
        textFieldResponse.EnsureSuccessStatusCode();

        var dateFieldResponse = await client.PostAsJsonAsync("/api/custom-fields", new
        {
            key = "reviewed_on",
            label = "Reviewed On",
            type = "date",
            entityTypes = new[] { CustomFieldEntityTypes.Tag },
            filterable = true,
            sortable = true,
        }, IntegrationHttpJson.Options);
        dateFieldResponse.EnsureSuccessStatusCode();

        var createResponse = await client.PostAsJsonAsync("/api/tags", new
        {
            name = "Tag with custom fields",
            customFields = new Dictionary<string, object?>
            {
                ["source_id"] = "cf-ui",
                ["reviewed_on"] = "2026-05-09",
            },
        }, IntegrationHttpJson.Options);
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadApiJsonAsync<TagDetailDto>();
        Assert.NotNull(created);
        Assert.NotNull(created!.CustomFields);
        Assert.Equal("cf-ui", Assert.IsType<JsonElement>(created.CustomFields!["source_id"]).GetString());
        Assert.Equal("2026-05-09", Assert.IsType<JsonElement>(created.CustomFields!["reviewed_on"]).GetString());

        var detailResponse = await client.GetAsync($"/api/tags/{created.Id}");
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadApiJsonAsync<TagDetailDto>();
        Assert.NotNull(detail);
        Assert.NotNull(detail!.CustomFields);
        Assert.Equal("cf-ui", Assert.IsType<JsonElement>(detail.CustomFields!["source_id"]).GetString());
        Assert.Equal("2026-05-09", Assert.IsType<JsonElement>(detail.CustomFields!["reviewed_on"]).GetString());
    }

    [Fact]
    public async Task TagGroups_And_TagMetadata_RoundTripThroughApi()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();
        using var client = factory.CreateAuthenticatedClient();

        var groupResponse = await client.PostAsJsonAsync("/api/taggroups", new
        {
            name = "Wave Group",
            description = "Wave one grouping",
            color = "#22c55e",
            sortOrder = 7,
        }, IntegrationHttpJson.Options);
        groupResponse.EnsureSuccessStatusCode();
        var group = await groupResponse.Content.ReadApiJsonAsync<TagGroupDto>();
        Assert.NotNull(group);
        Assert.Equal("Wave Group", group!.Name);
        Assert.Equal("#22c55e", group.Color);

        var tagResponse = await client.PostAsJsonAsync("/api/tags", new
        {
            name = "Wave Tag",
            description = "Context aware",
            color = "#0ea5e9",
            tagGroupId = group.Id,
            minOccurrenceSec = 4.5,
            minOccurrencePercent = 12.5,
            aliases = new[] { "wave alias" },
        }, IntegrationHttpJson.Options);
        tagResponse.EnsureSuccessStatusCode();
        var tag = await tagResponse.Content.ReadApiJsonAsync<TagDetailDto>();
        Assert.NotNull(tag);
        Assert.Equal("#0ea5e9", tag!.Color);
        Assert.Equal(group.Id, tag.TagGroupId);
        Assert.Equal("Wave Group", tag.TagGroupName);
        Assert.Equal(4.5, tag.MinOccurrenceSec);
        Assert.Equal(12.5, tag.MinOccurrencePercent);

        var groupsResponse = await client.GetAsync("/api/taggroups");
        groupsResponse.EnsureSuccessStatusCode();
        var groups = await groupsResponse.Content.ReadApiJsonAsync<List<TagGroupDto>>();
        Assert.NotNull(groups);
        var listedGroup = Assert.Single(groups!);
        Assert.Equal(1, listedGroup.TagCount);

        var clearResponse = await client.PutAsJsonAsync($"/api/tags/{tag.Id}", new
        {
            name = "Wave Tag",
            color = (string?)null,
            tagGroupId = (int?)null,
            minOccurrenceSec = (double?)null,
            minOccurrencePercent = (double?)null,
        }, IntegrationHttpJson.Options);
        clearResponse.EnsureSuccessStatusCode();
        var cleared = await clearResponse.Content.ReadApiJsonAsync<TagDetailDto>();
        Assert.NotNull(cleared);
        Assert.Null(cleared!.Color);
        Assert.Null(cleared.TagGroupId);
        Assert.Null(cleared.MinOccurrenceSec);
        Assert.Null(cleared.MinOccurrencePercent);
    }

    [Fact]
    public async Task PerformerContextTagApplication_RoundTripsThroughApiAndSceneDetail()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        var (sceneId, performerId, tagId) = await factory.WithDbContextAsync(async db =>
        {
            var scene = new Scene { Title = "Context Scene", MaxDuration = 100 };
            var performer = new Performer { Name = "Context Performer" };
            var tag = new Tag { Name = "Context Tag", Color = "#f97316" };
            db.AddRange(scene, performer, tag);
            await db.SaveChangesAsync();

            db.Set<ScenePerformer>().Add(new ScenePerformer { SceneId = scene.Id, PerformerId = performer.Id });
            await db.SaveChangesAsync();
            return (scene.Id, performer.Id, tag.Id);
        });

        using var client = factory.CreateAuthenticatedClient();
        var createResponse = await client.PostAsJsonAsync("/api/tagapplications", new
        {
            hostType = "scene",
            hostId = sceneId,
            contextType = "performer",
            contextId = performerId,
            tagId,
            sourceKey = "user",
            totalDurationSec = 18.0,
            hostDurationSec = 100.0,
        }, IntegrationHttpJson.Options);
        createResponse.EnsureSuccessStatusCode();
        var application = await createResponse.Content.ReadApiJsonAsync<TagApplicationDto>();
        Assert.NotNull(application);
        Assert.Equal("performer", application!.ContextType);
        Assert.Equal(performerId, application.ContextId);
        Assert.Equal(18.0, application.TotalDurationSec);

        var sceneResponse = await client.GetAsync($"/api/scenes/{sceneId}");
        sceneResponse.EnsureSuccessStatusCode();
        var scene = await sceneResponse.Content.ReadApiJsonAsync<SceneDto>();
        Assert.NotNull(scene);
        var contextApplication = Assert.Single(scene!.ContextTagApplications!);
        Assert.Equal(application.Id, contextApplication.Id);
        Assert.Equal("Context Tag", contextApplication.Tag.Name);
        Assert.Equal("#f97316", contextApplication.Tag.Color);

        var invalidResponse = await client.PostAsJsonAsync("/api/tagapplications", new
        {
            hostType = "scene",
            hostId = sceneId,
            contextType = "performer",
            contextId = performerId + 1000,
            tagId,
        }, IntegrationHttpJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/tagapplications/{application.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await factory.WithDbContextAsync(async db =>
        {
            Assert.Empty(await db.TagApplications.ToListAsync());
        });
    }
}
