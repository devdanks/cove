using Cove.Core.Events;
using Cove.Api.Services;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Core.Entities.Galleries.Zip;
using Cove.Data;
using Cove.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public class ScanServiceTests
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg" };
    private static readonly HashSet<string> GalleryExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase) { ".epub" };

    [Fact]
    public void NeedsVideoMetadataProbe_ReturnsTrueWhenDurationIsMissing()
    {
        var videoFile = new VideoFile
        {
            Width = 1920,
            Height = 1080,
            Duration = 0,
        };

        Assert.True(ScanService.NeedsVideoMetadataProbe(videoFile));
    }

    [Fact]
    public void NeedsVideoMetadataProbe_ReturnsTrueWhenDimensionsAreMissing()
    {
        var videoFile = new VideoFile
        {
            Width = 0,
            Height = 1080,
            Duration = 307.9,
        };

        Assert.True(ScanService.NeedsVideoMetadataProbe(videoFile));
    }

    [Fact]
    public void NeedsVideoMetadataProbe_ReturnsFalseWhenCoreVideoMetricsExist()
    {
        var videoFile = new VideoFile
        {
            Width = 1920,
            Height = 1080,
            Duration = 307.9,
        };

        Assert.False(ScanService.NeedsVideoMetadataProbe(videoFile));
    }

    [Fact]
    public void IsMediaTypeExcludedByScanTarget_ReturnsTrueForGalleryArchiveWhenImagesAreExcluded()
    {
        Assert.True(ScanService.IsMediaTypeExcludedByScanTarget(
            ".zip",
            excludeVideo: false,
            excludeImage: true,
            excludeAudio: false,
            excludeText: false,
            VideoExtensions,
            ImageExtensions,
            GalleryExtensions,
            AudioExtensions,
            TextExtensions));
    }

    [Fact]
    public void IsMediaTypeExcludedByScanTarget_ReturnsTrueForTextsWhenTextsAreExcluded()
    {
        Assert.True(ScanService.IsMediaTypeExcludedByScanTarget(
            ".epub",
            excludeVideo: false,
            excludeImage: false,
            excludeAudio: false,
            excludeText: true,
            VideoExtensions,
            ImageExtensions,
            GalleryExtensions,
            AudioExtensions,
            TextExtensions));
    }

    [Fact]
    public void IsMediaTypeExcludedByScanTarget_ReturnsFalseForAllowedMediaTypes()
    {
        Assert.False(ScanService.IsMediaTypeExcludedByScanTarget(
            ".zip",
            excludeVideo: false,
            excludeImage: false,
            excludeAudio: false,
            excludeText: false,
            VideoExtensions,
            ImageExtensions,
            GalleryExtensions,
            AudioExtensions,
            TextExtensions));
    }

    [Fact]
    public async Task StartScan_SkipsCaptionSyncForKnownUnchangedVideosDuringNormalScan()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cove-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var videoPath = Path.Combine(tempRoot, "known.mp4");
            await File.WriteAllBytesAsync(videoPath, [1, 2, 3, 4]);
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "known.en.vtt"), "WEBVTT");

            await using var environment = await CreateEnvironmentAsync(tempRoot, videoPath);

            environment.Service.StartScan();

            await using var verificationScope = environment.Services.CreateAsyncScope();
            var db = verificationScope.ServiceProvider.GetRequiredService<CoveContext>();
            var video = await db.VideoFiles.Include(item => item.Captions).SingleAsync();

            Assert.Empty(video.Captions);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartScan_RescanSyncsCaptionsForKnownVideos()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cove-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var videoPath = Path.Combine(tempRoot, "known.mp4");
            await File.WriteAllBytesAsync(videoPath, [1, 2, 3, 4]);
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "known.en.vtt"), "WEBVTT");

            await using var environment = await CreateEnvironmentAsync(tempRoot, videoPath);

            environment.Service.StartScan(new ScanOperationOptions { Rescan = true });

            await using var verificationScope = environment.Services.CreateAsyncScope();
            var db = verificationScope.ServiceProvider.GetRequiredService<CoveContext>();
            var video = await db.VideoFiles.Include(item => item.Captions).SingleAsync();
            var caption = Assert.Single(video.Captions);

            Assert.Equal("known.en.vtt", caption.Filename);
            Assert.Equal("en", caption.LanguageCode);
            Assert.Equal("vtt", caption.CaptionType);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<TestEnvironment> CreateEnvironmentAsync(string libraryRoot, string videoPath)
    {
        var services = new ServiceCollection();
        var dbOptions = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"scan-service-{Guid.NewGuid():N}")
            .Options;

        services.AddSingleton(dbOptions);
        services.AddScoped<CoveContext>(_ => new TestCoveContext(dbOptions));

        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
            await db.Database.EnsureCreatedAsync();

            var folder = new Folder
            {
                Path = NormalizeStoredFolderPath(libraryRoot),
                ModTime = Directory.GetLastWriteTimeUtc(libraryRoot),
            };

            var scene = new Scene
            {
                Title = "Known scene",
            };

            var fileInfo = new FileInfo(videoPath);
            scene.Files.Add(new VideoFile
            {
                Basename = Path.GetFileName(videoPath),
                ParentFolder = folder,
                Size = fileInfo.Length,
                ModTime = fileInfo.LastWriteTimeUtc,
                Format = "mp4",
                Width = 1920,
                Height = 1080,
                Duration = 42,
                VideoCodec = "h264",
                AudioCodec = "aac",
            });

            db.Scenes.Add(scene);
            await db.SaveChangesAsync();
        }
        var jobService = new ImmediateJobService();
        var config = new CoveConfiguration
        {
            CovePaths =
            [
                new CovePath
                {
                    Path = libraryRoot,
                }
            ],
        };

        var extensionManager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = libraryRoot,
            CoveVersion = "test",
        });

        var service = new ScanService(
            jobService,
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            new EventBus(),
            new NoOpFingerprintService(),
            new NoOpThumbnailService(),
            new TextExtractionService(),
            new ZipGalleryReader(new ZipFileReader()),
            extensionManager,
            NullLogger<ScanService>.Instance);

        return new TestEnvironment(provider, service);
    }

    private static string NormalizeStoredFolderPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var normalized = !string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalized.Replace('\\', '/');
    }

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options) : CoveContext(options);

    private sealed class ImmediateJobService : IJobService
    {
        private int _nextId;

        public string Enqueue(string type, string description, Func<Cove.Core.Interfaces.IJobProgress, CancellationToken, Task> work, bool exclusive = true)
        {
            work(new ImmediateJobProgress(), CancellationToken.None).GetAwaiter().GetResult();
            return $"job-{Interlocked.Increment(ref _nextId)}";
        }

        public bool Cancel(string jobId) => false;

        public bool ReorderQueued(string jobId, string? beforeJobId) => false;

        public JobInfo? GetJob(string jobId) => null;

        public IReadOnlyList<JobInfo> GetAllJobs() => [];

        public IReadOnlyList<JobInfo> GetJobHistory() => [];
    }

    private sealed class ImmediateJobProgress : Cove.Core.Interfaces.IJobProgress
    {
        public void Report(double progress, string? subTask = null)
        {
        }
    }

    private sealed class NoOpFingerprintService : IFingerprintService
    {
        public Task<string?> ComputeMd5Async(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<string?> ComputeImagePhashAsync(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<string?> ComputeVideoPhashAsync(string path, double duration, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<string?> ComputeAudioPhashAsync(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<string?> ComputeTextPhashAsync(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public string StartGenerateScenePhashes() => "noop";

        public string StartGenerateImagePhashes() => "noop";
    }

    private sealed class NoOpThumbnailService : IThumbnailService
    {
        public Task<string?> GetSceneThumbnailPathAsync(int sceneId, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<string?> GetImageFilePathAsync(int imageId, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetImageStreamAsync(int imageId, CancellationToken ct = default) => Task.FromResult<(Stream stream, string contentType, bool supportsRangeRequests)?>(null);

        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetImageThumbnailStreamAsync(int imageId, int maxDimension = 640, CancellationToken ct = default) => Task.FromResult<(Stream stream, string contentType, bool supportsRangeRequests)?>(null);

        public Task<(Stream stream, string contentType, bool supportsRangeRequests)?> GetBlobImageThumbnailStreamAsync(string blobId, int maxDimension = 640, CancellationToken ct = default) => Task.FromResult<(Stream stream, string contentType, bool supportsRangeRequests)?>(null);

        public Task DeleteSceneGeneratedFilesAsync(int sceneId, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteImageGeneratedFilesAsync(int imageId, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteBlobGeneratedFilesAsync(string blobId, CancellationToken ct = default) => Task.CompletedTask;

        public Task GenerateSceneThumbnailAsync(int sceneId, double? atSeconds = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task GenerateImageThumbnailAsync(int imageId, int maxDimension = 640, bool overwrite = false, CancellationToken ct = default) => Task.CompletedTask;

        public Task GenerateScenePreviewAsync(int sceneId, CancellationToken ct = default) => Task.CompletedTask;

        public Task GenerateSegmentAnimatedPreviewAsync(int sceneId, double startSec, double? endSec = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task GenerateSceneSpriteAsync(int sceneId, CancellationToken ct = default) => Task.CompletedTask;

        public string GetThumbnailPathForScene(int sceneId) => string.Empty;

        public string GetTimestampedThumbnailPath(int sceneId, double seconds) => string.Empty;

        public string GetSegmentAnimatedPreviewPath(int sceneId, double seconds) => string.Empty;

        public string GetPreviewPath(int sceneId) => string.Empty;

        public string GetSpritePath(int sceneId) => string.Empty;

        public string GetSpriteVttPath(int sceneId) => string.Empty;

        public string StartGenerateAllThumbnails() => "noop";
    }

    private sealed class TestEnvironment(ServiceProvider services, ScanService service) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;
        public ScanService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }
}
