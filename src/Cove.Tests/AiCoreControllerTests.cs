using System.Text.Json;
using Cove.Api.Controllers;
using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public class AiCoreControllerTests
{
    [Fact]
    public async Task FacesController_CanCreateUpdateLinkMergeAndFindSimilarFaces()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var performer = new Performer { Name = "Alex" };
        context.Performers.Add(performer);
        await context.SaveChangesAsync();

        var embeddingService = new EmbeddingService(context, []);
        var controller = new FacesController(
            context,
            embeddingService,
            new StubBlobService(new Dictionary<string, (byte[] Bytes, string ContentType)>()),
            new FacePerformerPropagationService(context),
            Array.Empty<IFaceLifecycleParticipant>(),
            NullLogger<FacesController>.Instance);

        var firstCreate = await controller.Create(new FaceCreateDto("Lead", null, false, "ext:ai.faces"), CancellationToken.None);
        var firstCreated = Assert.IsType<CreatedAtActionResult>(firstCreate.Result);
        var firstFace = Assert.IsType<FaceDto>(firstCreated.Value);

        var secondCreate = await controller.Create(new FaceCreateDto("Support", null, false, "ext:ai.faces"), CancellationToken.None);
        var secondCreated = Assert.IsType<CreatedAtActionResult>(secondCreate.Result);
        var secondFace = Assert.IsType<FaceDto>(secondCreated.Value);

        var updateResult = await controller.Update(firstFace.Id, new FaceUpdateDto("Lead Updated", performer.Id, false, "user"), CancellationToken.None);
        var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updatedFace = Assert.IsType<FaceDto>(updateOk.Value);
        Assert.Equal("Lead Updated", updatedFace.Label);
        Assert.Equal(performer.Id, updatedFace.PerformerId);
        Assert.Equal("Alex", updatedFace.PerformerName);

        var linkResult = await controller.Link(secondFace.Id, new FaceLinkDto(performer.Id), CancellationToken.None);
        var linkOk = Assert.IsType<OkObjectResult>(linkResult.Result);
        var linkedFace = Assert.IsType<FaceDto>(linkOk.Value);
        Assert.Equal(performer.Id, linkedFace.PerformerId);

        context.Embeddings.AddRange(
            new Embedding
            {
                HostType = EmbeddingHostType.Face,
                HostId = firstFace.Id,
                Kind = "face.arcface",
                KindFamily = "face.arcface",
                Modality = EmbeddingModality.Face,
                IsSemantic = true,
                Dim = 3,
                Vector = new Vector(new[] { 1f, 0f, 0f }),
                SourceKey = "ext:ai.faces",
            },
            new Embedding
            {
                HostType = EmbeddingHostType.Face,
                HostId = secondFace.Id,
                Kind = "face.arcface",
                KindFamily = "face.arcface",
                Modality = EmbeddingModality.Face,
                IsSemantic = true,
                Dim = 3,
                Vector = new Vector(new[] { 0.95f, 0.05f, 0f }),
                SourceKey = "ext:ai.faces",
            });
        await context.SaveChangesAsync();

        var similarResult = await controller.GetSimilar(firstFace.Id, "face.arcface", null, null, null, 1, 5, 5, CancellationToken.None);
        var similarOk = Assert.IsType<OkObjectResult>(similarResult.Result);
        var similarFaces = Assert.IsType<PaginatedResponse<FaceSimilarDto>>(similarOk.Value);
        var match = Assert.Single(similarFaces.Items);
        Assert.Equal(secondFace.Id, match.Id);

        var mergeResult = await controller.MergeInto(secondFace.Id, new FaceMergeDto(firstFace.Id), CancellationToken.None);
        var mergeOk = Assert.IsType<OkObjectResult>(mergeResult.Result);
        var mergedFace = Assert.IsType<FaceDto>(mergeOk.Value);
        Assert.Equal(firstFace.Id, mergedFace.MergedIntoFaceId);

        var ignoreResult = await controller.SetIgnored(firstFace.Id, new FaceIgnoreDto(true), CancellationToken.None);
        var ignoreOk = Assert.IsType<OkObjectResult>(ignoreResult.Result);
        var ignoredFace = Assert.IsType<FaceDto>(ignoreOk.Value);
        Assert.True(ignoredFace.Ignored);
    }

    [Fact]
    public async Task FacesController_CanListDetectionsPointingToFace()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var face = new Face { Label = "Lead", PrimarySourceKey = "ext:ai.faces" };
        var image = new Image { Title = "Still" };
        var scene = new Scene { Title = "Clip" };
        context.Faces.Add(face);
        context.Images.Add(image);
        context.Scenes.Add(scene);
        await context.SaveChangesAsync();

        context.Detections.AddRange(
            new Detection
            {
                HostType = DetectionHostType.Image,
                HostId = image.Id,
                FrameWidth = 1200,
                FrameHeight = 1600,
                Class = "face",
                Score = 0.92f,
                X = 120,
                Y = 180,
                W = 240,
                H = 300,
                RefKind = "face",
                RefId = face.Id,
                SourceKey = "ext:ai.faces",
            },
            new Detection
            {
                HostType = DetectionHostType.Scene,
                HostId = scene.Id,
                ObservedAtSec = 33.5,
                FrameWidth = 1920,
                FrameHeight = 1080,
                Class = "face",
                Score = 0.89f,
                X = 640,
                Y = 160,
                W = 220,
                H = 260,
                RefKind = "face",
                RefId = face.Id,
                SourceKey = "ext:ai.faces",
            });
        await context.SaveChangesAsync();

        var embeddingService = new EmbeddingService(context, []);
        var controller = new FacesController(
            context,
            embeddingService,
            new StubBlobService(new Dictionary<string, (byte[] Bytes, string ContentType)>()),
            new FacePerformerPropagationService(context),
            Array.Empty<IFaceLifecycleParticipant>(),
            NullLogger<FacesController>.Instance);

        var result = await controller.GetDetections(face.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var detections = Assert.IsAssignableFrom<IReadOnlyList<DetectionDto>>(ok.Value);
        Assert.Equal(2, detections.Count);
        Assert.Contains(detections, detection => detection.HostType == DetectionHostType.Image && detection.HostId == image.Id);
        Assert.Contains(detections, detection => detection.HostType == DetectionHostType.Scene && detection.HostId == scene.Id);
    }

    [Fact]
    public async Task FacesController_GetById_UsesCanonicalFaceImageRouteWhenCoverExists()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        var face = new Face
        {
            Label = "Lead",
            CoverBlobId = "blob-1",
            PrimarySourceKey = "ext:ai.faces",
        };
        context.Faces.Add(face);
        await context.SaveChangesAsync();

        var embeddingService = new EmbeddingService(context, []);
        var controller = new FacesController(
            context,
            embeddingService,
            new StubBlobService(new Dictionary<string, (byte[] Bytes, string ContentType)>()),
            new FacePerformerPropagationService(context),
            Array.Empty<IFaceLifecycleParticipant>(),
            NullLogger<FacesController>.Instance);

        var result = await controller.GetById(face.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<FaceDto>(ok.Value);

        Assert.NotNull(dto.CoverImageUrl);
        Assert.StartsWith($"/api/faces/{face.Id}/image?max=640&v=", dto.CoverImageUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/entity-images/", dto.CoverImageUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FacesController_GetById_PropagatesBearerTokenIntoCoverImageUrl()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        var face = new Face
        {
            Label = "Lead",
            CoverBlobId = "blob-1",
            PrimarySourceKey = "ext:ai.faces",
        };
        context.Faces.Add(face);
        await context.SaveChangesAsync();

        var embeddingService = new EmbeddingService(context, []);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer owner-token";
        var controller = new FacesController(
            context,
            embeddingService,
            new StubBlobService(new Dictionary<string, (byte[] Bytes, string ContentType)>()),
            new FacePerformerPropagationService(context),
            Array.Empty<IFaceLifecycleParticipant>(),
            NullLogger<FacesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            },
        };

        var result = await controller.GetById(face.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<FaceDto>(ok.Value);

        Assert.NotNull(dto.CoverImageUrl);
        Assert.Contains("access_token=owner-token", dto.CoverImageUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntityImageController_GetFaceImage_ReturnsStoredBlob()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;
        var bytes = new byte[] { 1, 2, 3, 4 };

        var face = new Face
        {
            Label = "Lead",
            CoverBlobId = "blob-1",
            PrimarySourceKey = "ext:ai.faces",
        };
        context.Faces.Add(face);
        await context.SaveChangesAsync();

        var controller = new EntityImageController(
            context,
            new StubBlobService(new Dictionary<string, (byte[] Bytes, string ContentType)>
            {
                ["blob-1"] = (bytes, "image/jpeg"),
            }),
            new StubThumbnailService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.GetFaceImage(face.Id, null, null, CancellationToken.None);
        var file = Assert.IsType<FileStreamResult>(result);

        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("public, max-age=3600", controller.Response.Headers.CacheControl.ToString());

        await using var output = new MemoryStream();
        await file.FileStream.CopyToAsync(output);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task FacesController_GetSuggestions_ReturnsEmptyListWhenNoSuggestersRegistered()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        var face = new Face
        {
            Label = "Lead",
            PrimarySourceKey = "ext:ai.faces",
        };
        context.Faces.Add(face);
        await context.SaveChangesAsync();

        var embeddingService = new EmbeddingService(context, []);
        var controller = new FacesController(
            context,
            embeddingService,
            new StubBlobService(new Dictionary<string, (byte[] Bytes, string ContentType)>()),
            new FacePerformerPropagationService(context),
            Array.Empty<IFaceLifecycleParticipant>(),
            NullLogger<FacesController>.Instance);

        var result = await controller.GetSuggestions(face.Id, 5, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var suggestions = Assert.IsAssignableFrom<IReadOnlyList<FaceSuggestionDto>>(ok.Value);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task EmbeddingsController_CanListAndSearchEmbeddingsUsingSQLiteFallback()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        context.Embeddings.AddRange(
            new Embedding
            {
                HostType = EmbeddingHostType.Scene,
                HostId = 11,
                Kind = "scene.clip",
                KindFamily = "scene.clip",
                Modality = EmbeddingModality.Visual,
                IsSemantic = true,
                Dim = 2,
                Vector = new Vector(new[] { 1f, 0f }),
                SourceKey = "ext:ai.clip",
                SectionIndex = 0,
            },
            new Embedding
            {
                HostType = EmbeddingHostType.Scene,
                HostId = 12,
                Kind = "scene.clip",
                KindFamily = "scene.clip",
                Modality = EmbeddingModality.Visual,
                IsSemantic = true,
                Dim = 2,
                Vector = new Vector(new[] { 0f, 1f }),
                SourceKey = "ext:ai.clip",
                SectionIndex = 1,
            });
        await context.SaveChangesAsync();

        var embeddingService = new EmbeddingService(context, []);
        var controller = new EmbeddingsController(context, embeddingService, embeddingService);

        var listResult = await controller.List(EmbeddingHostType.Scene, null, null, "scene.clip", null, null, 1, 20, CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var list = Assert.IsType<PaginatedResponse<EmbeddingDto>>(listOk.Value);
        Assert.Equal(2, list.TotalCount);

        var searchResult = await controller.Search(
            new EmbeddingSearchRequestDto(
                QueryText: null,
                QueryVector: [0.9f, 0.1f],
                Kind: null,
                KindFamily: "scene.clip",
                HostType: EmbeddingHostType.Scene,
                HostId: null,
                Modality: EmbeddingModality.Visual,
                IsSemantic: true,
                SourceKey: "ext:ai.clip",
                K: 2),
            CancellationToken.None);

        var searchOk = Assert.IsType<OkObjectResult>(searchResult.Result);
        var matches = Assert.IsAssignableFrom<IReadOnlyList<EmbeddingSearchResultDto>>(searchOk.Value);
        Assert.Equal(2, matches.Count);
        Assert.Equal(11, matches[0].HostId);
        Assert.True(matches[0].Distance < matches[1].Distance);
    }

    [Fact]
    public async Task AiRunsController_CanListAndGetRunProvenance()
    {
        await using var scope = await CreateContextAsync();
        var context = scope.Context;

        context.AiRuns.AddRange(
            new AiRun
            {
                RunKey = "run-a",
                SourceKey = "ext:ai.faces",
                TargetType = AiRunTargetType.Scene,
                TargetId = 10,
                Trigger = "manual",
                JobId = "job-1",
                Status = AiRunStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                CompletedAt = DateTime.UtcNow.AddMinutes(-1),
                Summary = JsonDocument.Parse("{" + "\"faces\":4}"),
            },
            new AiRun
            {
                RunKey = "run-b",
                SourceKey = "ext:ai.clip",
                TargetType = AiRunTargetType.Image,
                TargetId = 20,
                Trigger = "scheduled",
                Status = AiRunStatus.Running,
                StartedAt = DateTime.UtcNow,
            });
        await context.SaveChangesAsync();

        var controller = new AiRunsController(context);

        var listResult = await controller.List(AiRunTargetType.Scene, 10, "ext:ai.faces", null, AiRunStatus.Completed, 1, 20, CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var list = Assert.IsType<PaginatedResponse<AiRunDto>>(listOk.Value);
        var run = Assert.Single(list.Items);
        Assert.Equal("run-a", run.RunKey);
        Assert.True(run.Summary.HasValue);
        Assert.Equal(4, run.Summary.Value.GetProperty("faces").GetInt32());

        var getResult = await controller.GetById(run.Id, CancellationToken.None);
        var getOk = Assert.IsType<OkObjectResult>(getResult.Result);
        var fetched = Assert.IsType<AiRunDto>(getOk.Value);
        Assert.Equal(run.Id, fetched.Id);
    }

    private static async Task<TestContextScope> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AiCoreTestContext(options);
        await context.Database.EnsureCreatedAsync();
        return new TestContextScope(context, connection);
    }

    private sealed class AiCoreTestContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

        }
    }

    private sealed class TestContextScope(CoveContext context, SqliteConnection connection) : IAsyncDisposable
    {
        public CoveContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class StubBlobService(Dictionary<string, (byte[] Bytes, string ContentType)> blobs) : IBlobService
    {
        public Task<string> StoreBlobAsync(Stream data, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<(Stream Stream, string ContentType)?> GetBlobAsync(string blobId, CancellationToken ct = default)
        {
            if (!blobs.TryGetValue(blobId, out var blob))
                return Task.FromResult<(Stream, string)?>(null);

            return Task.FromResult<(Stream, string)?>(
                (new MemoryStream(blob.Bytes, writable: false), blob.ContentType));
        }

        public Task DeleteBlobAsync(string blobId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubThumbnailService : IThumbnailService
    {
        public Task<string?> GetSceneThumbnailPathAsync(int sceneId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string?> GetImageFilePathAsync(int imageId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetImageStreamAsync(int imageId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetImageThumbnailStreamAsync(int imageId, int maxDimension, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetBlobImageThumbnailStreamAsync(string blobId, int maxDimension, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task GenerateSceneThumbnailAsync(int sceneId, double? atSeconds = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task GenerateImageThumbnailAsync(int imageId, int maxDimension = 640, bool overwrite = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task GenerateScenePreviewAsync(int sceneId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task GenerateSegmentAnimatedPreviewAsync(int sceneId, double startSec, double? endSec = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task GenerateSceneSpriteAsync(int sceneId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public string GetThumbnailPathForScene(int sceneId)
            => throw new NotSupportedException();

        public string GetTimestampedThumbnailPath(int sceneId, double seconds)
            => throw new NotSupportedException();

        public string GetSegmentAnimatedPreviewPath(int sceneId, double seconds)
            => throw new NotSupportedException();

        public string GetPreviewPath(int sceneId)
            => throw new NotSupportedException();

        public string GetSpritePath(int sceneId)
            => throw new NotSupportedException();

        public string GetSpriteVttPath(int sceneId)
            => throw new NotSupportedException();

        public string StartGenerateAllThumbnails()
            => throw new NotSupportedException();
    }
}
