using System.Text.Json;
using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public class ScrapeAttemptServiceTests
{
    [Fact]
    public async Task ApplyAttemptAsync_AudioAttemptAppliesSelectedFieldsAndNormalizesTags()
    {
        var dbName = $"scrape-attempt-service-{Guid.NewGuid():N}";
        await using var db = CreateDbContext(dbName);

        var existingStudio = new Studio { Name = "Existing Studio" };
        var existingTag = new Tag { Name = "Legacy" };
        var existingPerformer = new Performer { Name = "Existing Performer" };

        var audio = new Audio
        {
            Title = "Current Title",
            Studio = existingStudio,
            Urls = [new AudioUrl { Url = "https://existing.example/audio" }],
            AudioTags = [new AudioTag { Tag = existingTag }],
            AudioPerformers = [new AudioPerformer { Performer = existingPerformer }],
            TagIds = [],
            PerformerIds = [],
        };

        db.Audios.Add(audio);
        await db.SaveChangesAsync();

        audio.TagIds = [existingTag.Id];
        audio.PerformerIds = [existingPerformer.Id];

        var attempt = new ScrapeAttempt
        {
            ScraperId = "tests.fake-scraper/audio",
            EntityType = EntityKinds.Audio,
            EntityId = audio.Id,
            InputKind = "url",
            InputJson = JsonSerializer.Serialize(new { url = "https://example.com/audio" }),
            ResultJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["Title"] = "Scraped Title",
                ["Artist"] = "Scraped Artist",
                ["URLs"] = new[] { "https://existing.example/audio", "https://new.example/audio" },
                ["TagNames"] = new[] { "[F4M]" },
                ["PerformerNames"] = new[] { "New Performer" },
                ["StudioName"] = "Scraped Studio",
            }),
        };

        db.ScrapeAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var service = new ScrapeAttemptService(
            db,
            null!,
            null!,
            null!,
            new NoOpTagProvenanceService(),
            NullLogger<ScrapeAttemptService>.Instance);

        var result = await service.ApplyAttemptAsync(
            attempt.Id,
            new ApplySceneScrapeAttemptDto(
                ReplaceFields: ["title"],
                CollectionModes: new Dictionary<string, string>
                {
                    ["urls"] = "merge",
                    ["tags"] = "replace",
                    ["performers"] = "merge",
                    ["studio"] = "replace",
                },
                CreateMissingTags: true,
                CreateMissingPerformers: true,
                CreateMissingStudio: true,
                MarkOrganized: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Applied", result!.Status);
        Assert.NotNull(result.EntitySnapshotJson);

        var updatedAudio = await db.Audios
            .Include(item => item.Urls)
            .Include(item => item.AudioTags).ThenInclude(item => item.Tag)
            .Include(item => item.AudioPerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .SingleAsync(item => item.Id == audio.Id);

        Assert.Equal("Scraped Title", updatedAudio.Title);
        Assert.True(updatedAudio.Organized);
        Assert.Equal("Scraped Studio", updatedAudio.Studio?.Name);
        Assert.Equal(
            ["https://existing.example/audio", "https://new.example/audio"],
            updatedAudio.Urls.Select(item => item.Url).OrderBy(item => item).ToArray());
        Assert.Equal(["F4M"], updatedAudio.AudioTags.Select(item => item.Tag!.Name).OrderBy(item => item).ToArray());
        Assert.Equal(
            ["Existing Performer", "New Performer", "Scraped Artist"],
            updatedAudio.AudioPerformers.Select(item => item.Performer!.Name).OrderBy(item => item).ToArray());
        Assert.Single(updatedAudio.TagIds);
        Assert.Equal(3, updatedAudio.PerformerIds.Length);
    }

    [Fact]
    public async Task ApplyAttemptAsync_TextAttemptHonorsPerItemSelections()
    {
        var dbName = $"scrape-attempt-service-{Guid.NewGuid():N}";
        await using var db = CreateDbContext(dbName);

        var existingTag = new Tag { Name = "Existing Tag" };
        var skippedExistingTag = new Tag { Name = "Skipped Existing Tag" };
        var existingPerformer = new Performer { Name = "Existing Performer" };
        db.Tags.AddRange(existingTag, skippedExistingTag);
        db.Performers.Add(existingPerformer);

        var text = new TextDocument
        {
            Title = "Current Text",
            TextTags = [new TextTag { Tag = skippedExistingTag }],
            TextPerformers = [],
            TagIds = [],
            PerformerIds = [],
        };
        db.TextDocuments.Add(text);
        await db.SaveChangesAsync();

        text.TagIds = [skippedExistingTag.Id];

        var attempt = new ScrapeAttempt
        {
            ScraperId = "tests.fake-scraper/text",
            EntityType = EntityKinds.Text,
            EntityId = text.Id,
            InputKind = "url",
            InputJson = JsonSerializer.Serialize(new { url = "https://example.com/story" }),
            ResultJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["TagNames"] = new[] { "Existing Tag", "Created Tag", "Skipped Tag" },
                ["PerformerNames"] = new[] { "Existing Performer", "Created Performer", "Skipped Performer" },
            }),
        };

        db.ScrapeAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var service = new ScrapeAttemptService(
            db,
            null!,
            null!,
            null!,
            new NoOpTagProvenanceService(),
            NullLogger<ScrapeAttemptService>.Instance);

        var result = await service.ApplyAttemptAsync(
            attempt.Id,
            new ApplySceneScrapeAttemptDto(
                ReplaceFields: [],
                CollectionModes: new Dictionary<string, string>
                {
                    ["tags"] = "replace",
                    ["performers"] = "replace",
                },
                CreateMissingTags: false,
                CreateMissingPerformers: false,
                TagSelections:
                [
                    new ScrapeCollectionItemSelectionDto("Existing Tag", "include"),
                    new ScrapeCollectionItemSelectionDto("Created Tag", "create"),
                    new ScrapeCollectionItemSelectionDto("Skipped Tag", "exclude"),
                ],
                PerformerSelections:
                [
                    new ScrapeCollectionItemSelectionDto("Existing Performer", "include"),
                    new ScrapeCollectionItemSelectionDto("Created Performer", "create"),
                    new ScrapeCollectionItemSelectionDto("Skipped Performer", "exclude"),
                ]),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("AppliedPartial", result!.Status);

        var updatedText = await db.TextDocuments
            .Include(item => item.TextTags).ThenInclude(item => item.Tag)
            .Include(item => item.TextPerformers).ThenInclude(item => item.Performer)
            .SingleAsync(item => item.Id == text.Id);

        Assert.Equal(["Created Tag", "Existing Tag"], updatedText.TextTags.Select(item => item.Tag!.Name).OrderBy(item => item).ToArray());
        Assert.Equal(["Created Performer", "Existing Performer"], updatedText.TextPerformers.Select(item => item.Performer!.Name).OrderBy(item => item).ToArray());
        Assert.False(await db.Tags.AnyAsync(item => item.Name == "Skipped Tag"));
        Assert.False(await db.Performers.AnyAsync(item => item.Name == "Skipped Performer"));
    }

    private static CoveContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new CoveContext(options);
    }

    private sealed class NoOpTagProvenanceService : ITagProvenanceService
    {
        public Task RecordAsync(AffinityHostType hostType, int hostId, int tagId, string sourceKey, string? sourceRunId = null, string? modelKey = null, float? confidence = null, string? contextType = null, int? contextId = null, double? totalDurationSec = null, double? hostDurationSec = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordAsync(AffinityHostType hostType, int hostId, Tag tag, string sourceKey, string? sourceRunId = null, string? modelKey = null, float? confidence = null, string? contextType = null, int? contextId = null, double? totalDurationSec = null, double? hostDurationSec = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncTagSetAsync(AffinityHostType hostType, int hostId, IReadOnlyCollection<int> previousTagIds, IReadOnlyCollection<int> currentTagIds, string sourceKey = "user", CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveForHostAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyDictionary<int, List<TagProvenanceDto>>> GetLookupAsync(AffinityHostType hostType, int hostId, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, List<TagProvenanceDto>>>(new Dictionary<int, List<TagProvenanceDto>>());
    }
}