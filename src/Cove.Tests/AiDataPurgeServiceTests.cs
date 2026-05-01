using System.Text.Json;

using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public sealed class AiDataPurgeServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_UsesAiRunModelFallback()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var scene = new Scene { Title = "Audio Scene" };
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();

        db.AiRuns.Add(new AiRun
        {
            RunKey = "run-summary",
            SourceKey = "ext:ai.audio",
            TargetType = AiRunTargetType.Scene,
            TargetId = scene.Id,
            Models = JsonDocument.Parse("[{\"ConfigName\":\"audio-model\"}]"),
        });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = scene.Id,
            StartSec = 0,
            EndSec = 3,
            Kind = "audio.label",
            SourceKey = "ext:ai.audio",
            SourceRunId = "run-summary",
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var summary = await service.GetSummaryAsync(new AiDataSelectorDto(null, null, null, null, null, null, null));

        var item = Assert.Single(summary.Items);
        Assert.Equal("segment", item.Kind);
        Assert.Equal("audio-model", item.Model);
        Assert.Equal("scene", item.HostType);
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public async Task PurgeAsync_BySourceRunId_RemovesMatchingArtifactsAcrossKinds()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var scene = new Scene { Title = "Tagged Scene" };
        var image = new Image { Title = "Tagged Image" };
        var aiOnlyTag = new Tag { Name = "AI Only" };
        var manualTag = new Tag { Name = "Manual" };
        db.AddRange(scene, image, aiOnlyTag, manualTag);
        await db.SaveChangesAsync();

        db.Set<SceneTag>().AddRange(
            new SceneTag { SceneId = scene.Id, TagId = aiOnlyTag.Id },
            new SceneTag { SceneId = scene.Id, TagId = manualTag.Id });
        db.TagApplications.AddRange(
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = scene.Id,
                TagId = aiOnlyTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-1",
                ModelKey = "tagger-v1",
            },
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = scene.Id,
                TagId = manualTag.Id,
                SourceKey = "user",
                SourceRunId = string.Empty,
                ModelKey = string.Empty,
            });
        db.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 0,
                EndSec = 1,
                Kind = "tag",
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-1",
            },
            new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = scene.Id,
                StartSec = 1,
                EndSec = 2,
                Kind = "tag",
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-2",
            });
        db.Set<Detection>().AddRange(
            new Detection
            {
                HostType = DetectionHostType.Scene,
                HostId = scene.Id,
                Class = "face",
                Score = 0.9f,
                SourceKey = "ext:ai.faces",
                SourceRunId = "run-1",
            },
            new Detection
            {
                HostType = DetectionHostType.Scene,
                HostId = scene.Id,
                Class = "face",
                Score = 0.5f,
                SourceKey = "ext:ai.faces",
                SourceRunId = "run-2",
            });
        db.Embeddings.AddRange(
            new Embedding
            {
                HostType = EmbeddingHostType.Image,
                HostId = image.Id,
                Kind = "clip.image",
                Modality = EmbeddingModality.Visual,
                Dim = 2,
                Vector = new Pgvector.Vector(new float[] { 0.1f, 0.2f }),
                SourceKey = "ext:ai.visual",
                SourceRunId = "run-1",
            },
            new Embedding
            {
                HostType = EmbeddingHostType.Image,
                HostId = image.Id,
                Kind = "clip.image",
                Modality = EmbeddingModality.Visual,
                Dim = 2,
                Vector = new Pgvector.Vector(new float[] { 0.3f, 0.4f }),
                SourceKey = "ext:ai.visual",
                SourceRunId = "run-2",
            });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(new AiDataSelectorDto(null, "run-1", null, null, null, null, ["embedding", "detection", "segment", "tagApplication"]));

        Assert.Equal(1, result.RemovedCounts["embedding"]);
        Assert.Equal(1, result.RemovedCounts["detection"]);
        Assert.Equal(1, result.RemovedCounts["segment"]);
        Assert.Equal(1, result.RemovedCounts["tagApplication"]);
        Assert.Single(await db.Embeddings.ToListAsync());
        Assert.Single(await db.Set<Detection>().ToListAsync());
        Assert.Single(await db.Segments.ToListAsync());
        Assert.Single(await db.TagApplications.ToListAsync());
        Assert.Single(await db.Set<SceneTag>().ToListAsync());
        Assert.Equal(manualTag.Id, (await db.Set<SceneTag>().SingleAsync()).TagId);
    }

    [Fact]
    public async Task PurgeAsync_RemovesAiTagApplicationsButKeepsManualTags()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var scene = new Scene { Title = "Scene" };
        var sharedTag = new Tag { Name = "Shared" };
        var aiOnlyTag = new Tag { Name = "AI Only" };
        db.AddRange(scene, sharedTag, aiOnlyTag);
        await db.SaveChangesAsync();

        db.Set<SceneTag>().AddRange(
            new SceneTag { SceneId = scene.Id, TagId = sharedTag.Id },
            new SceneTag { SceneId = scene.Id, TagId = aiOnlyTag.Id });
        db.TagApplications.AddRange(
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = scene.Id,
                TagId = sharedTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-tagging",
                ModelKey = "tagger-v1",
            },
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = scene.Id,
                TagId = sharedTag.Id,
                SourceKey = "user",
                SourceRunId = string.Empty,
                ModelKey = string.Empty,
            },
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = scene.Id,
                TagId = aiOnlyTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-tagging",
                ModelKey = "tagger-v1",
            });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = scene.Id,
            StartSec = 0,
            EndSec = 2,
            Kind = "tag",
            SourceKey = "ext:ai.tagging",
            SourceRunId = "run-tagging",
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(new AiDataSelectorDto("ext:ai.tagging", null, null, null, null, null, ["tagApplication", "segment"]));

        Assert.Equal(2, result.RemovedCounts["tagApplication"]);
        Assert.Equal(1, result.RemovedCounts["segment"]);

        var sceneTags = await db.Set<SceneTag>().OrderBy(sceneTag => sceneTag.TagId).ToListAsync();
        var remainingApplications = await db.TagApplications.OrderBy(application => application.TagId).ToListAsync();

        Assert.Single(sceneTags);
        Assert.Equal(sharedTag.Id, sceneTags[0].TagId);
        Assert.Single(remainingApplications);
        Assert.Equal("user", remainingApplications[0].SourceKey);
        Assert.Empty(await db.Segments.ToListAsync());
    }

    [Fact]
    public async Task DryRun_ReturnsCounts_WithoutMutating()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var scene = new Scene { Title = "Dry Run Scene" };
        var image = new Image { Title = "Dry Run Image" };
        var tag = new Tag { Name = "Dry Tag" };
        db.AddRange(scene, image, tag);
        await db.SaveChangesAsync();

        db.Set<SceneTag>().Add(new SceneTag { SceneId = scene.Id, TagId = tag.Id });
        db.TagApplications.Add(new TagApplication
        {
            HostType = AffinityHostType.Scene,
            HostId = scene.Id,
            TagId = tag.Id,
            SourceKey = "ext:ai.tagging",
            SourceRunId = "dry-run-1",
            ModelKey = "tagger-v1",
        });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = scene.Id,
            StartSec = 5,
            EndSec = 8,
            Kind = "tag",
            SourceKey = "ext:ai.tagging",
            SourceRunId = "dry-run-1",
        });
        db.Set<Detection>().Add(new Detection
        {
            HostType = DetectionHostType.Scene,
            HostId = scene.Id,
            Class = "face",
            Score = 0.91f,
            SourceKey = "ext:ai.tagging",
            SourceRunId = "dry-run-1",
        });
        db.Embeddings.Add(new Embedding
        {
            HostType = EmbeddingHostType.Image,
            HostId = image.Id,
            Kind = "clip.image",
            Modality = EmbeddingModality.Visual,
            Dim = 2,
            Vector = new Pgvector.Vector(new float[] { 0.4f, 0.8f }),
            SourceKey = "ext:ai.tagging",
            SourceRunId = "dry-run-1",
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(
            new AiDataSelectorDto("ext:ai.tagging", "dry-run-1", null, null, null, null, ["embedding", "detection", "segment", "tagApplication"]),
            dryRun: true);

        Assert.Equal(1, result.RemovedCounts["embedding"]);
        Assert.Equal(1, result.RemovedCounts["detection"]);
        Assert.Equal(1, result.RemovedCounts["segment"]);
        Assert.Equal(1, result.RemovedCounts["tagApplication"]);
        Assert.Single(await db.Embeddings.ToListAsync());
        Assert.Single(await db.Set<Detection>().ToListAsync());
        Assert.Single(await db.Segments.ToListAsync());
        Assert.Single(await db.TagApplications.ToListAsync());
        Assert.Single(await db.Set<SceneTag>().ToListAsync());
    }

    [Fact]
    public async Task DryRun_WithFaceKind_DoesNotDoubleCountFaceOwnedArtifacts()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var scene = new Scene { Title = "Face Scene" };
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();

        var face = new Face
        {
            Label = "Face A",
            PrimarySourceKey = "ext:ai.faces",
        };
        db.Faces.Add(face);
        await db.SaveChangesAsync();

        db.Set<Detection>().Add(new Detection
        {
            HostType = DetectionHostType.Scene,
            HostId = scene.Id,
            Class = "face",
            Score = 0.97f,
            RefKind = "face",
            RefId = face.Id,
            SourceKey = "ext:ai.faces",
            SourceRunId = "face-run-1",
        });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = scene.Id,
            StartSec = 1,
            EndSec = 2,
            Kind = "face",
            RefId = face.Id,
            SourceKey = "ext:ai.faces",
            SourceRunId = "face-run-1",
        });
        db.Embeddings.Add(new Embedding
        {
            HostType = EmbeddingHostType.Face,
            HostId = face.Id,
            Kind = "face.embedding",
            Modality = EmbeddingModality.Face,
            Dim = 2,
            Vector = new Pgvector.Vector(new float[] { 0.2f, 0.6f }),
            SourceKey = "ext:ai.faces",
            SourceRunId = "face-run-1",
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(
            new AiDataSelectorDto("ext:ai.faces", null, null, null, null, null, ["embedding", "detection", "segment", "face"]),
            dryRun: true);

        Assert.Equal(1, result.RemovedCounts["face"]);
        Assert.Equal(1, result.RemovedCounts["embedding"]);
        Assert.Equal(1, result.RemovedCounts["detection"]);
        Assert.Equal(1, result.RemovedCounts["segment"]);
        Assert.Single(await db.Faces.ToListAsync());
        Assert.Single(await db.Embeddings.ToListAsync());
        Assert.Single(await db.Set<Detection>().ToListAsync());
        Assert.Single(await db.Segments.ToListAsync());
    }

    private static AiDataPurgeService CreateService(CoveContext context)
        => new(context, [], new StubBlobService(), NullLogger<AiDataPurgeService>.Instance);

    private static async Task<TestEnvironment> CreateEnvironmentAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AiDataTestContext(options);
        await context.Database.EnsureCreatedAsync();
        return new TestEnvironment(connection, context);
    }

    private sealed class AiDataTestContext(DbContextOptions<CoveContext> options) : CoveContext(options)
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

    private sealed class StubBlobService : IBlobService
    {
        public Task<string> StoreBlobAsync(Stream data, string contentType, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString("n"));

        public Task<(Stream Stream, string ContentType)?> GetBlobAsync(string blobId, CancellationToken ct = default)
            => Task.FromResult<(Stream Stream, string ContentType)?>(null);

        public Task DeleteBlobAsync(string blobId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class TestEnvironment(SqliteConnection connection, AiDataTestContext context) : IAsyncDisposable
    {
        public AiDataTestContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}