using Cove.Api.Controllers;
using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Events;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Tests;

public sealed class TagProvenanceServiceTests
{
    [Fact]
    public async Task SyncTagSetAsync_AddsUserRowsForNewTagsAndDeletesRemovedProvenance()
    {
        await using var context = CreateContext();

        var scene = new Scene { Title = "Tagged Scene" };
        var manualTag = new Tag { Name = "Manual" };
        var keptTag = new Tag { Name = "Kept" };
        var addedTag = new Tag { Name = "Added" };

        context.AddRange(scene, manualTag, keptTag, addedTag);
        await context.SaveChangesAsync();

        context.TagApplications.AddRange(
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = scene.Id,
                TagId = manualTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-1",
                ModelKey = "tagger-v1",
                Confidence = 0.91f,
            },
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = scene.Id,
                TagId = keptTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-2",
                ModelKey = "tagger-v1",
                Confidence = 0.77f,
            });
        await context.SaveChangesAsync();

        ITagProvenanceService service = new TagProvenanceService(context);

        await service.SyncTagSetAsync(
            AffinityHostType.Scene,
            scene.Id,
            [manualTag.Id, keptTag.Id],
            [keptTag.Id, addedTag.Id]);
        await context.SaveChangesAsync();

        var applications = await context.TagApplications
            .Where(application => application.HostType == AffinityHostType.Scene && application.HostId == scene.Id)
            .OrderBy(application => application.TagId)
            .ThenBy(application => application.SourceKey)
            .ToListAsync();

        Assert.DoesNotContain(applications, application => application.TagId == manualTag.Id);
        Assert.Contains(applications, application => application.TagId == keptTag.Id && application.SourceKey == "ext:ai.tagging");
        Assert.Contains(applications, application => application.TagId == addedTag.Id && application.SourceKey == "user");
        Assert.DoesNotContain(applications, application => application.TagId == keptTag.Id && application.SourceKey == "user");
    }

    [Fact]
    public async Task RecordAsync_UpdatesExistingConfidenceForMatchingSource()
    {
        await using var context = CreateContext();

        var image = new Image { Title = "Tagged Image" };
        var tag = new Tag { Name = "Action" };
        context.AddRange(image, tag);
        await context.SaveChangesAsync();

        ITagProvenanceService service = new TagProvenanceService(context);

        await service.RecordAsync(AffinityHostType.Image, image.Id, tag.Id, "ext:ai.tagging", "run-1", "tagger-v1", 0.41f);
        await service.RecordAsync(AffinityHostType.Image, image.Id, tag.Id, "ext:ai.tagging", "run-1", "tagger-v1", 0.83f);
        await context.SaveChangesAsync();

        var applications = await context.TagApplications
            .Where(application => application.HostType == AffinityHostType.Image && application.HostId == image.Id)
            .ToListAsync();

        var application = Assert.Single(applications);
        Assert.Equal(0.83f, application.Confidence);
    }

    [Fact]
    public async Task ScenesController_CreateUpdateAndDelete_TracksUserTagProvenance()
    {
        await using var context = CreateContext();

        var firstTag = new Tag { Name = "First" };
        var secondTag = new Tag { Name = "Second" };
        context.AddRange(firstTag, secondTag);
        await context.SaveChangesAsync();

        var controller = new ScenesController(
            new SceneRepository(context),
            context,
            null!,
            null!,
            null!,
            new MemoryCache(new MemoryCacheOptions()),
            null!,
            null!,
            null!,
            new NoOpUserEngagementService(),
            new TagProvenanceService(context));

        var createResult = await controller.Create(
            new SceneCreateDto("Tagged Scene", null, null, null, null, null, false, null, null, null, null, [firstTag.Id], null, null, null),
            CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var createdScene = Assert.IsType<SceneDto>(created.Value);

        var createdApplications = await context.TagApplications
            .Where(application => application.HostType == AffinityHostType.Scene && application.HostId == createdScene.Id)
            .ToListAsync();
        Assert.Single(createdApplications);
        Assert.Equal(firstTag.Id, createdApplications[0].TagId);
        Assert.Equal("user", createdApplications[0].SourceKey);

        var updateResult = await controller.Update(
            createdScene.Id,
            new SceneUpdateDto(null, null, null, null, null, null, null, null, null, null, null, [secondTag.Id], null, null, null, null),
            CancellationToken.None);
        var updated = Assert.IsType<OkObjectResult>(updateResult.Result);
        Assert.IsType<SceneDto>(updated.Value);

        var updatedApplications = await context.TagApplications
            .Where(application => application.HostType == AffinityHostType.Scene && application.HostId == createdScene.Id)
            .ToListAsync();
        var updatedApplication = Assert.Single(updatedApplications);
        Assert.Equal(secondTag.Id, updatedApplication.TagId);
        Assert.Equal("user", updatedApplication.SourceKey);

        var deleteResult = await controller.Delete(createdScene.Id, false, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);
        Assert.Empty(await context.TagApplications.Where(application => application.HostType == AffinityHostType.Scene && application.HostId == createdScene.Id).ToListAsync());
    }

    [Fact]
    public async Task SceneMetadataApplyService_RecordsScraperTagProvenance()
    {
        await using var context = CreateContext();

        var scene = new Scene { Title = "Scraped Scene" };
        context.Scenes.Add(scene);
        await context.SaveChangesAsync();

        var service = new SceneMetadataApplyService(context, new EventBus(), new NoOpSceneCoverService(), new TagProvenanceService(context));

        var applied = await service.ApplyAsync(
            scene.Id,
            new ScrapedSceneDto
            {
                TagNames = ["Body"],
            },
            CancellationToken.None);

        Assert.True(applied);

        var application = await context.TagApplications.SingleAsync();
        var tag = await context.Tags.SingleAsync();

        Assert.Equal(AffinityHostType.Scene, application.HostType);
        Assert.Equal(scene.Id, application.HostId);
        Assert.Equal(tag.Id, application.TagId);
        Assert.Equal("scraper", application.SourceKey);
    }

    [Fact]
    public async Task GetLookupAsync_UsesSeparateContextWhenCallerHasActiveReader()
    {
        await using var callerContext = CreateContext();
        callerContext.AddRange(
            new Scene { Id = 1, Title = "Caller Scene" },
            new Tag { Id = 1, Name = "Action" },
            new AiRun
            {
                RunKey = "run-1",
                SourceKey = "ext:ai.tagging",
                TargetType = AiRunTargetType.Scene,
                TargetId = 1,
                Status = AiRunStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                CompletedAt = DateTime.UtcNow.AddMinutes(-1),
            });
        await callerContext.SaveChangesAsync();

        await using var lookupContext = CreateContext();
        lookupContext.AddRange(
            new Scene { Id = 1, Title = "Lookup Scene" },
            new Tag { Id = 1, Name = "Action" },
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = 1,
                TagId = 1,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-1",
                ModelKey = "tagger-v1",
                Confidence = 0.91f,
            });
        await lookupContext.SaveChangesAsync();

        var scopeFactory = new FixedScopeFactory(lookupContext);
        var service = new TagProvenanceService(callerContext, scopeFactory);

        await using var reader = callerContext.AiRuns
            .AsNoTracking()
            .AsAsyncEnumerable()
            .GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());

        var fallbackLookup = await new TagProvenanceService(callerContext).GetLookupAsync(AffinityHostType.Scene, 1, [1]);
        Assert.Empty(fallbackLookup);

        var lookup = await service.GetLookupAsync(AffinityHostType.Scene, 1, [1]);

        Assert.True(scopeFactory.ScopeCreated);
        var provenance = Assert.Single(lookup[1]);
        Assert.Equal("ext:ai.tagging", provenance.SourceKey);
        Assert.Equal("tagger-v1", provenance.ModelKey);
    }

    private static CoveContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"tag-provenance-{Guid.NewGuid():N}")
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

    private sealed class FixedScopeFactory(CoveContext context) : IServiceScopeFactory
    {
        public bool ScopeCreated { get; private set; }

        public IServiceScope CreateScope()
        {
            ScopeCreated = true;
            return new FixedScope(context);
        }
    }

    private sealed class FixedScope(CoveContext context) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection()
            .AddScoped<CoveContext>(_ => context)
            .BuildServiceProvider();

        public void Dispose()
        {
        }
    }

    private sealed class NoOpSceneCoverService : ISceneCoverService
    {
        public Task<bool> TryApplyRemoteCoverAsync(Scene scene, string? imageUrl, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}