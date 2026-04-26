using Cove.Core.DTOs;
using Microsoft.Extensions.Logging;

namespace Cove.Plugins;

public sealed class ExtensionPermissionManifest
{
    public List<string> Network { get; set; } = [];
    public List<string> ScraperRuntime { get; set; } = [];
    public List<string> DownloaderRuntime { get; set; } = [];
}

[Flags]
public enum ScraperCapabilities
{
    None = 0,
    ByUrl = 1 << 0,
    ByName = 1 << 1,
    ByFragment = 1 << 2,
    ByQueryFragment = 1 << 3,
}

public enum ScraperRiskLevel
{
    None,
    NetworkOnly,
    RemoteCode,
}

public enum ScraperEntity
{
    Scene,
    Performer,
    Gallery,
    Image,
    Group,
    Audio,
}

public sealed record ScraperPermissions(
    IReadOnlyList<string>? AllowNetworkHosts = null,
    bool AllowJavaScript = false,
    bool AllowCdp = false);

public sealed record ScraperDescriptor(
    string Id,
    string Name,
    ScraperEntity Entity,
    ScraperCapabilities Capabilities,
    IReadOnlyList<string> SupportedUrls,
    ScraperRiskLevel Risk = ScraperRiskLevel.NetworkOnly);

public sealed record ScraperRequest<TInput>(
    string ScraperId,
    TInput Input,
    ScraperPermissions Permissions);

public interface IScraperHost
{
    IHttpClientFactory HttpClients { get; }
    ILogger CreateLogger(string categoryName);
    Task<SceneScrapeInput?> GetSceneAsync(int sceneId, CancellationToken ct = default);
    Task<PerformerScrapeInput?> GetPerformerAsync(int performerId, CancellationToken ct = default);
    Task<GalleryScrapeInput?> GetGalleryAsync(int galleryId, CancellationToken ct = default);
    Task<ImageScrapeInput?> GetImageAsync(int imageId, CancellationToken ct = default);
    Task<GroupScrapeInput?> GetGroupAsync(int groupId, CancellationToken ct = default);
}

public interface IScraperProvider : IExtension
{
    IReadOnlyList<ScraperDescriptor> GetScrapers();

    Task<ScrapedSceneDto?> ScrapeSceneAsync(ScraperRequest<SceneScrapeInput> request, CancellationToken ct)
        => Task.FromResult<ScrapedSceneDto?>(null);

    Task<IReadOnlyList<ScrapedSceneDto>> SearchScenesAsync(ScraperRequest<string> request, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ScrapedSceneDto>>([]);

    Task<ScrapedPerformerDto?> ScrapePerformerAsync(ScraperRequest<PerformerScrapeInput> request, CancellationToken ct)
        => Task.FromResult<ScrapedPerformerDto?>(null);

    Task<IReadOnlyList<ScrapedPerformerDto>> SearchPerformersAsync(ScraperRequest<string> request, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ScrapedPerformerDto>>([]);

    Task<ScrapedGalleryDto?> ScrapeGalleryAsync(ScraperRequest<GalleryScrapeInput> request, CancellationToken ct)
        => Task.FromResult<ScrapedGalleryDto?>(null);

    Task<ScrapedImageDto?> ScrapeImageAsync(ScraperRequest<ImageScrapeInput> request, CancellationToken ct)
        => Task.FromResult<ScrapedImageDto?>(null);

    Task<ScrapedGroupDto?> ScrapeGroupAsync(ScraperRequest<GroupScrapeInput> request, CancellationToken ct)
        => Task.FromResult<ScrapedGroupDto?>(null);
}

[Flags]
public enum DownloaderCapabilities
{
    None = 0,
    ResumeSupported = 1 << 0,
    RangeRequests = 1 << 1,
    MultiQuality = 1 << 2,
    InlineMetadata = 1 << 3,
}

public enum DownloaderEntity
{
    Scene,
    Image,
    Gallery,
    Audio,
}

public sealed record DownloaderPermissions(IReadOnlyList<string>? AllowNetworkHosts = null);

public sealed record DownloaderDescriptor(
    string Id,
    string Name,
    DownloaderEntity SupportedEntity,
    IReadOnlyList<string> SupportedUrlPatterns,
    DownloaderCapabilities Capabilities = DownloaderCapabilities.None);

public sealed record DownloaderQualityOption(string Id, string Label, string? Description = null);

public sealed record DownloaderUrlMatch(
    string DownloaderId,
    string NormalizedUrl,
    IReadOnlyList<DownloaderQualityOption>? QualityOptions = null,
    string? Label = null);

public sealed record DownloaderRequest(
    string DownloaderId,
    string Url,
    DownloaderEntity Entity,
    DownloaderPermissions Permissions,
    string? QualityId = null);

public sealed record DownloaderResult(
    string LocalPath,
    string? OriginalFilename = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    ScrapedSceneDto? InlineSceneMetadata = null,
    ScrapedGalleryDto? InlineGalleryMetadata = null,
    ScrapedImageDto? InlineImageMetadata = null);

public interface IDownloaderHost
{
    string TempDirectory { get; }
    IHttpClientFactory HttpClients { get; }
    ILogger CreateLogger(string categoryName);
    void ReportProgress(double progress, string? message = null);
}

public interface IDownloaderProvider : IExtension
{
    IReadOnlyList<DownloaderDescriptor> GetDownloaders();

    Task<DownloaderUrlMatch?> MatchAsync(string url, CancellationToken ct)
        => Task.FromResult<DownloaderUrlMatch?>(null);

    Task<DownloaderResult?> DownloadAsync(DownloaderRequest request, IDownloaderHost host, CancellationToken ct)
        => Task.FromResult<DownloaderResult?>(null);
}

public enum AutoTagContentType
{
    Scene,
    Image,
    Gallery,
}

public enum AutoTagEntityKind
{
    Performer,
    Studio,
    Tag,
}

public sealed record AutoTagContentCandidate(
    AutoTagContentType ContentType,
    int ContentId,
    string SearchText,
    string? DisplayName = null);

public sealed record AutoTagEntityCandidate(
    AutoTagEntityKind EntityKind,
    int EntityId,
    string Name,
    IReadOnlyList<string>? Aliases = null);

public sealed record AutoTagMatchRequest(
    AutoTagEntityCandidate Entity,
    AutoTagContentCandidate Content,
    IReadOnlyDictionary<string, string>? Options = null);

public sealed record AutoTagMatch(
    AutoTagContentType ContentType,
    int ContentId,
    AutoTagEntityKind EntityKind,
    int EntityId,
    string MatcherId,
    double Score,
    string EvidenceSnippet);

public interface IAutoTagMatcher
{
    string Id { get; }
    string Name { get; }
    AutoTagEntityKind[] SupportedEntities { get; }

    Task<IReadOnlyList<AutoTagMatch>> MatchAsync(AutoTagMatchRequest request, CancellationToken ct = default);
}

public interface IAutoTagMatcherExtension : IExtension
{
    IReadOnlyList<IAutoTagMatcher> GetMatchers();
}