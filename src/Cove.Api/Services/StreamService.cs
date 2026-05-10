using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Cove.Core.Interfaces;
using Cove.Data;

namespace Cove.Api.Services;

public class StreamService(IServiceScopeFactory scopeFactory, IThumbnailService thumbnailService, IBlobService blobService) : IStreamService
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv",
        [".flv"] = "video/x-flv",
        [".m4v"] = "video/x-m4v",
        [".mpg"] = "video/mpeg",
        [".mpeg"] = "video/mpeg",
        [".ts"] = "video/mp2t",
        [".rmvb"] = "application/vnd.rn-realmedia-vbr",
        [".rm"] = "application/vnd.rn-realmedia",
    };

    public async Task<(Stream stream, string contentType, long? fileSize)?> GetSceneStream(int sceneId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();

        var sourceSceneId = await ResolveSourceSceneIdAsync(db, sceneId, ct);
        if (!sourceSceneId.HasValue) return null;

        var videoFile = await db.VideoFiles
            .Include(f => f.ParentFolder)
            .FirstOrDefaultAsync(f => f.SceneId == sourceSceneId.Value, ct);

        if (videoFile == null) return null;

        var filePath = videoFile.ParentFolder != null
            ? Path.Combine(videoFile.ParentFolder.Path, videoFile.Basename)
            : videoFile.Basename;

        if (!File.Exists(filePath)) return null;

        var ext = Path.GetExtension(filePath);
        var contentType = MimeTypes.GetValueOrDefault(ext, "application/octet-stream");
        var fileInfo = new FileInfo(filePath);

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return (stream, contentType, fileInfo.Length);
    }

    public async Task<(Stream stream, string contentType, bool useLongCache)?> GetSceneScreenshot(int sceneId, double? seconds, CancellationToken ct = default)
    {
        using var scope = scopeFactory?.CreateScope();
        var db = scope?.ServiceProvider.GetService<CoveContext>();
        var sceneSource = db is null ? new SceneSource(sceneId, null) : await ResolveSceneSourceAsync(db, sceneId, ct);
        if (sceneSource is null) return null;

        var sourceSceneId = sceneSource.Value.SourceSceneId;
        var effectiveSeconds = seconds ?? sceneSource.Value.ClipStartSec;

        // For timestamped thumbnails, only serve from cache — never generate on demand.
        // Thumbnail generation is exclusively the job of the generate task.
        if (effectiveSeconds.HasValue)
        {
            var tsPath = thumbnailService.GetTimestampedThumbnailPath(sourceSceneId, effectiveSeconds.Value);
            if (File.Exists(tsPath))
            {
                var stream = new FileStream(tsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
                return (stream, "image/jpeg", true);
            }

            var spriteFrame = await TryOpenSpriteFrameAsync(sourceSceneId, effectiveSeconds.Value, ct);
            if (spriteFrame != null) return spriteFrame;
        }

        var customCoverBlobId = db is null
            ? null
            : await db.Scenes
                .AsNoTracking()
                .Where(scene => scene.Id == sceneId)
                .Select(scene => scene.ImageBlobId)
                .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(customCoverBlobId))
        {
            var customCover = await blobService.GetBlobAsync(customCoverBlobId, ct);
            if (customCover != null)
            {
                return (customCover.Value.Stream, customCover.Value.ContentType, false);
            }
        }

        // Default cover thumbnail (no timestamp) — also only served from cache
        var thumbPath = await thumbnailService.GetSceneThumbnailPathAsync(sourceSceneId, ct);
        if (thumbPath == null) return null;

        var defaultStream = new FileStream(thumbPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        return (defaultStream, "image/jpeg", true);
    }

    public async Task<(Stream stream, string contentType, bool useLongCache)?> GetSegmentAnimatedPreview(int sceneId, double seconds, CancellationToken ct = default)
    {
        using var scope = scopeFactory?.CreateScope();
        var db = scope?.ServiceProvider.GetService<CoveContext>();
        var sourceSceneId = db is null ? sceneId : await ResolveSourceSceneIdAsync(db, sceneId, ct);
        if (!sourceSceneId.HasValue) return null;

        var previewPath = thumbnailService.GetSegmentAnimatedPreviewPath(sourceSceneId.Value, seconds);
        if (!File.Exists(previewPath))
            return await TryOpenSpriteFrameAsync(sourceSceneId.Value, seconds, ct);

        Stream stream = new FileStream(previewPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        return (stream, "image/webp", true);
    }

    private static async Task<int?> ResolveSourceSceneIdAsync(CoveContext db, int sceneId, CancellationToken ct)
        => (await ResolveSceneSourceAsync(db, sceneId, ct))?.SourceSceneId;

    private static async Task<SceneSource?> ResolveSceneSourceAsync(CoveContext db, int sceneId, CancellationToken ct)
    {
        var scene = await db.Scenes.AsNoTracking()
            .Where(item => item.Id == sceneId)
            .Select(item => new { item.Id, item.ParentSceneId, item.ClipStartSec })
            .FirstOrDefaultAsync(ct);

        return scene is null
            ? null
            : new SceneSource(scene.ParentSceneId ?? scene.Id, scene.ClipStartSec);
    }

    private readonly record struct SceneSource(int SourceSceneId, double? ClipStartSec);

    private async Task<(Stream stream, string contentType, bool useLongCache)?> TryOpenSpriteFrameAsync(int sceneId, double seconds, CancellationToken ct)
    {
        var vttPath = thumbnailService.GetSpriteVttPath(sceneId);
        var spritePath = thumbnailService.GetSpritePath(sceneId);
        if (!File.Exists(vttPath) || !File.Exists(spritePath)) return null;

        var frame = await FindSpriteFrameAsync(vttPath, seconds, ct);
        if (frame == null) return null;

        using var image = await Image.LoadAsync(spritePath, ct);
        var bounds = new Rectangle(0, 0, image.Width, image.Height);
        var crop = Rectangle.Intersect(bounds, frame.Value.Bounds);
        if (crop.Width <= 0 || crop.Height <= 0) return null;

        image.Mutate(context => context.Crop(crop));

        var output = new MemoryStream();
        await image.SaveAsync(output, new JpegEncoder { Quality = 85 }, ct);
        output.Position = 0;
        return (output, "image/jpeg", true);
    }

    private static async Task<SpriteFrame?> FindSpriteFrameAsync(string vttPath, double seconds, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(vttPath, ct);
        SpriteFrame? previousFrame = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var timing = lines[i].Trim();
            var separatorIndex = timing.IndexOf("-->", StringComparison.Ordinal);
            if (separatorIndex < 0) continue;

            if (!TryParseVttTime(timing[..separatorIndex], out var startSeconds)) continue;
            if (!TryParseVttTime(timing[(separatorIndex + 3)..], out var endSeconds)) continue;

            var bounds = TryParseSpriteBounds(lines, i + 1);
            if (bounds == null) continue;

            var frame = new SpriteFrame(startSeconds, endSeconds, bounds.Value);
            if (seconds >= frame.StartSeconds && seconds < frame.EndSeconds)
                return frame;

            if (seconds < frame.StartSeconds)
                return previousFrame ?? frame;

            previousFrame = frame;
        }

        return previousFrame;
    }

    private static Rectangle? TryParseSpriteBounds(string[] lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.Contains("-->", StringComparison.Ordinal)) return null;

            var xywhIndex = line.IndexOf("#xywh=", StringComparison.OrdinalIgnoreCase);
            if (xywhIndex < 0) continue;

            var rectText = line[(xywhIndex + "#xywh=".Length)..];
            var parts = rectText.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 4) return null;

            return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                && width > 0
                && height > 0
                    ? new Rectangle(x, y, width, height)
                    : null;
        }

        return null;
    }

    private static bool TryParseVttTime(string value, out double seconds)
    {
        var token = value.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            seconds = 0;
            return false;
        }

        var parts = token.Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3)
        {
            seconds = 0;
            return false;
        }

        var hours = 0;
        var minutesIndex = 0;
        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
            {
                seconds = 0;
                return false;
            }

            minutesIndex = 1;
        }

        if (!int.TryParse(parts[minutesIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[minutesIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secondsPart))
        {
            seconds = 0;
            return false;
        }

        seconds = hours * 3600d + minutes * 60d + secondsPart;
        return true;
    }

    private readonly record struct SpriteFrame(double StartSeconds, double EndSeconds, Rectangle Bounds);
}
