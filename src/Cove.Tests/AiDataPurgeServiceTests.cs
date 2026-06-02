using System.Text.Json;

using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public sealed class AiDataPurgeServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_UsesAiRunModelFallback()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Audio Video" };
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        db.AiRuns.Add(new AiRun
        {
            RunKey = "run-summary",
            SourceKey = "ext:ai.audio",
            TargetType = AiRunTargetType.Video,
            TargetId = video.Id,
            Models = JsonDocument.Parse("[{\"ConfigName\":\"audio-model\"}]"),
        });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Video,
            HostId = video.Id,
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
        Assert.Equal("video", item.HostType);
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public async Task PurgeAsync_BySourceRunId_RemovesMatchingArtifactsAcrossKinds()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Tagged Video" };
        var image = new Image { Title = "Tagged Image" };
        var aiOnlyTag = new Tag { Name = "AI Only" };
        var manualTag = new Tag { Name = "Manual" };
        db.AddRange(video, image, aiOnlyTag, manualTag);
        await db.SaveChangesAsync();

        db.Set<VideoTag>().AddRange(
            new VideoTag { VideoId = video.Id, TagId = aiOnlyTag.Id },
            new VideoTag { VideoId = video.Id, TagId = manualTag.Id });
        db.TagApplications.AddRange(
            new TagApplication
            {
                HostType = AffinityHostType.Video,
                HostId = video.Id,
                TagId = aiOnlyTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-1",
                ModelKey = "tagger-v1",
            },
            new TagApplication
            {
                HostType = AffinityHostType.Video,
                HostId = video.Id,
                TagId = manualTag.Id,
                SourceKey = "user",
                SourceRunId = string.Empty,
                ModelKey = string.Empty,
            });
        db.Segments.AddRange(
            new Segment
            {
                HostType = SegmentHostType.Video,
                HostId = video.Id,
                StartSec = 0,
                EndSec = 1,
                Kind = "tag",
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-1",
            },
            new Segment
            {
                HostType = SegmentHostType.Video,
                HostId = video.Id,
                StartSec = 1,
                EndSec = 2,
                Kind = "tag",
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-2",
            });
        db.Set<Detection>().AddRange(
            new Detection
            {
                HostType = DetectionHostType.Video,
                HostId = video.Id,
                Class = "face",
                Score = 0.9f,
                SourceKey = "ext:ai.faces",
                SourceRunId = "run-1",
            },
            new Detection
            {
                HostType = DetectionHostType.Video,
                HostId = video.Id,
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
        Assert.Single(await db.Set<VideoTag>().ToListAsync());
        Assert.Equal(manualTag.Id, (await db.Set<VideoTag>().SingleAsync()).TagId);
    }

    [Fact]
    public async Task PurgeAsync_RemovesAiRunWhenPurgedArtifactsLeaveNoRemainingRunData()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Face Video" };
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        db.AiRuns.Add(new AiRun
        {
            RunKey = "run-face-purge",
            SourceKey = "ext:ai.core",
            TargetType = AiRunTargetType.Video,
            TargetId = video.Id,
            Status = AiRunStatus.Completed,
            Models = JsonDocument.Parse("""
                [
                  { "config_name": "face_detector_torchexport" },
                  { "config_name": "face_embedding_torchexport" }
                ]
                """),
        });
        db.Set<Detection>().Add(new Detection
        {
            HostType = DetectionHostType.Video,
            HostId = video.Id,
            Class = "face",
            Score = 0.92f,
            SourceKey = "ext:ai.faces",
            SourceRunId = "run-face-purge",
            Extra = JsonDocument.Parse("""{ "modelKey": "face_detector_torchexport" }"""),
        });
        db.Embeddings.Add(new Embedding
        {
            HostType = EmbeddingHostType.Video,
            HostId = video.Id,
            Kind = "face",
            Modality = EmbeddingModality.Face,
            Dim = 2,
            Vector = new Pgvector.Vector(new float[] { 0.1f, 0.2f }),
            SourceKey = "ext:ai.faces",
            SourceRunId = "run-face-purge",
            Meta = JsonDocument.Parse("""{ "modelKey": "face_embedding_torchexport" }"""),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(new AiDataSelectorDto("ext:ai.faces", null, null, null, "video", video.Id, ["embedding", "detection"]));

        Assert.Equal(1, result.RemovedCounts["embedding"]);
        Assert.Equal(1, result.RemovedCounts["detection"]);
        Assert.Equal(1, result.RemovedCounts["aiRun"]);
        Assert.Empty(await db.Embeddings.ToListAsync());
        Assert.Empty(await db.Set<Detection>().ToListAsync());
        Assert.Empty(await db.AiRuns.ToListAsync());
    }

    [Fact]
    public async Task PurgeAsync_KeepsAiRunWhenOtherArtifactsStillReferenceRun()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Mixed Video" };
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        db.AiRuns.Add(new AiRun
        {
            RunKey = "run-mixed-purge",
            SourceKey = "ext:ai.core",
            TargetType = AiRunTargetType.Video,
            TargetId = video.Id,
            Status = AiRunStatus.Completed,
            Models = JsonDocument.Parse("""
                [
                  { "config_name": "face_detector_torchexport" },
                  { "config_name": "metaclip2_base" }
                ]
                """),
        });
        db.Set<Detection>().Add(new Detection
        {
            HostType = DetectionHostType.Video,
            HostId = video.Id,
            Class = "face",
            Score = 0.92f,
            SourceKey = "ext:ai.faces",
            SourceRunId = "run-mixed-purge",
            Extra = JsonDocument.Parse("""{ "modelKey": "face_detector_torchexport" }"""),
        });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Video,
            HostId = video.Id,
            StartSec = 0,
            EndSec = 5,
            Kind = "visual.section",
            SourceKey = "ext:ai.visual",
            SourceRunId = "run-mixed-purge",
            Payload = JsonDocument.Parse("""{ "modelKey": "metaclip2_base" }"""),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(new AiDataSelectorDto("ext:ai.faces", null, null, null, "video", video.Id, ["detection"]));

        Assert.Equal(1, result.RemovedCounts["detection"]);
        Assert.False(result.RemovedCounts.ContainsKey("aiRun"));
        Assert.Empty(await db.Set<Detection>().ToListAsync());
        Assert.Single(await db.Segments.ToListAsync());
        Assert.Single(await db.AiRuns.ToListAsync());
    }

    [Fact]
    public async Task PurgeAsync_RemovesSelectedAiRunWhenNoArtifactsRemain()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Empty AI Video" };
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        db.AiRuns.Add(new AiRun
        {
            RunKey = "run-empty-face-purge",
            SourceKey = "ext:ai.core",
            TargetType = AiRunTargetType.Video,
            TargetId = video.Id,
            Status = AiRunStatus.Completed,
            Models = JsonDocument.Parse("""
                [
                  { "config_name": "face_detector_torchexport" },
                  { "config_name": "face_embedding_torchexport" }
                ]
                """),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(new AiDataSelectorDto("ext:ai.faces", null, null, null, "video", video.Id, ["embedding", "detection", "segment", "face"]));

        Assert.Equal(1, result.RemovedCounts["aiRun"]);
        Assert.Empty(await db.AiRuns.ToListAsync());
    }

    [Fact]
    public async Task PurgeAsync_DryRunCountsSelectedAiRunWhenNoArtifactsRemain()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Empty AI Video" };
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        db.AiRuns.Add(new AiRun
        {
            RunKey = "run-empty-face-preview",
            SourceKey = "ext:ai.core",
            TargetType = AiRunTargetType.Video,
            TargetId = video.Id,
            Status = AiRunStatus.Completed,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(new AiDataSelectorDto("ext:ai.faces", null, null, null, "video", video.Id, ["embedding", "detection", "segment", "face"]), dryRun: true);

        Assert.Equal(1, result.RemovedCounts["aiRun"]);
        Assert.Single(await db.AiRuns.ToListAsync());
    }

    [Fact]
    public async Task PurgeAsync_RemovesAiTagApplicationsButKeepsManualTags()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Video" };
        var sharedTag = new Tag { Name = "Shared" };
        var aiOnlyTag = new Tag { Name = "AI Only" };
        db.AddRange(video, sharedTag, aiOnlyTag);
        await db.SaveChangesAsync();

        db.Set<VideoTag>().AddRange(
            new VideoTag { VideoId = video.Id, TagId = sharedTag.Id },
            new VideoTag { VideoId = video.Id, TagId = aiOnlyTag.Id });
        db.TagApplications.AddRange(
            new TagApplication
            {
                HostType = AffinityHostType.Video,
                HostId = video.Id,
                TagId = sharedTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-tagging",
                ModelKey = "tagger-v1",
            },
            new TagApplication
            {
                HostType = AffinityHostType.Video,
                HostId = video.Id,
                TagId = sharedTag.Id,
                SourceKey = "user",
                SourceRunId = string.Empty,
                ModelKey = string.Empty,
            },
            new TagApplication
            {
                HostType = AffinityHostType.Video,
                HostId = video.Id,
                TagId = aiOnlyTag.Id,
                SourceKey = "ext:ai.tagging",
                SourceRunId = "run-tagging",
                ModelKey = "tagger-v1",
            });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Video,
            HostId = video.Id,
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

        var videoTags = await db.Set<VideoTag>().OrderBy(videoTag => videoTag.TagId).ToListAsync();
        var remainingApplications = await db.TagApplications.OrderBy(application => application.TagId).ToListAsync();

        Assert.Single(videoTags);
        Assert.Equal(sharedTag.Id, videoTags[0].TagId);
        Assert.Single(remainingApplications);
        Assert.Equal("user", remainingApplications[0].SourceKey);
        Assert.Empty(await db.Segments.ToListAsync());
    }

    [Fact]
    public async Task DryRun_ReturnsCounts_WithoutMutating()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Dry Run Video" };
        var image = new Image { Title = "Dry Run Image" };
        var tag = new Tag { Name = "Dry Tag" };
        db.AddRange(video, image, tag);
        await db.SaveChangesAsync();

        db.Set<VideoTag>().Add(new VideoTag { VideoId = video.Id, TagId = tag.Id });
        db.TagApplications.Add(new TagApplication
        {
            HostType = AffinityHostType.Video,
            HostId = video.Id,
            TagId = tag.Id,
            SourceKey = "ext:ai.tagging",
            SourceRunId = "dry-run-1",
            ModelKey = "tagger-v1",
        });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Video,
            HostId = video.Id,
            StartSec = 5,
            EndSec = 8,
            Kind = "tag",
            SourceKey = "ext:ai.tagging",
            SourceRunId = "dry-run-1",
        });
        db.Set<Detection>().Add(new Detection
        {
            HostType = DetectionHostType.Video,
            HostId = video.Id,
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
        Assert.Single(await db.Set<VideoTag>().ToListAsync());
    }

    [Fact]
    public async Task DryRun_WithFaceKind_DoesNotDoubleCountFaceOwnedArtifacts()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Face Video" };
        db.Videos.Add(video);
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
            HostType = DetectionHostType.Video,
            HostId = video.Id,
            Class = "face",
            Score = 0.97f,
            RefKind = "face",
            RefId = face.Id,
            SourceKey = "ext:ai.faces",
            SourceRunId = "face-run-1",
        });
        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Video,
            HostId = video.Id,
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

    [Fact]
    public async Task PurgeAsync_ByAiFacesSource_RemovesFacesResolvedFromAppearances()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Face Video" };
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        var face = new Face
        {
            Label = "AI Identity",
            PrimarySourceKey = "face-0001",
        };
        db.Faces.Add(face);
        await db.SaveChangesAsync();

        db.AiRuns.Add(new AiRun
        {
            RunKey = "run-face-appearance-purge",
            SourceKey = "ext:ai.core",
            TargetType = AiRunTargetType.Video,
            TargetId = video.Id,
            Status = AiRunStatus.Completed,
        });
        db.FaceAppearances.Add(new FaceAppearance
        {
            FaceId = face.Id,
            HostType = FaceAppearanceHostType.Video,
            HostId = video.Id,
            SourceKey = "ext:ai.faces",
            SourceRunId = "run-face-appearance-purge",
            SampleCount = 2,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.PurgeAsync(new AiDataSelectorDto("ext:ai.faces", null, null, null, "video", video.Id, ["face"]));

        Assert.Equal(1, result.RemovedCounts["face"]);
        Assert.Equal(1, result.RemovedCounts["aiRun"]);
        Assert.Empty(await db.Faces.ToListAsync());
        Assert.Empty(await db.FaceAppearances.ToListAsync());
        Assert.Empty(await db.AiRuns.ToListAsync());
    }

    [Fact]
    public async Task PurgeAsync_EvictsCachedVideoSpanResultsWhenVideoSegmentsAreDeleted()
    {
        await using var environment = await CreateEnvironmentAsync();
        var db = environment.Context;

        var video = new Video { Title = "Cached Video" };
        db.Videos.Add(video);
        await db.SaveChangesAsync();

        db.Segments.Add(new Segment
        {
            HostType = SegmentHostType.Video,
            HostId = video.Id,
            StartSec = 12,
            EndSec = 18,
            Kind = "face",
            SourceKey = "ext:ai.faces",
            SourceRunId = "run-cache-evict",
        });
        await db.SaveChangesAsync();

        var resolver = new SegmentSpanResolver(db, new CurrentPrincipalAccessor(), new MemoryCache(new MemoryCacheOptions()));
        var request = new SegmentSpanQueryRequestDto(
            Profile: null,
            Operator: "union",
            Operands:
            [
                new SegmentSpanOperandDto(
                    SourceKey: "ext:ai.faces",
                    Kind: "face",
                    TagIds: null,
                    MinConfidence: null,
                    RefIds: null),
            ],
            MergeGapSec: 0,
            MinDurationSec: 0);

        var cachedBeforePurge = await resolver.QueryVideoAsync(video.Id, request, CancellationToken.None);
        Assert.Single(cachedBeforePurge);

        var service = CreateService(db, resolver);
        var result = await service.PurgeAsync(new AiDataSelectorDto("ext:ai.faces", null, null, null, "video", video.Id, ["segment"]));

        Assert.Equal(1, result.RemovedCounts["segment"]);
        Assert.Empty(await db.Segments.ToListAsync());

        var cachedAfterPurge = await resolver.QueryVideoAsync(video.Id, request, CancellationToken.None);
        Assert.Empty(cachedAfterPurge);
    }

    [Fact]
    public async Task DeleteEmbeddingsAsync_LargeMatchSet_DeletesInMultipleBatches()
    {
        var saveChangesCounter = new SaveChangesCounterInterceptor();
        await using var environment = await CreateEnvironmentAsync(saveChangesCounter);
        var db = environment.Context;

        var image = new Image { Title = "Batch Image" };
        db.Images.Add(image);
        await db.SaveChangesAsync();

        var embeddings = Enumerable.Range(0, 12_000)
            .Select(_ => new Embedding
            {
                HostType = EmbeddingHostType.Image,
                HostId = image.Id,
                Kind = "clip.image",
                Modality = EmbeddingModality.Visual,
                Dim = 2,
                Vector = new Pgvector.Vector(new float[] { 0.1f, 0.2f }),
                SourceKey = "ext:ai.visual",
                SourceRunId = "batch-run",
            })
            .ToList();

        db.Embeddings.AddRange(embeddings);
        await db.SaveChangesAsync();

        saveChangesCounter.Reset();

        var service = CreateService(db);
        var removed = await service.DeleteEmbeddingsAsync(new AiDataSelectorDto("ext:ai.visual", "batch-run", null, null, null, null, null));

        Assert.Equal(12_000, removed);
        Assert.Equal(0, await db.Embeddings.CountAsync());
        Assert.Equal(3, saveChangesCounter.SaveChangesCalls);
    }

    private static AiDataPurgeService CreateService(CoveContext context, SegmentSpanResolver? spanResolver = null)
        => new(context, [], new StubBlobService(), NullLogger<AiDataPurgeService>.Instance, spanResolver);

    private static async Task<TestEnvironment> CreateEnvironmentAsync(params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var optionsBuilder = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection);

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        var options = optionsBuilder.Options;

        var context = new AiDataTestContext(options);
        await context.Database.EnsureCreatedAsync();
        return new TestEnvironment(connection, context);
    }

    private sealed class AiDataTestContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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

    private sealed class SaveChangesCounterInterceptor : SaveChangesInterceptor
    {
        public int SaveChangesCalls { get; private set; }

        public void Reset() => SaveChangesCalls = 0;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
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

