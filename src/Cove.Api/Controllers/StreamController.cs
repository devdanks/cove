using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.StreamRead)]
public class StreamController(IStreamService streamService, IThumbnailService thumbnailService, ITranscodeService transcodeService, CoveContext db) : ControllerBase
{
    [HttpGet("scene/{sceneId:int}")]
    public async Task<IActionResult> StreamScene(int sceneId, CancellationToken ct)
    {
        var result = await streamService.GetSceneStream(sceneId, ct);
        if (result == null) return NotFound();

        var (stream, contentType, fileSize) = result.Value;
        Response.Headers["Accept-Ranges"] = "bytes";

        if (fileSize.HasValue)
            return File(stream, contentType, enableRangeProcessing: true);

        return File(stream, contentType);
    }

    [HttpGet("scene/{sceneId:int}/screenshot")]
    public async Task<IActionResult> GetScreenshot(int sceneId, [FromQuery] double? seconds, CancellationToken ct)
    {
        var result = await streamService.GetSceneScreenshot(sceneId, seconds, ct);
        if (result == null) return NotFound();

        var (stream, contentType, useLongCache) = result.Value;
        Response.Headers["Cache-Control"] = useLongCache
            ? "public, max-age=86400"
            : "no-store, no-cache, max-age=0, must-revalidate";
        return File(stream, contentType);
    }

    [HttpGet("scene/{sceneId:int}/segment-preview")]
    public async Task<IActionResult> GetSegmentPreview(int sceneId, [FromQuery] double seconds, CancellationToken ct)
    {
        var result = await streamService.GetSegmentAnimatedPreview(sceneId, seconds, ct);
        if (result == null) return NotFound();

        var (stream, contentType, useLongCache) = result.Value;
        Response.Headers["Cache-Control"] = useLongCache
            ? "public, max-age=86400"
            : "no-store, no-cache, max-age=0, must-revalidate";
        return File(stream, contentType);
    }

    [HttpGet("scene/{sceneId:int}/preview")]
    public IActionResult GetPreview(int sceneId)
    {
        var path = GetExistingPreviewPath(sceneId);
        if (path == null) return NotFound();

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        SetPreviewHeaders();
        return File(stream, "video/mp4", enableRangeProcessing: true);
    }

    [HttpHead("scene/{sceneId:int}/preview")]
    public IActionResult HeadPreview(int sceneId)
    {
        var path = GetExistingPreviewPath(sceneId);
        if (path == null) return NotFound();

        SetPreviewHeaders();
        Response.ContentType = "video/mp4";
        Response.ContentLength = new FileInfo(path).Length;
        return Ok();
    }

    [HttpGet("scene/{sceneId:int}/preview/status")]
    public IActionResult GetPreviewStatus(int sceneId)
    {
        return Ok(new { available = GetExistingPreviewPath(sceneId) != null });
    }

    private string? GetExistingPreviewPath(int sceneId)
    {
        var sourceSceneId = db is null ? sceneId : ResolveSourceSceneId(sceneId);
        if (!sourceSceneId.HasValue) return null;

        var path = thumbnailService.GetPreviewPath(sourceSceneId.Value);
        return System.IO.File.Exists(path) ? path : null;
    }

    private void SetPreviewHeaders()
    {
        Response.Headers["Cache-Control"] = "public, max-age=86400";
        Response.Headers["Accept-Ranges"] = "bytes";
    }

    [HttpGet("scene/{sceneId:int}/sprite")]
    public IActionResult GetSprite(int sceneId)
    {
        var sourceSceneId = db is null ? sceneId : ResolveSourceSceneId(sceneId);
        if (!sourceSceneId.HasValue) return NotFound();

        var path = thumbnailService.GetSpritePath(sourceSceneId.Value);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return File(stream, "image/jpeg");
    }

    [HttpGet("scene/{sceneId:int}/vtt/thumbs")]
    public IActionResult GetSpriteVtt(int sceneId)
    {
        var sourceSceneId = db is null ? sceneId : ResolveSourceSceneId(sceneId);
        if (!sourceSceneId.HasValue) return NotFound();

        var path = thumbnailService.GetSpriteVttPath(sourceSceneId.Value);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return File(stream, "text/vtt");
    }

    [HttpGet("image/{imageId:int}")]
    public async Task<IActionResult> GetImage(int imageId, CancellationToken ct)
    {
        var result = await thumbnailService.GetImageStreamAsync(imageId, ct);
        if (result == null) return NotFound();

        var (stream, contentType, supportsRangeRequests) = result.Value;

        Response.Headers["Cache-Control"] = "public, max-age=86400";

        if (supportsRangeRequests)
        {
            Response.Headers["Accept-Ranges"] = "bytes";
            return File(stream, contentType, enableRangeProcessing: true);
        }

        return File(stream, contentType);
    }

    [HttpGet("image/{imageId:int}/thumbnail")]
    public async Task<IActionResult> GetImageThumbnail(int imageId, [FromQuery] int? max, CancellationToken ct)
    {
        var result = await thumbnailService.GetImageThumbnailStreamAsync(imageId, max ?? 0, ct);
        if (result == null) return NotFound();

        var (stream, contentType, supportsRangeRequests) = result.Value;
        Response.Headers["Cache-Control"] = "public, max-age=86400";

        if (supportsRangeRequests)
        {
            Response.Headers["Accept-Ranges"] = "bytes";
            return File(stream, contentType, enableRangeProcessing: true);
        }

        return File(stream, contentType);
    }

    [HttpGet("detection/{detectionId:int}/crop")]
    public async Task<IActionResult> GetDetectionCrop(int detectionId, [FromQuery] int? max, CancellationToken ct)
    {
        var detection = await db.Detections
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == detectionId, ct);
        if (detection is null) return NotFound();

        if (detection.HostType == DetectionHostType.Scene)
        {
            if (max.GetValueOrDefault(640) > 640)
            {
                await EnsureHighResolutionDetectionFrameAsync(detection, ct);
            }

            var result = await streamService.GetSceneScreenshot(detection.HostId, detection.ObservedAtSec, ct);
            if (result is null) return NotFound();

            await using var stream = result.Value.stream;
            return await BuildDetectionCropResultAsync(detection, stream, max, ct);
        }

        if (detection.HostType == DetectionHostType.Image)
        {
            var result = await thumbnailService.GetImageStreamAsync(detection.HostId, ct);
            if (result is null) return NotFound();

            await using var stream = result.Value.stream;
            return await BuildDetectionCropResultAsync(detection, stream, max, ct);
        }

        return NotFound();
    }

    private async Task EnsureHighResolutionDetectionFrameAsync(Detection detection, CancellationToken ct)
    {
        if (!detection.ObservedAtSec.HasValue)
        {
            return;
        }

        var sourceSceneId = await ResolveSourceSceneIdAsync(detection.HostId, ct);
        if (!sourceSceneId.HasValue)
        {
            return;
        }

        var framePath = thumbnailService.GetTimestampedThumbnailPath(sourceSceneId.Value, detection.ObservedAtSec.Value);
        if (System.IO.File.Exists(framePath))
        {
            return;
        }

        await thumbnailService.GenerateSceneThumbnailAsync(sourceSceneId.Value, detection.ObservedAtSec.Value, ct);
    }

    [HttpGet("scene/{sceneId:int}/caption/{captionId:int}")]
    public async Task<IActionResult> GetCaption(int sceneId, int captionId, CancellationToken ct)
    {
        var sourceSceneId = await ResolveSourceSceneIdAsync(sceneId, ct);
        if (!sourceSceneId.HasValue) return NotFound();

        var caption = await db.VideoCaptions
            .Include(c => c.File)
            .FirstOrDefaultAsync(c => c.Id == captionId && c.File != null
                && db.Scenes.Any(s => s.Id == sourceSceneId.Value && s.Files.Any(f => f.Id == c.FileId)), ct);

        if (caption?.File == null) return NotFound();

        var videoDir = Path.GetDirectoryName(caption.File.Path);
        if (videoDir == null) return NotFound();

        var captionPath = Path.Combine(videoDir, caption.Filename);
        if (!System.IO.File.Exists(captionPath)) return NotFound();

        var contentType = caption.CaptionType == "srt" ? "application/x-subrip" : "text/vtt";
        var stream = new FileStream(captionPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        Response.Headers["Cache-Control"] = "public, max-age=3600";
        return File(stream, contentType);
    }

    [HttpGet("scene/{sceneId:int}/captions")]
    public async Task<IActionResult> GetCaptions(int sceneId, CancellationToken ct)
    {
        var sourceSceneId = await ResolveSourceSceneIdAsync(sceneId, ct);
        if (!sourceSceneId.HasValue) return NotFound();

        var scene = await db.Scenes
            .Include(s => s.Files).ThenInclude(f => f.Captions)
            .FirstOrDefaultAsync(s => s.Id == sourceSceneId.Value, ct);

        if (scene == null) return NotFound();

        var captions = scene.Files
            .SelectMany(f => f.Captions)
            .Select(c => new { c.Id, c.LanguageCode, c.CaptionType, c.Filename })
            .ToList();

        return Ok(captions);
    }

    // ===== Transcoding / HLS =====

    [HttpGet("scene/{sceneId:int}/transcode")]
    public async Task<IActionResult> TranscodeScene(int sceneId, [FromQuery] string? resolution, [FromQuery] double? start, CancellationToken ct)
    {
        var filePath = await GetSceneFilePathAsync(sceneId, ct);
        if (filePath == null) return NotFound();

        var startSeconds = start.HasValue && double.IsFinite(start.Value) ? Math.Max(0, start.Value) : 0;
        var stream = await transcodeService.TranscodeToMp4Async(filePath, resolution, startSeconds, ct);
        if (stream == null) return StatusCode(503, "Transcoding unavailable — FFmpeg not found");

        Response.Headers["Accept-Ranges"] = "none";
        return File(stream, "video/mp4");
    }

    [HttpGet("scene/{sceneId:int}/hls/master.m3u8")]
    public async Task<IActionResult> GetHlsMasterPlaylist(int sceneId, CancellationToken ct)
    {
        var sourceSceneId = await ResolveSourceSceneIdAsync(sceneId, ct);
        if (!sourceSceneId.HasValue) return NotFound();

        var file = await db.VideoFiles.FirstOrDefaultAsync(f => f.SceneId == sourceSceneId.Value, ct);
        if (file == null) return NotFound();

        var resolutions = transcodeService.GetAvailableResolutions(file.Width, file.Height);
        if (resolutions.Length == 0)
            resolutions = ["original"];

        // Build master playlist
        var lines = new List<string> { "#EXTM3U" };
        foreach (var res in resolutions)
        {
            var bw = res switch { "240p" => 400000, "360p" => 800000, "480p" => 1200000, "720p" => 2500000, "1080p" => 5000000, "1440p" => 8000000, "4K" => 15000000, _ => 5000000 };
            lines.Add($"#EXT-X-STREAM-INF:BANDWIDTH={bw},RESOLUTION={GetResForLabel(res)},NAME=\"{res}\"");
            lines.Add($"/api/stream/scene/{sceneId}/hls/{res}.m3u8");
        }

        Response.Headers["Cache-Control"] = "no-cache";
        return Content(string.Join("\n", lines), "application/vnd.apple.mpegurl");
    }

    [HttpGet("scene/{sceneId:int}/hls/{profile}.m3u8")]
    public async Task<IActionResult> GetHlsPlaylist(int sceneId, string profile, CancellationToken ct)
    {
        var filePath = await GetSceneFilePathAsync(sceneId, ct);
        if (filePath == null) return NotFound();

        var resolution = profile == "original" ? null : profile;
        var manifest = await transcodeService.GenerateHlsManifestAsync(sceneId, filePath, resolution, ct);
        if (manifest == null) return StatusCode(503, "HLS generation failed — FFmpeg not found or error occurred");

        // Rewrite segment paths to use API URLs
        manifest = manifest.Replace($"{resolution ?? "original"}_", $"/api/stream/scene/{sceneId}/hls/segment/{resolution ?? "original"}_");

        Response.Headers["Cache-Control"] = "no-cache";
        return Content(manifest, "application/vnd.apple.mpegurl");
    }

    [HttpGet("scene/{sceneId:int}/hls/segment/{segment}")]
    public async Task<IActionResult> GetHlsSegment(int sceneId, string segment, CancellationToken ct)
    {
        var stream = await transcodeService.GetHlsSegmentAsync(sceneId, segment, ct);
        if (stream == null) return NotFound();

        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return File(stream, "video/mp2t");
    }

    [HttpGet("scene/{sceneId:int}/resolutions")]
    public async Task<IActionResult> GetAvailableResolutions(int sceneId, CancellationToken ct)
    {
        var sourceSceneId = await ResolveSourceSceneIdAsync(sceneId, ct);
        if (!sourceSceneId.HasValue) return NotFound();

        var file = await db.VideoFiles.FirstOrDefaultAsync(f => f.SceneId == sourceSceneId.Value, ct);
        if (file == null) return NotFound();

        return Ok(transcodeService.GetAvailableResolutions(file.Width, file.Height));
    }

    private async Task<string?> GetSceneFilePathAsync(int sceneId, CancellationToken ct)
    {
        var sourceSceneId = await ResolveSourceSceneIdAsync(sceneId, ct);
        if (!sourceSceneId.HasValue) return null;

        var videoFile = await db.VideoFiles
            .Include(f => f.ParentFolder)
            .FirstOrDefaultAsync(f => f.SceneId == sourceSceneId.Value, ct);

        if (videoFile == null) return null;

        var filePath = videoFile.ParentFolder != null
            ? Path.Combine(videoFile.ParentFolder.Path, videoFile.Basename)
            : videoFile.Basename;

        return System.IO.File.Exists(filePath) ? filePath : null;
    }

    private int? ResolveSourceSceneId(int sceneId)
        => db.Scenes.AsNoTracking()
            .Where(scene => scene.Id == sceneId)
            .Select(scene => (int?)(scene.ParentSceneId ?? scene.Id))
            .FirstOrDefault();

    private async Task<int?> ResolveSourceSceneIdAsync(int sceneId, CancellationToken ct)
    {
        var scene = await db.Scenes.AsNoTracking()
            .Where(item => item.Id == sceneId)
            .Select(item => new { item.Id, item.ParentSceneId })
            .FirstOrDefaultAsync(ct);

        return scene?.ParentSceneId ?? scene?.Id;
    }

    private async Task<IActionResult> BuildDetectionCropResultAsync(Detection detection, Stream sourceStream, int? max, CancellationToken ct)
    {
        try
        {
            using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(sourceStream, ct);
            image.Mutate(static context => context.AutoOrient());

            var cropRect = BuildDetectionCropRectangle(image.Width, image.Height, detection);
            if (cropRect is null)
            {
                return NotFound();
            }

            image.Mutate(context => context.Crop(cropRect.Value));

            var maxDimension = Math.Clamp(max.GetValueOrDefault(640), 64, 2048);
            if (Math.Max(image.Width, image.Height) > maxDimension)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxDimension, maxDimension),
                }));
            }

            var output = new MemoryStream();
            await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 88 }, ct);
            output.Position = 0;

            Response.Headers["Cache-Control"] = "public, max-age=86400";
            return File(output, "image/jpeg");
        }
        catch when (!ct.IsCancellationRequested)
        {
            return NotFound();
        }
    }

    private static Rectangle? BuildDetectionCropRectangle(int imageWidth, int imageHeight, Detection detection)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || detection.W <= 0 || detection.H <= 0)
        {
            return null;
        }

        var normalized = detection.X >= 0
            && detection.Y >= 0
            && detection.X <= 1.000001f
            && detection.Y <= 1.000001f
            && detection.W <= 1.000001f
            && detection.H <= 1.000001f;

        var x = (double)detection.X;
        var y = (double)detection.Y;
        var width = (double)detection.W;
        var height = (double)detection.H;

        if (normalized)
        {
            x *= imageWidth;
            width *= imageWidth;
            y *= imageHeight;
            height *= imageHeight;
        }
        else if (detection.FrameWidth > 0 && detection.FrameHeight > 0)
        {
            x = x / detection.FrameWidth * imageWidth;
            width = width / detection.FrameWidth * imageWidth;
            y = y / detection.FrameHeight * imageHeight;
            height = height / detection.FrameHeight * imageHeight;
        }

        var left = Clamp((int)Math.Floor(x), 0, imageWidth - 1);
        var top = Clamp((int)Math.Floor(y), 0, imageHeight - 1);
        var right = Clamp((int)Math.Ceiling(x + width), left + 1, imageWidth);
        var bottom = Clamp((int)Math.Ceiling(y + height), top + 1, imageHeight);
        var boxWidth = Math.Max(1, right - left);
        var boxHeight = Math.Max(1, bottom - top);
        var side = (int)Math.Ceiling(Math.Max(boxWidth, boxHeight) * 1.8);
        side = Math.Clamp(side, 1, Math.Min(imageWidth, imageHeight));

        var centerX = left + boxWidth / 2.0;
        var centerY = top + boxHeight / 2.0 - boxHeight * 0.1;
        var cropLeft = Clamp((int)Math.Round(centerX - side / 2.0), 0, Math.Max(0, imageWidth - side));
        var cropTop = Clamp((int)Math.Round(centerY - side / 2.0), 0, Math.Max(0, imageHeight - side));

        return new Rectangle(cropLeft, cropTop, side, side);
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    private static string GetResForLabel(string label) => label switch
    {
        "240p" => "426x240",
        "360p" => "640x360",
        "480p" => "854x480",
        "720p" => "1280x720",
        "1080p" => "1920x1080",
        "1440p" => "2560x1440",
        "4K" => "3840x2160",
        _ => "1920x1080"
    };
}
