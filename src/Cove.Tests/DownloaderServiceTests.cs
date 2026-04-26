using Cove.Api.Services;
using Cove.Core.Entities;
using Cove.Core.DTOs;
using Cove.Core.Interfaces;
using Cove.Plugins;
using Cove.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests;

public class DownloaderServiceTests
{
    [Fact]
    public async Task GetDownloadersAndMatchUrl_ReturnRegisteredProviderData()
    {
        var service = CreateService(out _);

        var downloaders = service.GetDownloaders();
        var matches = await service.MatchUrlAsync("https://example.com/watch/123", CancellationToken.None);

        var downloader = Assert.Single(downloaders);
        Assert.Equal("tests.fake-downloader/example", downloader.Id);
        Assert.Equal("Scene", downloader.SupportedEntity);
        Assert.Contains("MultiQuality", downloader.Capabilities);

        var match = Assert.Single(matches);
        Assert.Equal(downloader.Id, match.DownloaderId);
        Assert.Equal("Example Download", match.DownloaderName);
        Assert.Equal("https://example.com/watch/123", match.NormalizedUrl);
        Assert.Single(match.QualityOptions);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsAbsoluteLocalPathFromProviderOutput()
    {
        var service = CreateService(out _);

        var result = await service.DownloadAsync(
            new DownloaderRequest(
                "tests.fake-downloader/example",
                "https://example.com/watch/456",
                DownloaderEntity.Scene,
                new DownloaderPermissions(["example.com"]),
                "hd"),
            progress: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result!.LocalPath));
        Assert.True(File.Exists(result.LocalPath));
        Assert.Equal("downloaded-scene.mp4", Path.GetFileName(result.LocalPath));
    }

    [Fact]
    public async Task DownloadAndIngestAsync_MovesFileIntoLibraryAndDelegatesSceneImport()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);

        var scanService = new FakeScanService();
        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths =
                [
                    new CovePath { Path = libraryRoot }
                ]
            },
            scanService);

        try
        {
            var (result, importedSceneId) = await service.DownloadAndIngestAsync(
                new DownloaderRequest(
                    "tests.fake-downloader/example",
                    "https://example.com/watch/789",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["example.com"]),
                    "hd"),
                entityId: 42,
                progress: null,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(42, importedSceneId);
            Assert.NotNull(scanService.ImportedPath);
            Assert.True(File.Exists(result!.LocalPath));
            Assert.Equal(result.LocalPath, scanService.ImportedPath);
            Assert.Equal(42, scanService.SceneId);
            Assert.StartsWith(
                Path.Combine(Path.GetFullPath(libraryRoot), "_downloads", "scenes"),
                result.LocalPath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestAsync_AutoAppliesInlineSceneMetadataWhenRequested()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);

        var scanService = new FakeScanService();
        var metadataApplyService = new FakeSceneMetadataApplyService();
        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths =
                [
                    new CovePath { Path = libraryRoot }
                ]
            },
            scanService,
            metadataApplyService);

        try
        {
            var (_, importedSceneId) = await service.DownloadAndIngestAsync(
                new DownloaderRequest(
                    "tests.fake-downloader/example",
                    "https://example.com/watch/999",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["example.com"]),
                    "hd"),
                entityId: 42,
                progress: null,
                CancellationToken.None,
                autoApplyMetadata: true);

            Assert.Equal(42, importedSceneId);
            Assert.Equal(42, metadataApplyService.SceneId);
            Assert.NotNull(metadataApplyService.Metadata);
            Assert.Equal("Downloaded Scene", metadataApplyService.Metadata!.Title);
            Assert.Equal("https://example.com/watch/999", Assert.Single(metadataApplyService.Metadata.Urls));
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestAsync_UsesDownloaderOverridePathWhenConfigured()
    {
        var defaultRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"), "default");
        var overrideRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"), "override");
        Directory.CreateDirectory(defaultRoot);
        Directory.CreateDirectory(overrideRoot);

        var scanService = new FakeScanService();
        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths =
                [
                    new CovePath { Path = defaultRoot }
                ],
                DownloaderPathOverrides =
                [
                    new DownloaderPathOverride
                    {
                        DownloaderId = "tests.fake-downloader/example",
                        Path = overrideRoot,
                    }
                ]
            },
            scanService);

        try
        {
            var (result, _) = await service.DownloadAndIngestAsync(
                new DownloaderRequest(
                    "tests.fake-downloader/example",
                    "https://example.com/watch/override",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["example.com"]),
                    "hd"),
                entityId: null,
                progress: null,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.StartsWith(
                Path.GetFullPath(overrideRoot),
                result!.LocalPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("_downloads", result.LocalPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(Path.GetDirectoryName(defaultRoot)!))
                Directory.Delete(Path.GetDirectoryName(defaultRoot)!, recursive: true);
            if (Directory.Exists(Path.GetDirectoryName(overrideRoot)!))
                Directory.Delete(Path.GetDirectoryName(overrideRoot)!, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestAsync_ThrowsWhenTargetEntityAlreadyHasFiles()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);
        Scene? existingScene = null;
        var scanService = new FakeScanService();

        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }]
            },
            scanService,
            seedDatabase: db =>
            {
                existingScene = new Scene
                {
                    Title = "Existing Scene",
                };

                db.Scenes.Add(existingScene);
                db.Set<SceneUrl>().Add(new SceneUrl { Scene = existingScene, Url = "https://example.com/watch/existing" });
                db.VideoFiles.Add(new VideoFile
                {
                    Scene = existingScene,
                    Basename = "existing.mp4",
                    ParentFolder = new Folder { Path = "C:\\library" },
                });
            });

        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndIngestAsync(
                new DownloaderRequest(
                    "tests.fake-downloader/example",
                    "https://example.com/watch/new-url",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["example.com"]),
                    "hd"),
                entityId: existingScene!.Id,
                progress: null,
                CancellationToken.None));

            Assert.Contains("already has downloaded files", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestAsync_ThrowsWhenUrlAlreadyExistsOnDownloadedEntity()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);
        Scene? existingScene = null;
        var scanService = new FakeScanService();

        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }]
            },
            scanService,
            seedDatabase: db =>
            {
                existingScene = new Scene
                {
                    Title = "Downloaded Scene",
                };

                db.Scenes.Add(existingScene);
                db.Set<SceneUrl>().Add(new SceneUrl { Scene = existingScene, Url = "https://example.com/watch/existing" });
                db.VideoFiles.Add(new VideoFile
                {
                    Scene = existingScene,
                    Basename = "existing.mp4",
                    ParentFolder = new Folder { Path = "C:\\library" },
                });
            });

        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndIngestAsync(
                new DownloaderRequest(
                    "tests.fake-downloader/example",
                    "https://example.com/watch/existing",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["example.com"]),
                    "hd"),
                entityId: null,
                progress: null,
                CancellationToken.None));

            Assert.Contains("already downloaded", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestAsync_AllowsDuplicateWhenRequested()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);

        var scanService = new FakeScanService();
        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }]
            },
            scanService,
            seedDatabase: db =>
            {
                var existingScene = new Scene
                {
                    Title = "Downloaded Scene",
                };

                db.Scenes.Add(existingScene);
                db.Set<SceneUrl>().Add(new SceneUrl { Scene = existingScene, Url = "https://example.com/watch/existing" });
                db.VideoFiles.Add(new VideoFile
                {
                    Scene = existingScene,
                    Basename = "existing.mp4",
                    ParentFolder = new Folder { Path = "C:\\library" },
                });
            });

        try
        {
            var (result, importedSceneId) = await service.DownloadAndIngestAsync(
                new DownloaderRequest(
                    "tests.fake-downloader/example",
                    "https://example.com/watch/existing",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["example.com"]),
                    "hd"),
                entityId: null,
                progress: null,
                CancellationToken.None,
                allowDuplicateDownload: true);

            Assert.NotNull(result);
            Assert.Equal(1, importedSceneId);
            Assert.NotNull(scanService.ImportedPath);
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestBatchAsync_QueuesFollowUpGenerateScan()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);

        var scanService = new FakeScanService();
        var metadataApplyService = new FakeSceneMetadataApplyService();
        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }],
                MaxConcurrentDownloads = 2,
            },
            scanService,
            metadataApplyService);

        try
        {
            var summary = await service.DownloadAndIngestBatchAsync(
                [
                    new DownloaderBatchItemDto
                    {
                        DownloaderId = "tests.fake-downloader/example",
                        Url = "https://example.com/watch/batch-one",
                        Entity = "Scene",
                        Title = "Batch One",
                        CreateEntityIfMissing = true,
                        QualityId = "hd",
                        Label = "Batch One",
                    },
                    new DownloaderBatchItemDto
                    {
                        DownloaderId = "tests.fake-downloader/example",
                        Url = "https://example.com/watch/batch-two",
                        Entity = "Scene",
                        Title = "Batch Two",
                        CreateEntityIfMissing = true,
                        QualityId = "hd",
                        Label = "Batch Two",
                    }
                ],
                new DownloaderBatchFollowUpDto
                {
                    ScrapeScenes = true,
                    Generate = new GenerateOptionsDto
                    {
                        Thumbnails = true,
                        Previews = true,
                    },
                },
                progress: null,
                CancellationToken.None);

            Assert.Equal(2, summary.TotalCount);
            Assert.Equal(2, summary.SucceededCount);
            Assert.Equal(0, summary.SkippedCount);
            Assert.Equal(0, summary.FailedCount);
            Assert.Equal("job-1", summary.FollowUpJobId);
            Assert.NotNull(scanService.StartedScanOptions);
            Assert.True(scanService.StartedScanOptions!.GenerateCovers);
            Assert.True(scanService.StartedScanOptions.GeneratePreviews);
            Assert.NotEmpty(scanService.StartedScanOptions.Paths ?? []);
            Assert.Equal(1, scanService.ScanStartCount);
            Assert.NotNull(metadataApplyService.Metadata);
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestBatchAsync_CreatesPlaceholderAndSkipsDuplicateWithoutCreatingAnotherEntity()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);

        var scanService = new FakeScanService();
        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }],
                MaxConcurrentDownloads = 2,
            },
            scanService,
            seedDatabase: db =>
            {
                var existingScene = new Scene
                {
                    Title = "Already Downloaded",
                };

                db.Scenes.Add(existingScene);
                db.Set<SceneUrl>().Add(new SceneUrl { Scene = existingScene, Url = "https://example.com/watch/existing" });
                db.VideoFiles.Add(new VideoFile
                {
                    Scene = existingScene,
                    Basename = "existing.mp4",
                    ParentFolder = new Folder { Path = "C:\\library" },
                });
            });

        try
        {
            var summary = await service.DownloadAndIngestBatchAsync(
                [
                    new DownloaderBatchItemDto
                    {
                        Url = "https://example.com/watch/new-import",
                        Entity = "Scene",
                        Title = "Imported From Batch",
                        CreateEntityIfMissing = true,
                    },
                    new DownloaderBatchItemDto
                    {
                        Url = "https://example.com/watch/existing",
                        Entity = "Scene",
                        Title = "Should Not Exist",
                        CreateEntityIfMissing = true,
                    },
                ],
                new DownloaderBatchFollowUpDto(),
                progress: null,
                CancellationToken.None);

            Assert.Equal(2, summary.TotalCount);
            Assert.Equal(1, summary.SucceededCount);
            Assert.Equal(1, summary.SkippedCount);
            Assert.Equal(0, summary.FailedCount);
            Assert.Contains(summary.Issues, issue => issue.Contains("already downloaded", StringComparison.OrdinalIgnoreCase));

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
            var importedScene = await db.Scenes.SingleAsync(scene => scene.Title == "Imported From Batch");
            Assert.NotEqual(0, importedScene.Id);
            Assert.Equal(importedScene.Id, scanService.SceneId);
            Assert.True(await db.Set<SceneUrl>().AnyAsync(item => item.SceneId == importedScene.Id && item.Url == "https://example.com/watch/new-import"));
            Assert.False(await db.Scenes.AnyAsync(scene => scene.Title == "Should Not Exist"));
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    private static DownloaderService CreateService(
        out ServiceProvider services,
        CoveConfiguration? config = null,
        IScanService? scanService = null,
        ISceneMetadataApplyService? sceneMetadataApplyService = null,
        Action<CoveContext>? seedDatabase = null)
    {
        var serviceCollection = new ServiceCollection();
        var databaseName = $"cove-downloader-tests-{Guid.NewGuid():N}";
        serviceCollection.AddHttpClient();
        serviceCollection.AddDbContext<DownloaderTestContext>(options => options.UseInMemoryDatabase(databaseName));
        serviceCollection.AddScoped<CoveContext>(provider => provider.GetRequiredService<DownloaderTestContext>());
        if (scanService != null)
            serviceCollection.AddSingleton(scanService);
        if (sceneMetadataApplyService != null)
            serviceCollection.AddSingleton(sceneMetadataApplyService);
        services = serviceCollection.BuildServiceProvider();

        if (seedDatabase != null)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
            seedDatabase(db);
            db.SaveChanges();
        }

        var extensionManager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = Path.GetTempPath(),
            CoveVersion = "test",
        });
        extensionManager.Register(new FakeDownloaderProvider());

        return new DownloaderService(
            extensionManager,
            services.GetRequiredService<IHttpClientFactory>(),
            NullLoggerFactory.Instance,
            config ?? new CoveConfiguration(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DownloaderService>.Instance);
    }

    private sealed class FakeScanService : IScanService
    {
        public string? ImportedPath { get; private set; }
        public int? SceneId { get; private set; }
        public int? ImageId { get; private set; }
        public int? GalleryId { get; private set; }
        public int ScanStartCount { get; private set; }
        public ScanOperationOptions? StartedScanOptions { get; private set; }

        public string StartScan(ScanOperationOptions? options = null)
        {
            ScanStartCount += 1;
            StartedScanOptions = options;
            return $"job-{ScanStartCount}";
        }

        public Task<int> ImportDownloadedSceneAsync(string path, int? sceneId, CancellationToken ct = default)
        {
            ImportedPath = path;
            SceneId = sceneId;
            return Task.FromResult(sceneId ?? 1);
        }

        public Task<int> ImportDownloadedImageAsync(string path, int? imageId, CancellationToken ct = default)
        {
            ImportedPath = path;
            ImageId = imageId;
            return Task.FromResult(imageId ?? 1);
        }

        public Task<int> ImportDownloadedGalleryAsync(string path, int? galleryId, CancellationToken ct = default)
        {
            ImportedPath = path;
            GalleryId = galleryId;
            return Task.FromResult(galleryId ?? 1);
        }
    }

    private sealed class FakeDownloaderProvider : IDownloaderProvider
    {
        public string Id => "tests.fake-downloader";
        public string Name => "Fake Downloader";
        public string Version => "1.0.0";
        public string? Description => null;
        public string? Author => null;
        public string? Url => null;
        public string? IconUrl => null;
        public IReadOnlyList<string> Categories => [ExtensionCategories.Downloader];

        public void ConfigureServices(IServiceCollection services, ExtensionContext context)
        {
        }

        public IReadOnlyList<DownloaderDescriptor> GetDownloaders()
        {
            return
            [
                new DownloaderDescriptor(
                    "tests.fake-downloader/example",
                    "Example Download",
                    DownloaderEntity.Scene,
                    ["example.com"],
                    DownloaderCapabilities.MultiQuality | DownloaderCapabilities.InlineMetadata)
            ];
        }

        public Task<DownloaderUrlMatch?> MatchAsync(string url, CancellationToken ct)
        {
            if (!url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<DownloaderUrlMatch?>(null);

            return Task.FromResult<DownloaderUrlMatch?>(new DownloaderUrlMatch(
                "tests.fake-downloader/example",
                url,
                [new DownloaderQualityOption("hd", "HD")],
                "Example scene"));
        }

        public async Task<DownloaderResult?> DownloadAsync(DownloaderRequest request, IDownloaderHost host, CancellationToken ct)
        {
            var filePath = Path.Combine(host.TempDirectory, "downloaded-scene.mp4");
            await File.WriteAllTextAsync(filePath, "fake media payload", ct);

            host.ReportProgress(1d, "Downloaded fake file");
            return new DownloaderResult(
                "downloaded-scene.mp4",
                "downloaded-scene.mp4",
                InlineSceneMetadata: new ScrapedSceneDto
                {
                    Title = "Downloaded Scene",
                    Urls = [request.Url],
                });
        }
    }

    private sealed class DownloaderTestContext : CoveContext
    {
        public DownloaderTestContext(DbContextOptions<DownloaderTestContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Scene>().Ignore(item => item.CustomFields);
            modelBuilder.Entity<Image>().Ignore(item => item.CustomFields);
            modelBuilder.Entity<Gallery>().Ignore(item => item.CustomFields);
            modelBuilder.Entity<Group>().Ignore(item => item.CustomFields);
            modelBuilder.Entity<Performer>().Ignore(item => item.CustomFields);
            modelBuilder.Entity<Studio>().Ignore(item => item.CustomFields);
            modelBuilder.Entity<Tag>().Ignore(item => item.CustomFields);
        }
    }

    private sealed class FakeSceneMetadataApplyService : ISceneMetadataApplyService
    {
        public int? SceneId { get; private set; }
        public ScrapedSceneDto? Metadata { get; private set; }

        public Task<bool> ApplyAsync(int sceneId, ScrapedSceneDto metadata, CancellationToken ct = default)
        {
            SceneId = sceneId;
            Metadata = metadata;
            return Task.FromResult(true);
        }
    }
}