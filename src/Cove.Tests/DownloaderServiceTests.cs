using System.Collections.Concurrent;
using System.Text.Json;
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
    public void ConvertScrapeResultToTextMetadata_ReadsJsonElementRelations()
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>("""
            {
              "title": "The Bracelet",
              "tagNames": ["Taboo/Incest", "mind control"],
              "performerNames": ["nolimits"]
            }
            """)!;

        var metadata = DownloaderService.ConvertScrapeResultToTextMetadata(result, "https://www.literotica.com/s/the-bracelet");

        Assert.NotNull(metadata);
        Assert.Equal("The Bracelet", metadata.Title);
        Assert.Equal(["Taboo/Incest", "mind control"], metadata.TagNames);
        Assert.Equal(["nolimits"], metadata.PerformerNames);
    }

    [Fact]
    public void ConvertScrapeResultToAudioMetadata_ReadsJsonElementRelations()
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>("""
            {
              "title": "Example Audio",
              "tagNames": ["F4M", { "name": "Bimbo" }],
              "performerNames": ["fieldsoflupine"]
            }
            """)!;

        var metadata = DownloaderService.ConvertScrapeResultToAudioMetadata(result, "https://soundgasm.net/u/example/audio");

        Assert.NotNull(metadata);
        Assert.Equal("Example Audio", metadata.Title);
        Assert.Equal(["F4M", "Bimbo"], metadata.TagNames);
        Assert.Equal(["fieldsoflupine"], metadata.PerformerNames);
    }

    [Fact]
    public async Task GetDownloadersAndMatchUrl_ReturnRegisteredProviderData()
    {
        var service = CreateService(out _);

        var downloaders = service.GetDownloaders();
        var matches = await service.MatchUrlAsync("https://example.com/watch/123", CancellationToken.None);

        Assert.Equal(2, downloaders.Count);
        var downloader = Assert.Single(downloaders, item => item.Id == "tests.fake-downloader/example");
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
    public async Task MatchUrlAsync_ReturnsAllProviderMatchesForUrl()
    {
        var service = CreateService(out _);

        var matches = await service.MatchUrlAsync("https://multi.example.com/watch/123", CancellationToken.None);

        Assert.Collection(
            matches,
            match =>
            {
                Assert.Equal("tests.fake-downloader/audio", match.DownloaderId);
                Assert.Equal("Audio Download", match.DownloaderName);
                Assert.Equal("Audio", match.SupportedEntity);
            },
            match =>
            {
                Assert.Equal("tests.fake-downloader/example", match.DownloaderId);
                Assert.Equal("Example Download", match.DownloaderName);
                Assert.Equal("Scene", match.SupportedEntity);
            });
    }

    [Fact]
    public async Task MatchUrlAsync_DivertsLinkedUrlsThroughOtherDownloaders()
    {
        const string sourceUrl = "https://forum.example.net/topics/abc/example-post";
        var service = CreateService(out _, includeForumProvider: true);

        var matches = await service.MatchUrlAsync(sourceUrl, CancellationToken.None);

        Assert.Collection(
            matches,
            match =>
            {
                Assert.Equal("tests.fake-downloader/audio", match.DownloaderId);
                Assert.Equal("Audio", match.SupportedEntity);
                Assert.Equal("https://audio.example.net/track/one", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.Equal("Forum post title (1)", match.Label);
            },
            match =>
            {
                Assert.Equal("tests.fake-downloader/audio", match.DownloaderId);
                Assert.Equal("Audio", match.SupportedEntity);
                Assert.Equal("https://audio.example.net/track/two", match.NormalizedUrl);
                Assert.Equal(sourceUrl, match.SourceUrl);
                Assert.Equal("Forum post title (2)", match.Label);
            });
    }

    [Fact]
    public async Task PreflightBatchAsync_ExpandsSourceUrlMatchesIntoCanonicalUrls()
    {
        const string sourceUrl = "https://forum.example.net/topics/abc/example-post";
        var service = CreateService(out var services, includeForumProvider: true);

        try
        {
            var preflight = await service.PreflightBatchAsync(
                [
                    new DownloaderBatchItemDto
                    {
                        Url = sourceUrl,
                        Entity = "Audio",
                        Title = "Forum Metadata Title",
                        CreateEntityIfMissing = true,
                    }
                ],
                new DownloaderBatchFollowUpDto(),
                CancellationToken.None);

            Assert.Empty(preflight.Issues);
            Assert.Collection(
                preflight.ItemsToQueue,
                item =>
                {
                    Assert.Equal("tests.fake-downloader/audio", item.DownloaderId);
                    Assert.Equal("https://audio.example.net/track/one", item.Url);
                    Assert.Equal(sourceUrl, item.SourceUrl);
                    Assert.Equal("Forum post title (1)", item.Label);
                    Assert.Equal("Forum Metadata Title", item.Title);
                },
                item =>
                {
                    Assert.Equal("tests.fake-downloader/audio", item.DownloaderId);
                    Assert.Equal("https://audio.example.net/track/two", item.Url);
                    Assert.Equal(sourceUrl, item.SourceUrl);
                    Assert.Equal("Forum post title (2)", item.Label);
                    Assert.Equal("Forum Metadata Title", item.Title);
                });
        }
        finally
        {
            await services.DisposeAsync();
        }
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
            scanService,
            includeForumProvider: true);

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
    public void ConvertScrapeResultToSceneMetadata_ReturnsMetadataWhenOnlyUrlsArePresent()
    {
        var metadata = DownloaderService.ConvertScrapeResultToSceneMetadata(
            new Dictionary<string, object>
            {
                ["urls"] = new[]
                {
                    "https://audio.example.net/track/example",
                    "https://stream.example.net/tracks/example"
                }
            },
            "https://forum.example.net/topics/abc/post");

        Assert.NotNull(metadata);
        Assert.Equal(
            [
                "https://audio.example.net/track/example",
                "https://stream.example.net/tracks/example",
                "https://forum.example.net/topics/abc/post"
            ],
            metadata!.Urls);
    }

    [Fact]
    public async Task ApplyAudioMetadataAsync_MergesUrlsAndRelationsIntoAudio()
    {
        var service = CreateService(
            out var services,
            seedDatabase: db =>
            {
                db.Audios.Add(new Audio
                {
                    Title = "Before",
                    Urls = [new AudioUrl { Url = "https://forum.example.net/topics/abc/post" }],
                });
            });

        try
        {
            int audioId;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
                audioId = await db.Audios.Select(item => item.Id).SingleAsync();
            }

            var applied = await service.ApplyAudioMetadataAsync(
                audioId,
                new DownloaderService.ScrapedAudioMetadata
                {
                    Title = "Merged Audio Title",
                    Details = "Merged details from source and mirror",
                    Date = "2024-02-03",
                    StudioName = "Forum",
                    Urls =
                    [
                        "https://audio.example.net/track/example",
                        "https://stream.example.net/tracks/example"
                    ],
                    TagNames = ["[F4M]", "[GWA]"],
                    PerformerNames = ["forum_poster"],
                },
                CancellationToken.None,
                new DownloaderMetadataApplyOptions(
                    CreateMissingTags: true,
                    CreateMissingPerformers: true,
                    CreateMissingStudio: true));

            Assert.True(applied);

            using var verifyScope = services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CoveContext>();
            var audio = await verifyDb.Audios
                .Include(item => item.Urls)
                .Include(item => item.AudioTags).ThenInclude(item => item.Tag)
                .Include(item => item.AudioPerformers).ThenInclude(item => item.Performer)
                .Include(item => item.Studio)
                .SingleAsync(item => item.Id == audioId);

            Assert.Equal("Merged Audio Title", audio.Title);
            Assert.Equal("Merged details from source and mirror", audio.Details);
            Assert.Equal(new DateOnly(2024, 2, 3), audio.Date);
            Assert.Equal("Forum", audio.Studio?.Name);
            Assert.Equal(
                [
                    "https://audio.example.net/track/example",
                    "https://forum.example.net/topics/abc/post",
                    "https://stream.example.net/tracks/example"
                ],
                audio.Urls.Select(item => item.Url).OrderBy(item => item).ToArray());
            Assert.Equal(["F4M", "GWA"], audio.AudioTags.Select(item => item.Tag!.Name).OrderBy(item => item).ToArray());
            Assert.Equal(["forum_poster"], audio.AudioPerformers.Select(item => item.Performer!.Name).OrderBy(item => item).ToArray());
            Assert.Equal(["forum_poster"], audio.AudioPerformers.Select(item => item.Performer!.Name).ToArray());
            Assert.Equal(2, audio.TagIds.Length);
            Assert.Single(audio.PerformerIds);
        }
        finally
        {
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task ApplyAudioMetadataAsync_DoesNotCreateMissingRelationsByDefault()
    {
        var service = CreateService(
            out var services,
            seedDatabase: db =>
            {
                db.Tags.Add(new Tag { Name = "Existing Tag" });
                db.Performers.Add(new Performer { Name = "Existing Performer" });
                db.Studios.Add(new Studio { Name = "Existing Studio" });
                db.Audios.Add(new Audio { Title = "Before" });
            });

        try
        {
            int audioId;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
                audioId = await db.Audios.Select(item => item.Id).SingleAsync();
            }

            var applied = await service.ApplyAudioMetadataAsync(
                audioId,
                new DownloaderService.ScrapedAudioMetadata
                {
                    Title = "After",
                    StudioName = "Missing Studio",
                    TagNames = ["Existing Tag", "Missing Tag"],
                    PerformerNames = ["Existing Performer", "Missing Performer"],
                },
                CancellationToken.None);

            Assert.True(applied);

            using var verifyScope = services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CoveContext>();
            var audio = await verifyDb.Audios
                .Include(item => item.AudioTags).ThenInclude(item => item.Tag)
                .Include(item => item.AudioPerformers).ThenInclude(item => item.Performer)
                .Include(item => item.Studio)
                .SingleAsync(item => item.Id == audioId);

            Assert.Equal("After", audio.Title);
            Assert.Null(audio.Studio);
            Assert.Equal(["Existing Tag"], audio.AudioTags.Select(item => item.Tag!.Name).ToArray());
            Assert.Equal(["Existing Performer"], audio.AudioPerformers.Select(item => item.Performer!.Name).ToArray());
            Assert.False(await verifyDb.Tags.AnyAsync(item => item.Name == "Missing Tag"));
            Assert.False(await verifyDb.Performers.AnyAsync(item => item.Name == "Missing Performer"));
            Assert.False(await verifyDb.Studios.AnyAsync(item => item.Name == "Missing Studio"));
        }
        finally
        {
            await services.DisposeAsync();
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
    public async Task DownloadAndIngestAsync_ThrowsWhenCanonicalSceneUrlAlreadyExists()
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
                    Title = "Canonical Scene",
                };

                db.Scenes.Add(existingScene);
                db.Set<SceneUrl>().Add(new SceneUrl { Scene = existingScene, Url = "https://www.example.com/watch/existing/?b=Two&utm_source=feed&a=One#ignored" });
            });

        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndIngestAsync(
                new DownloaderRequest(
                    "tests.fake-downloader/example",
                    "http://example.com/watch/existing?a=one&b=two",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["example.com"]),
                    "hd"),
                entityId: null,
                progress: null,
                CancellationToken.None));

            Assert.Contains("already downloaded", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Canonical Scene", error.Message, StringComparison.OrdinalIgnoreCase);
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
        var metadataApplyService = new FakeSceneMetadataApplyService();
        var service = CreateService(
            out var services,
            new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }],
                MaxConcurrentDownloads = 2,
            },
            scanService,
            metadataApplyService,
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
                new DownloaderBatchFollowUpDto { AutoApplyMetadata = true },
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

    [Fact]
    public async Task DownloadAndIngestBatchAsync_SkipsCanonicalDuplicateWithinBatch()
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
                        Url = "https://www.example.com/watch/batch-dupe?utm_source=mail&b=two&a=one",
                        Entity = "Scene",
                        Title = "Batch Dupe One",
                        CreateEntityIfMissing = true,
                    },
                    new DownloaderBatchItemDto
                    {
                        Url = "http://example.com/watch/batch-dupe?a=one&b=two#fragment",
                        Entity = "Scene",
                        Title = "Batch Dupe Two",
                        CreateEntityIfMissing = true,
                    },
                ],
                new DownloaderBatchFollowUpDto { AutoApplyMetadata = true },
                progress: null,
                CancellationToken.None);

            Assert.Equal(2, summary.TotalCount);
            Assert.Equal(1, summary.SucceededCount);
            Assert.Equal(1, summary.SkippedCount);
            Assert.Equal(0, summary.FailedCount);
            Assert.Contains(summary.Issues, issue => issue.Contains("already queued", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestBatchAsync_UsesCanonicalUrlsWhenExpandingSourceUrl()
    {
        const string sourceUrl = "https://forum.example.net/topics/abc/example-post";
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);

        var scanService = new FakeScanService();
        var service = CreateService(
            out var services,
            out var provider,
            config: new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }],
                MaxConcurrentDownloads = 2,
            },
            scanService: scanService,
            includeForumProvider: true);

        try
        {
            var summary = await service.DownloadAndIngestBatchAsync(
                [
                    new DownloaderBatchItemDto
                    {
                        Url = sourceUrl,
                        Entity = "Audio",
                        Title = "Forum Metadata Title",
                        CreateEntityIfMissing = true,
                    }
                ],
                new DownloaderBatchFollowUpDto(),
                progress: null,
                CancellationToken.None);

            Assert.Equal(2, summary.TotalCount);
            Assert.Equal(2, summary.SucceededCount);
            Assert.Equal(0, summary.SkippedCount);
            Assert.Equal(0, summary.FailedCount);

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
            var audios = await db.Audios
                .Include(audio => audio.Urls)
                .ToListAsync();
            var firstAudio = Assert.Single(audios, audio => audio.Urls.Any(url => url.Url == "https://audio.example.net/track/one"));
            var secondAudio = Assert.Single(audios, audio => audio.Urls.Any(url => url.Url == "https://audio.example.net/track/two"));
            Assert.Equal(
                ["https://audio.example.net/track/one", sourceUrl],
                firstAudio.Urls.Select(item => item.Url).OrderBy(item => item).ToList());
            Assert.Equal(
                ["https://audio.example.net/track/two", sourceUrl],
                secondAudio.Urls.Select(item => item.Url).OrderBy(item => item).ToList());

            var urls = await db.Set<AudioUrl>()
                .Select(item => item.Url)
                .OrderBy(item => item)
                .ToListAsync();

            Assert.Equal(
                ["https://audio.example.net/track/one", "https://audio.example.net/track/two", sourceUrl, sourceUrl],
                urls);
            Assert.Contains(sourceUrl, urls);
            Assert.All(provider.Requests, request => Assert.Equal(sourceUrl, request.SourceUrl));
            Assert.Equal(
                ["https://audio.example.net/track/one", "https://audio.example.net/track/two"],
                provider.Requests.Select(request => request.Url).OrderBy(item => item).ToList());
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndIngestBatchAsync_PrefersChildTagsWhenExpandingSourceUrl()
    {
        const string sourceUrl = "https://forum.example.net/topics/abc/example-post";
        var libraryRoot = Path.Combine(Path.GetTempPath(), "cove-downloader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(libraryRoot);
        var scanService = new FakeScanService();

        var service = CreateService(
            out var services,
            config: new CoveConfiguration
            {
                CovePaths = [new CovePath { Path = libraryRoot }],
                MaxConcurrentDownloads = 2,
            },
            scanService: scanService,
            includeForumProvider: true);

        try
        {
            var summary = await service.DownloadAndIngestBatchAsync(
                [
                    new DownloaderBatchItemDto
                    {
                        Url = sourceUrl,
                        Entity = "Audio",
                        Title = "Forum Metadata Title",
                        CreateEntityIfMissing = true,
                    }
                ],
                new DownloaderBatchFollowUpDto
                {
                    AutoApplyMetadata = true,
                    CreateMissingTags = true,
                },
                progress: null,
                CancellationToken.None);

            Assert.Equal(2, summary.TotalCount);
            Assert.Equal(2, summary.SucceededCount);
            Assert.Equal(0, summary.SkippedCount);
            Assert.Equal(0, summary.FailedCount);

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
            var audios = await db.Audios
                .Include(audio => audio.Urls)
                .Include(audio => audio.AudioTags)
                .ThenInclude(audioTag => audioTag.Tag)
                .ToListAsync();

            var firstAudio = Assert.Single(audios, audio => audio.Urls.Any(url => url.Url == "https://audio.example.net/track/one"));
            var secondAudio = Assert.Single(audios, audio => audio.Urls.Any(url => url.Url == "https://audio.example.net/track/two"));

            Assert.Equal(["Track Tag"], firstAudio.AudioTags.Select(item => item.Tag!.Name).OrderBy(name => name).ToList());
            Assert.Equal(["Forum Tag"], secondAudio.AudioTags.Select(item => item.Tag!.Name).OrderBy(name => name).ToList());
        }
        finally
        {
            await services.DisposeAsync();
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PreflightBatchAsync_QueuesSpecificAudioChildWhenSourceUrlExistsOnSibling()
    {
        const string sourceUrl = "https://forum.example.net/topics/abc/example-post";
        var service = CreateService(
            out var services,
            seedDatabase: db =>
            {
                var existingAudio = new Audio { Title = "Track One" };
                db.Audios.Add(existingAudio);
                db.Set<AudioUrl>().Add(new AudioUrl { Audio = existingAudio, Url = "https://audio.example.net/track/one" });
                db.Set<AudioUrl>().Add(new AudioUrl { Audio = existingAudio, Url = sourceUrl });
                db.AudioFiles.Add(new AudioFile
                {
                    Audio = existingAudio,
                    Path = Path.Combine(Path.GetTempPath(), "track-one.mp3"),
                    Basename = "track-one.mp3",
                });
            },
            includeForumProvider: true);

        try
        {
            var preflight = await service.PreflightBatchAsync(
                [
                    new DownloaderBatchItemDto
                    {
                        Url = sourceUrl,
                        Entity = "Audio",
                        Title = "Forum Metadata Title",
                        CreateEntityIfMissing = true,
                    }
                ],
                new DownloaderBatchFollowUpDto(),
                CancellationToken.None);

            var item = Assert.Single(preflight.ItemsToQueue);
            Assert.Equal("https://audio.example.net/track/two", item.Url);
            var issue = Assert.Single(preflight.Issues);
            Assert.Equal("skipped", issue.Kind);
            Assert.Contains("Track One", issue.Reason);
        }
        finally
        {
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task PreflightBatchAsync_SkipsExistingSceneUrlsBeforeQueueing()
    {
        var service = CreateService(
            out var services,
            seedDatabase: db =>
            {
                var existingScene = new Scene { Title = "Already In Cove" };
                db.Scenes.Add(existingScene);
                db.Set<SceneUrl>().Add(new SceneUrl
                {
                    Scene = existingScene,
                    Url = "https://www.example.com/watch/existing?utm_source=mail&b=two&a=one",
                });
            });

        try
        {
            var preflight = await service.PreflightBatchAsync(
                [
                    new DownloaderBatchItemDto
                    {
                        Url = "http://example.com/watch/existing?a=one&b=two#fragment",
                        Entity = "Scene",
                        Title = "Should Skip",
                        CreateEntityIfMissing = true,
                    },
                    new DownloaderBatchItemDto
                    {
                        Url = "https://example.com/watch/new",
                        Entity = "Scene",
                        Title = "Should Queue",
                        CreateEntityIfMissing = true,
                    },
                ],
                new DownloaderBatchFollowUpDto(),
                CancellationToken.None);

            var item = Assert.Single(preflight.ItemsToQueue);
            Assert.Equal("https://example.com/watch/new", item.Url);
            var issue = Assert.Single(preflight.Issues);
            Assert.Equal("skipped", issue.Kind);
            Assert.Equal("Should Skip", issue.Label);
            Assert.Contains("Already In Cove", issue.Reason);
        }
        finally
        {
            await services.DisposeAsync();
        }
    }

    private static DownloaderService CreateService(
        out ServiceProvider services,
        CoveConfiguration? config = null,
        IScanService? scanService = null,
        ISceneMetadataApplyService? sceneMetadataApplyService = null,
        Action<CoveContext>? seedDatabase = null,
        bool includeForumProvider = false)
    {
        return CreateService(out services, out _, config, scanService, sceneMetadataApplyService, seedDatabase, includeForumProvider);
    }

    private static DownloaderService CreateService(
        out ServiceProvider services,
        out FakeDownloaderProvider downloaderProvider,
        CoveConfiguration? config = null,
        IScanService? scanService = null,
        ISceneMetadataApplyService? sceneMetadataApplyService = null,
        Action<CoveContext>? seedDatabase = null,
        bool includeForumProvider = false)
    {
        var serviceCollection = new ServiceCollection();
        var databaseName = $"cove-downloader-tests-{Guid.NewGuid():N}";
        var effectiveConfig = config ?? new CoveConfiguration();
        var extensionManager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = Path.GetTempPath(),
            CoveVersion = "test",
        });

        downloaderProvider = new FakeDownloaderProvider();
        extensionManager.Register(downloaderProvider);
        if (includeForumProvider)
            extensionManager.Register(new FakeForumProvider());

        serviceCollection.AddHttpClient();
        serviceCollection.AddDbContext<DownloaderTestContext>(options => options.UseInMemoryDatabase(databaseName));
        serviceCollection.AddScoped<CoveContext>(provider => provider.GetRequiredService<DownloaderTestContext>());
        serviceCollection.AddSingleton(extensionManager);
        serviceCollection.AddSingleton(provider => new ScraperService(
            effectiveConfig,
            NullLogger<ScraperService>.Instance,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ExtensionManager>()));
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

        return new DownloaderService(
            extensionManager,
            services.GetRequiredService<IHttpClientFactory>(),
            NullLoggerFactory.Instance,
            effectiveConfig,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DownloaderService>.Instance);
    }

    private sealed class FakeScanService : IScanService
    {
        public string? ImportedPath { get; private set; }
        public int? SceneId { get; private set; }
        public int? ImageId { get; private set; }
        public int? GalleryId { get; private set; }
        public int? AudioId { get; private set; }
        public int? TextDocumentId { get; private set; }
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

        public Task<int> ImportDownloadedAudioAsync(string path, int? audioId, CancellationToken ct = default)
        {
            ImportedPath = path;
            AudioId = audioId;
            return Task.FromResult(audioId ?? 1);
        }

        public Task<int> ImportDownloadedTextAsync(string path, int? textDocumentId, CancellationToken ct = default)
        {
            ImportedPath = path;
            TextDocumentId = textDocumentId;
            return Task.FromResult(textDocumentId ?? 1);
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
        public ConcurrentBag<DownloaderRequest> Requests { get; } = [];

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
                    DownloaderCapabilities.MultiQuality | DownloaderCapabilities.InlineMetadata),
                new DownloaderDescriptor(
                    "tests.fake-downloader/audio",
                    "Audio Download",
                    DownloaderEntity.Audio,
                    ["multi.example.com", "audio.example.net"],
                    DownloaderCapabilities.None)
            ];
        }

        public Task<IReadOnlyList<DownloaderUrlMatch>> MatchAllAsync(string url, CancellationToken ct)
        {
            if (url.Contains("audio.example.net", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<IReadOnlyList<DownloaderUrlMatch>>([new DownloaderUrlMatch("tests.fake-downloader/audio", url, null, "Linked audio")]);

            if (!url.Contains("multi.example.com", StringComparison.OrdinalIgnoreCase))
            {
                return MatchAsync(url, ct).ContinueWith<IReadOnlyList<DownloaderUrlMatch>>(
                    task => task.Result == null ? [] : [task.Result],
                    ct);
            }

            return Task.FromResult<IReadOnlyList<DownloaderUrlMatch>>([
                new DownloaderUrlMatch("tests.fake-downloader/example", url, [new DownloaderQualityOption("hd", "HD")], "Example scene"),
                new DownloaderUrlMatch("tests.fake-downloader/audio", url, null, "Example audio"),
            ]);
        }

        public Task<DownloaderUrlMatch?> MatchAsync(string url, CancellationToken ct)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (!uri.Host.Equals("example.com", StringComparison.OrdinalIgnoreCase)
                    && !uri.Host.Equals("www.example.com", StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult<DownloaderUrlMatch?>(null);
            }

            return Task.FromResult<DownloaderUrlMatch?>(new DownloaderUrlMatch(
                "tests.fake-downloader/example",
                url,
                [new DownloaderQualityOption("hd", "HD")],
                "Example scene"));
        }

        public async Task<DownloaderResult?> DownloadAsync(DownloaderRequest request, IDownloaderHost host, CancellationToken ct)
        {
            Requests.Add(request);

            var filename = request.Entity == DownloaderEntity.Audio
                ? $"downloaded-audio-{request.Url.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "file"}.mp3"
                : "downloaded-scene.mp4";
            var filePath = Path.Combine(host.TempDirectory, filename);
            await File.WriteAllTextAsync(filePath, "fake media payload", ct);

            host.ReportProgress(1d, "Downloaded fake file");
            return new DownloaderResult(
                filename,
                filename,
                InlineSceneMetadata: request.Entity == DownloaderEntity.Scene ? new ScrapedSceneDto
                {
                    Title = "Downloaded Scene",
                    Urls = [request.Url],
                } : null);
        }
    }

    private sealed class FakeForumProvider : IDownloaderProvider, IScraperProvider
    {
        public string Id => "tests.fake-forum";
        public string Name => "Fake Forum";
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
                    "tests.fake-forum/native-video",
                    "Forum Native Video",
                    DownloaderEntity.Scene,
                    ["forum.example.net"],
                    DownloaderCapabilities.None)
            ];
        }

        public IReadOnlyList<ScraperDescriptor> GetScrapers()
        {
            return
            [
                new ScraperDescriptor(
                    "tests.fake-forum/audio-metadata",
                    "Forum Audio Metadata",
                    ScraperEntity.Audio,
                    ScraperCapabilities.ByUrl,
                    ["forum.example.net", "audio.example.net"])
            ];
        }

        public Task<IReadOnlyList<DownloaderUrlMatch>> MatchAllAsync(string url, CancellationToken ct)
        {
            if (!url.Contains("forum.example.net", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<IReadOnlyList<DownloaderUrlMatch>>([]);

            return Task.FromResult<IReadOnlyList<DownloaderUrlMatch>>([
                new DownloaderUrlMatch(
                    "tests.fake-forum/divert",
                    "https://audio.example.net/track/one",
                    Label: "Forum post title (1)",
                    SourceUrl: url,
                    Divert: true),
                new DownloaderUrlMatch(
                    "tests.fake-forum/divert",
                    "https://audio.example.net/track/two",
                    Label: "Forum post title (2)",
                    SourceUrl: url,
                    Divert: true),
            ]);
        }

        public Task<ScrapedAudioDto?> ScrapeAudioAsync(ScraperRequest<AudioScrapeInput> request, CancellationToken ct)
        {
            var url = request.Input.Url ?? string.Empty;
            if (url.Contains("forum.example.net", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ScrapedAudioDto?>(new ScrapedAudioDto
                {
                    Title = "Forum Metadata Title",
                    Urls =
                    [
                        "https://audio.example.net/track/one",
                        "https://audio.example.net/track/two",
                        url,
                    ],
                    TagNames = ["Forum Tag"],
                });
            }

            if (url.Contains("audio.example.net/track/one", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ScrapedAudioDto?>(new ScrapedAudioDto
                {
                    Urls = [url],
                    TagNames = ["Track Tag"],
                });
            }

            if (url.Contains("audio.example.net", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ScrapedAudioDto?>(new ScrapedAudioDto
                {
                    Urls = [url],
                });
            }

            return Task.FromResult<ScrapedAudioDto?>(null);
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
        }
    }

    private sealed class FakeSceneMetadataApplyService : ISceneMetadataApplyService
    {
        public int? SceneId { get; private set; }
        public ScrapedSceneDto? Metadata { get; private set; }

        public Task<bool> ApplyAsync(int sceneId, ScrapedSceneDto metadata, DownloaderMetadataApplyOptions? options = null, CancellationToken ct = default)
        {
            SceneId = sceneId;
            Metadata = metadata;
            return Task.FromResult(true);
        }
    }
}
