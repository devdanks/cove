using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Cove.Plugins;

namespace Cove.Api.Extensions;

public sealed class DirectFileDownloaderExtension : IDownloaderProvider
{
    private const string SceneDownloaderId = "builtin.direct-file/scene";
    private const string ImageDownloaderId = "builtin.direct-file/image";
    private const string AudioDownloaderId = "builtin.direct-file/audio";

    private static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv", ".ts", ".mpeg", ".mpg"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif"];
    private static readonly string[] AudioExtensions = [".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus"];

    private static readonly DownloaderDescriptor SceneDownloader = new(
        SceneDownloaderId,
        "Direct Video File",
        DownloaderEntity.Scene,
        VideoExtensions.Select(extension => $"*{extension}*").ToList(),
        DownloaderCapabilities.None);

    private static readonly DownloaderDescriptor ImageDownloader = new(
        ImageDownloaderId,
        "Direct Image File",
        DownloaderEntity.Image,
        ImageExtensions.Select(extension => $"*{extension}*").ToList(),
        DownloaderCapabilities.None);

    private static readonly DownloaderDescriptor AudioDownloader = new(
        AudioDownloaderId,
        "Direct Audio File",
        DownloaderEntity.Audio,
        AudioExtensions.Select(extension => $"*{extension}*").ToList(),
        DownloaderCapabilities.None);

    private static readonly IReadOnlyDictionary<string, DownloaderDescriptor> Downloaders = new Dictionary<string, DownloaderDescriptor>(StringComparer.OrdinalIgnoreCase)
    {
        [SceneDownloader.Id] = SceneDownloader,
        [ImageDownloader.Id] = ImageDownloader,
        [AudioDownloader.Id] = AudioDownloader,
    };

    public string Id => "builtin.direct-file";
    public string Name => "Direct File Downloader";
    public string Version => "1.0.0";
    public string? Description => "Downloads direct image, video, and audio file URLs without a site-specific downloader.";
    public string? Author => "Cove";
    public string? Url => null;
    public string? IconUrl => null;
    public IReadOnlyList<string> Categories => [ExtensionCategories.Downloader];

    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
    }

    public IReadOnlyList<DownloaderDescriptor> GetDownloaders() => [SceneDownloader, ImageDownloader, AudioDownloader];

    public Task<DownloaderUrlMatch?> MatchAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
            return Task.FromResult<DownloaderUrlMatch?>(null);

        if (!TryResolveDownloader(uri.AbsolutePath, out var descriptor))
            return Task.FromResult<DownloaderUrlMatch?>(null);

        return Task.FromResult<DownloaderUrlMatch?>(new DownloaderUrlMatch(
            descriptor.Id,
            NormalizeUrl(uri),
            Label: BuildLabel(uri)));
    }

    public async Task<DownloaderResult?> DownloadAsync(DownloaderRequest request, IDownloaderHost host, CancellationToken ct)
    {
        if (!Downloaders.TryGetValue(request.DownloaderId, out var descriptor))
            return null;

        if (request.Entity != descriptor.SupportedEntity)
            throw new InvalidOperationException($"Downloader {request.DownloaderId} only supports {descriptor.SupportedEntity} downloads.");

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || !TryResolveDownloader(uri.AbsolutePath, out var resolvedDescriptor))
            throw new InvalidOperationException("This downloader only supports direct media file URLs.");

        if (!string.Equals(resolvedDescriptor.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The URL does not match the expected media type for downloader {request.DownloaderId}.");

        host.ReportProgress(0.05d, "Fetching direct media file...");

        var client = host.HttpClients.CreateClient(string.Empty);
        using var response = await client.GetAsync(request.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var fileName = ResolveFileName(response.Content.Headers.ContentDisposition, uri, descriptor.SupportedEntity, response.Content.Headers.ContentType?.MediaType);
        var destinationPath = Path.Combine(host.TempDirectory, SanitizeFileName(fileName));

        await using (var output = File.Create(destinationPath))
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            var contentLength = response.Content.Headers.ContentLength;

            while (true)
            {
                var read = await input.ReadAsync(buffer, ct);
                if (read == 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;

                if (contentLength is long knownLength && knownLength > 0)
                {
                    var progress = Math.Clamp((double)totalRead / knownLength, 0d, 1d);
                    host.ReportProgress(0.1d + (progress * 0.85d), $"Downloading {fileName}...");
                }
            }
        }

        host.ReportProgress(0.98d, "Direct file download completed.");

        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);

        return new DownloaderResult(Path.GetFileName(destinationPath), fileName, Headers: headers);
    }

    private static bool TryResolveDownloader(string absolutePath, out DownloaderDescriptor descriptor)
    {
        var extension = Path.GetExtension(absolutePath)?.ToLowerInvariant();
        if (extension is null)
        {
            descriptor = default!;
            return false;
        }

        if (VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = SceneDownloader;
            return true;
        }

        if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = ImageDownloader;
            return true;
        }

        if (AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = AudioDownloader;
            return true;
        }

        descriptor = default!;
        return false;
    }

    private static string NormalizeUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };
        return builder.Uri.ToString();
    }

    private static string BuildLabel(Uri uri)
    {
        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName))
            return Uri.UnescapeDataString(fileName);

        return uri.Host;
    }

    private static string ResolveFileName(ContentDispositionHeaderValue? contentDisposition, Uri uri, DownloaderEntity entity, string? mediaType)
    {
        var candidate = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate.Trim('"').Trim();

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName))
            return Uri.UnescapeDataString(fileName);

        var inferredExtension = InferExtension(mediaType) ?? GetDefaultExtension(entity);
        return $"download{inferredExtension}";
    }

    private static string? InferExtension(string? mediaType)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/flac" => ".flac",
            "audio/wav" => ".wav",
            _ => null,
        };
    }

    private static string GetDefaultExtension(DownloaderEntity entity)
    {
        return entity switch
        {
            DownloaderEntity.Scene => ".mp4",
            DownloaderEntity.Image => ".jpg",
            DownloaderEntity.Audio => ".mp3",
            _ => ".bin",
        };
    }

    private static string SanitizeFileName(string value)
    {
        var fileName = string.IsNullOrWhiteSpace(value) ? "download.bin" : value;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return fileName;
    }
}