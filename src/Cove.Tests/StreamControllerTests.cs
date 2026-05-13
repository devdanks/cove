using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Cove.Api.Controllers;
using Cove.Api.Services;

namespace Cove.Tests;

public class StreamControllerTests
{
    [Fact]
    public void HeadPreview_ReturnsNotFound_WhenPreviewFileIsMissing()
    {
        var controller = CreateController(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.mp4"));

        var result = controller.HeadPreview(123);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task HeadPreview_ReturnsVideoHeaders_WhenPreviewFileExists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cove-preview-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

        try
        {
            var controller = CreateController(path);

            var result = controller.HeadPreview(123);

            Assert.IsType<OkResult>(result);
            Assert.Equal("video/mp4", controller.Response.ContentType);
            Assert.Equal(4, controller.Response.ContentLength);
            Assert.Equal("bytes", controller.Response.Headers["Accept-Ranges"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewStatus_ReturnsUnavailable_WhenPreviewFileIsMissing()
    {
        var controller = CreateController(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.mp4"));

        var result = controller.GetPreviewStatus(123);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)ok.Value!.GetType().GetProperty("available")!.GetValue(ok.Value)!);
    }

    private static StreamController CreateController(string previewPath)
    {
        return new StreamController(null!, new FakeThumbnailService(previewPath), null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private sealed class FakeThumbnailService(string previewPath) : IThumbnailService
    {
        public Task<string?> GetSceneThumbnailPathAsync(int sceneId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string?> GetImageFilePathAsync(int imageId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetImageStreamAsync(int imageId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetImageThumbnailStreamAsync(int imageId, int maxDimension = 640, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetBlobImageThumbnailStreamAsync(string blobId, int maxDimension = 640, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteSceneGeneratedFilesAsync(int sceneId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteImageGeneratedFilesAsync(int imageId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteBlobGeneratedFilesAsync(string blobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task GenerateSceneThumbnailAsync(int sceneId, double? atSeconds = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task GenerateImageThumbnailAsync(int imageId, int maxDimension = 640, bool overwrite = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task GenerateScenePreviewAsync(int sceneId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task GenerateSegmentAnimatedPreviewAsync(int sceneId, double startSec, double? endSec = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task GenerateSceneSpriteAsync(int sceneId, CancellationToken ct = default) => throw new NotImplementedException();
        public string GetThumbnailPathForScene(int sceneId) => throw new NotImplementedException();
        public string GetTimestampedThumbnailPath(int sceneId, double seconds) => throw new NotImplementedException();
        public string GetSegmentAnimatedPreviewPath(int sceneId, double seconds) => throw new NotImplementedException();
        public string GetPreviewPath(int sceneId) => previewPath;
        public string GetSpritePath(int sceneId) => throw new NotImplementedException();
        public string GetSpriteVttPath(int sceneId) => throw new NotImplementedException();
        public string StartGenerateAllThumbnails() => throw new NotImplementedException();
    }
}