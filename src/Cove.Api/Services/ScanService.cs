using System.IO.Enumeration;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Cove.Core.Entities;
using Cove.Core.Entities.Galleries.Zip;
using Cove.Core.Events;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Plugins;
using ExtJobProgress = Cove.Plugins.IJobProgress;

namespace Cove.Api.Services;

public class ScanService(
    IJobService jobService,
    IServiceScopeFactory scopeFactory,
    CoveConfiguration config,
    IEventBus eventBus,
    IFingerprintService fingerprintService,
    IThumbnailService thumbnailService,
    TextExtractionService textExtractionService,
    ZipGalleryReader zipGalleryReader,
    ExtensionManager extensionManager,
    ILogger<ScanService> logger) : IScanService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> FolderCreationLocks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Resolves the max degree of parallelism from config.
    /// -1 means use all processors; 0 or 1 means single-threaded; >1 means that many threads.
    /// </summary>
    private int ResolveMaxParallelism()
    {
        var configured = config.MaxParallelTasks;
        if (configured == -1) return Environment.ProcessorCount;
        if (configured <= 0) return 1;
        return configured;
    }

    public async Task<int> ImportDownloadedSceneAsync(string path, int? sceneId, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Downloaded scene file not found", path);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var videoFile = await ProcessVideoFileAsync(db, path, sceneId, ct);
        await db.SaveChangesAsync(ct);

        var resolvedSceneId = videoFile.SceneId;
        if (!resolvedSceneId.HasValue || resolvedSceneId.Value == 0)
            throw new InvalidOperationException($"Imported video file {path} was not attached to a scene");

        eventBus.Publish(new EntityEvent(
            sceneId.HasValue ? EventType.SceneUpdated : EventType.SceneCreated,
            "Scene",
            resolvedSceneId.Value));

        return resolvedSceneId.Value;
    }

    public async Task<int> ImportDownloadedImageAsync(string path, int? imageId, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Downloaded image file not found", path);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var image = await ProcessImageFileAsync(db, path, imageId, ct);
        await db.SaveChangesAsync(ct);

        if (image.Id == 0)
            throw new InvalidOperationException($"Imported image file {path} was not attached to an image");

        eventBus.Publish(new EntityEvent(
            imageId.HasValue ? EventType.ImageUpdated : EventType.ImageCreated,
            "Image",
            image.Id));

        return image.Id;
    }

    public async Task<int> ImportDownloadedGalleryAsync(string path, int? galleryId, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Downloaded gallery file not found", path);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var gallery = await ProcessGalleryFileAsync(db, path, galleryId, ct);
        await db.SaveChangesAsync(ct);

        if (gallery.Id == 0)
            throw new InvalidOperationException($"Imported gallery file {path} was not attached to a gallery");

        eventBus.Publish(new EntityEvent(
            galleryId.HasValue ? EventType.GalleryUpdated : EventType.GalleryCreated,
            "Gallery",
            gallery.Id));

        return gallery.Id;
    }

    public async Task<int> ImportDownloadedAudioAsync(string path, int? audioId, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Downloaded audio file not found", path);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var audio = await ProcessAudioFileAsync(db, path, audioId, ct);
        await db.SaveChangesAsync(ct);

        if (audio.Id == 0)
            throw new InvalidOperationException($"Imported audio file {path} was not attached to an audio item");

        eventBus.Publish(new EntityEvent(
            audioId.HasValue ? EventType.AudioUpdated : EventType.AudioCreated,
            "Audio",
            audio.Id));

        return audio.Id;
    }

    public async Task<int> ImportDownloadedTextAsync(string path, int? textDocumentId, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Downloaded text file not found", path);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var textDocument = await ProcessTextFileAsync(db, path, textDocumentId, ct);
        await db.SaveChangesAsync(ct);

        if (textDocument.Id == 0)
            throw new InvalidOperationException($"Imported text file {path} was not attached to a text document");

        eventBus.Publish(new EntityEvent(
            textDocumentId.HasValue ? EventType.TextUpdated : EventType.TextCreated,
            "Text",
            textDocument.Id));

        return textDocument.Id;
    }

    public string StartScan(ScanOperationOptions? options = null)
    {
        options ??= new ScanOperationOptions();

        return jobService.Enqueue("scan", "Scanning library", async (progress, ct) =>
        {
            var cfg = config;
            var scanTargets = ResolveScanTargets(cfg, options.Paths);

            if (scanTargets.Count == 0)
            {
                logger.LogWarning("No cove paths configured. Nothing to scan.");
                return;
            }

            var videoExts = new HashSet<string>(cfg.VideoExtensions, StringComparer.OrdinalIgnoreCase);
            var imageExts = new HashSet<string>(cfg.ImageExtensions, StringComparer.OrdinalIgnoreCase);
            var galleryExts = new HashSet<string>(cfg.GalleryExtensions, StringComparer.OrdinalIgnoreCase);
            var audioExts = new HashSet<string>(cfg.AudioExtensions, StringComparer.OrdinalIgnoreCase);
            var textExts = new HashSet<string>(cfg.TextExtensions, StringComparer.OrdinalIgnoreCase);
            var allExts = videoExts.Union(imageExts).Union(galleryExts).Union(audioExts).Union(textExts).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var processedVideoPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var processedImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var processedAudioPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var processedTextPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ignoreRuleCache = new Dictionary<string, List<IgnoreRule>>(StringComparer.OrdinalIgnoreCase);

            // Phase 1: Discover files
            progress.Report(0, "Discovering files...");
            var files = new List<DiscoveredFile>();
            foreach (var scanTarget in scanTargets)
            {
                if (scanTarget.IsFile)
                {
                    if (!File.Exists(scanTarget.Path))
                    {
                        logger.LogWarning("Scan target does not exist: {Path}", scanTarget.Path);
                        continue;
                    }

                    var ext = Path.GetExtension(scanTarget.Path);
                    if (!allExts.Contains(ext))
                    {
                        continue;
                    }
                    if (scanTarget.ExcludeVideo && videoExts.Contains(ext))
                    {
                        continue;
                    }
                    if (scanTarget.ExcludeImage && imageExts.Contains(ext))
                    {
                        continue;
                    }
                    if (scanTarget.ExcludeAudio && audioExts.Contains(ext))
                    {
                        continue;
                    }
                    if (scanTarget.ExcludeText && textExts.Contains(ext))
                    {
                        continue;
                    }
                    if (IsExcluded(scanTarget.Path, cfg.ExcludePatterns)
                        || IsExcludedByFolderIgnore(scanTarget.Path, Path.GetDirectoryName(scanTarget.Path) ?? scanTarget.Path, ignoreRuleCache))
                    {
                        continue;
                    }

                    files.Add(new DiscoveredFile(NormalizePath(scanTarget.Path), ext));
                    continue;
                }

                if (!Directory.Exists(scanTarget.Path))
                {
                    logger.LogWarning("Scan target does not exist: {Path}", scanTarget.Path);
                    continue;
                }

                var dirFiles = EnumerateFilesSafely(scanTarget.Path)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f);
                        if (!allExts.Contains(ext)) return false;
                        if (scanTarget.ExcludeVideo && videoExts.Contains(ext)) return false;
                        if (scanTarget.ExcludeImage && imageExts.Contains(ext)) return false;
                        if (scanTarget.ExcludeAudio && audioExts.Contains(ext)) return false;
                        if (scanTarget.ExcludeText && textExts.Contains(ext)) return false;
                        return !IsExcluded(f, cfg.ExcludePatterns)
                            && !IsExcludedByFolderIgnore(f, scanTarget.Path, ignoreRuleCache);
                    })
                    .Select(f => new DiscoveredFile(NormalizePath(f), Path.GetExtension(f)));

                files.AddRange(dirFiles);
            }

            logger.LogInformation("Discovered {Count} files to scan", files.Count);

            if (files.Count > 0)
            {
                // Phase 2: Process files
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CoveContext>();

                var processedCount = 0;
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    processedCount++;
                    progress.Report(0.85 * (double)processedCount / files.Count, Path.GetFileName(file.Path));

                    try
                    {
                        // Check if file already exists in DB by path
                        var existingFolderPath = NormalizeStoredFolderPath(Path.GetDirectoryName(file.Path) ?? file.Path);
                        var existingFolder = await db.Folders
                            .FirstOrDefaultAsync(f => f.Path == existingFolderPath, ct);

                        if (existingFolder != null)
                        {
                            var basename = Path.GetFileName(file.Path);
                            var existingFile = await db.Set<BaseFileEntity>()
                                .FirstOrDefaultAsync(f => f.ParentFolderId == existingFolder.Id && f.Basename == basename, ct);

                            if (existingFile != null)
                            {
                                // Check if file has been modified — but always re-process videos with missing metadata
                                var fileInfo = new FileInfo(file.Path);
                                var needsMetadata = existingFile switch
                                {
                                    VideoFile vf => NeedsVideoMetadataProbe(vf),
                                    AudioFile af => af.Duration == 0 && string.IsNullOrWhiteSpace(af.AudioCodec),
                                    TextFile tf => !tf.WordCount.HasValue && string.IsNullOrWhiteSpace(tf.ExcerptText),
                                    _ => false,
                                };
                                if (!options.Rescan && !needsMetadata && existingFile.ModTime >= fileInfo.LastWriteTimeUtc && existingFile.Size == fileInfo.Length)
                                {
                                    if (existingFile is VideoFile)
                                    {
                                        var existingVideo = await db.VideoFiles
                                            .Include(item => item.Captions)
                                            .FirstOrDefaultAsync(item => item.Id == existingFile.Id, ct);
                                        if (existingVideo != null)
                                            SyncVideoCaptions(existingVideo, file.Path);
                                    }
                                    continue; // Not modified and metadata present, skip
                                }
                            }
                        }

                        // Process the file
                        if (videoExts.Contains(file.Extension))
                        {
                            processedVideoPaths.Add(file.Path);
                            await ProcessVideoFileAsync(db, file.Path, sceneId: null, ct);
                        }
                        else if (imageExts.Contains(file.Extension))
                        {
                            processedImagePaths.Add(file.Path);
                            await ProcessImageFileAsync(db, file.Path, imageId: null, ct);
                        }
                        else if (audioExts.Contains(file.Extension))
                        {
                            processedAudioPaths.Add(file.Path);
                            await ProcessAudioFileAsync(db, file.Path, audioId: null, ct);
                        }
                        else if (textExts.Contains(file.Extension))
                        {
                            processedTextPaths.Add(file.Path);
                            await ProcessTextFileAsync(db, file.Path, textDocumentId: null, ct);
                        }
                        else if (galleryExts.Contains(file.Extension))
                            await ProcessGalleryFileAsync(db, file.Path, galleryId: null, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing file: {Path}", file.Path);
                    }
                }

                await db.SaveChangesAsync(ct);

                // Phase 3: Create galleries from folders (if enabled)
                if (cfg.CreateGalleriesFromFolders || HasForceGalleryHints(files))
                {
                    progress.Report(0.90, "Creating galleries from folders...");
                    await CreateGalleriesFromFoldersAsync(db, cfg.CreateGalleriesFromFolders, ct);
                }

                await GenerateRequestedAssetsAsync(db, progress, processedVideoPaths, processedImagePaths, processedAudioPaths, processedTextPaths, options, thumbnailService, ct);
            }

            // Phase 5: Extension scan participants
            var participants = extensionManager.GetScanParticipants();
            if (participants.Count > 0)
            {
                var scanPathInfos = scanTargets
                    .Select(t => new ScanPathInfo(t.Path, t.ExcludeVideo, t.ExcludeImage, t.ExcludeAudio, t.IsFile, t.ExcludeText))
                    .ToList();

                for (var i = 0; i < participants.Count; i++)
                {
                    var participant = participants[i];
                    var participantProgress = new ScopedProgress(progress,
                        rangeStart: 0.95 + (0.05 * i / participants.Count),
                        rangeEnd: 0.95 + (0.05 * (i + 1) / participants.Count));

                    try
                    {
                        logger.LogInformation("Running scan participant: {Name}", participant.Name);
                        using var participantScope = scopeFactory.CreateScope();
                        var scanContext = new ScanContext(scanPathInfos, participantProgress, participantScope.ServiceProvider, options.Rescan);
                        await participant.ScanAsync(scanContext, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Extension scan participant {Name} failed", participant.Name);
                    }
                }
            }

            logger.LogInformation("Scan completed. Processed {Count} core files, {ParticipantCount} extension participant(s)", files.Count, participants.Count);
            eventBus.Publish(new CoveEvent(EventType.ScanCompleted));
        });
    }

    private async Task GenerateRequestedAssetsAsync(
        CoveContext db,
        Cove.Core.Interfaces.IJobProgress progress,
        HashSet<string> processedVideoPaths,
        HashSet<string> processedImagePaths,
        HashSet<string> processedAudioPaths,
        HashSet<string> processedTextPaths,
        ScanOperationOptions options,
        IThumbnailService thumbnailService,
        CancellationToken ct)
    {
        var generateSceneAssets = options.GenerateCovers || options.GeneratePreviews || options.GenerateSprites || options.GeneratePhashes || options.GenerateMd5;
        var generateImageAssets = options.GenerateImagePhashes || options.GenerateImageThumbnails || options.GenerateMd5;
        var generateAudioAssets = options.GenerateAudioPhashes || options.GenerateMd5;
        var generateTextAssets = options.GenerateTextPhashes || options.GenerateMd5;

        if ((!generateSceneAssets && !generateImageAssets && !generateAudioAssets && !generateTextAssets)
            || (processedVideoPaths.Count == 0 && processedImagePaths.Count == 0 && processedAudioPaths.Count == 0 && processedTextPaths.Count == 0))
        {
            return;
        }

        if (generateSceneAssets && processedVideoPaths.Count > 0)
        {
            progress.Report(0.92, "Generating scene assets...");

            var videoDirs = processedVideoPaths
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateFiles = await db.VideoFiles
                .Include(f => f.ParentFolder)
                .Include(f => f.Fingerprints)
                .Where(f => f.ParentFolder != null && videoDirs.Contains(f.ParentFolder.Path))
                .ToListAsync(ct);

            var sceneFiles = candidateFiles
                .Where(file => file.ParentFolder != null && processedVideoPaths.Contains(NormalizePath(Path.Combine(file.ParentFolder.Path, file.Basename))))
                .Where(file => file.SceneId.HasValue && file.SceneId.Value != 0)
                .GroupBy(file => file.SceneId)
                .Select(group => group.First())
                .ToList();

            var total = Math.Max(sceneFiles.Count, 1);
            var completed = 0;

            var maxParallelism = ResolveMaxParallelism();
            await Parallel.ForEachAsync(sceneFiles, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct }, async (sceneFile, token) =>
            {
                var done = Interlocked.Increment(ref completed);
                var sceneId = sceneFile.SceneId!.Value;

                progress.Report(0.92 + (0.06 * done / total), $"Generating scene assets ({done}/{sceneFiles.Count})");

                if (options.GenerateCovers)
                {
                    await thumbnailService.GenerateSceneThumbnailAsync(sceneId, null, token);
                }
                if (options.GeneratePreviews)
                {
                    await thumbnailService.GenerateScenePreviewAsync(sceneId, token);
                }
                if (options.GenerateSprites)
                {
                    await thumbnailService.GenerateSceneSpriteAsync(sceneId, token);
                }
                if (options.GeneratePhashes && sceneFile.ParentFolder != null)
                {
                    var filePath = Path.Combine(sceneFile.ParentFolder.Path, sceneFile.Basename);
                    var phash = await fingerprintService.ComputeVideoPhashAsync(filePath, sceneFile.Duration, token);
                    if (!string.IsNullOrWhiteSpace(phash))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == sceneFile.Id && fp.Type == "phash", token);
                        if (existing != null)
                        {
                            existing.Value = phash;
                        }
                        else
                        {
                            innerDb.FileFingerprints.Add(new FileFingerprint
                            {
                                FileId = sceneFile.Id,
                                Type = "phash",
                                Value = phash,
                            });
                        }
                        await innerDb.SaveChangesAsync(token);
                    }
                }
                if (options.GenerateMd5 && sceneFile.ParentFolder != null)
                {
                    var filePath = Path.Combine(sceneFile.ParentFolder.Path, sceneFile.Basename);
                    var md5 = await fingerprintService.ComputeMd5Async(filePath, token);
                    if (!string.IsNullOrWhiteSpace(md5))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == sceneFile.Id && fp.Type == "md5", token);
                        if (existing != null)
                        {
                            existing.Value = md5;
                        }
                        else
                        {
                            innerDb.FileFingerprints.Add(new FileFingerprint
                            {
                                FileId = sceneFile.Id,
                                Type = "md5",
                                Value = md5,
                            });
                        }
                        await innerDb.SaveChangesAsync(token);
                    }
                }
            });
        }

        if (generateImageAssets && processedImagePaths.Count > 0)
        {
            progress.Report(0.98, "Generating image assets...");

            var imageDirs = processedImagePaths
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateFiles = await db.ImageFiles
                .Include(f => f.ParentFolder)
                .Include(f => f.Fingerprints)
                .Where(f => f.ParentFolder != null && imageDirs.Contains(f.ParentFolder.Path))
                .ToListAsync(ct);

            var imageFiles = candidateFiles
                .Where(file => file.ParentFolder != null && processedImagePaths.Contains(NormalizePath(Path.Combine(file.ParentFolder.Path, file.Basename))))
                .ToList();

            var total = Math.Max(imageFiles.Count, 1);
            var completed = 0;
            var imgMaxParallelism = ResolveMaxParallelism();
            await Parallel.ForEachAsync(imageFiles, new ParallelOptions { MaxDegreeOfParallelism = imgMaxParallelism, CancellationToken = ct }, async (imageFile, token) =>
            {
                var done = Interlocked.Increment(ref completed);
                progress.Report(0.98 + (0.01 * done / total), $"Generating image assets ({done}/{imageFiles.Count})");

                if (imageFile.ParentFolder == null)
                    return;

                if (options.GenerateImageThumbnails && imageFile.ImageId.HasValue)
                {
                    await thumbnailService.GenerateImageThumbnailAsync(imageFile.ImageId.Value, ct: token);
                }

                if (options.GenerateImagePhashes)
                {
                    var filePath = Path.Combine(imageFile.ParentFolder.Path, imageFile.Basename);
                    var phash = await fingerprintService.ComputeImagePhashAsync(filePath, token);
                    if (!string.IsNullOrWhiteSpace(phash))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == imageFile.Id && fp.Type == "phash", token);
                        if (existing != null)
                        {
                            existing.Value = phash;
                        }
                        else
                        {
                            innerDb.FileFingerprints.Add(new FileFingerprint
                            {
                                FileId = imageFile.Id,
                                Type = "phash",
                                Value = phash,
                            });
                        }
                        await innerDb.SaveChangesAsync(token);
                    }
                }

                if (options.GenerateMd5)
                {
                    var filePath = Path.Combine(imageFile.ParentFolder.Path, imageFile.Basename);
                    var md5 = await fingerprintService.ComputeMd5Async(filePath, token);
                    if (!string.IsNullOrWhiteSpace(md5))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == imageFile.Id && fp.Type == "md5", token);
                        if (existing != null)
                        {
                            existing.Value = md5;
                        }
                        else
                        {
                            innerDb.FileFingerprints.Add(new FileFingerprint
                            {
                                FileId = imageFile.Id,
                                Type = "md5",
                                Value = md5,
                            });
                        }
                        await innerDb.SaveChangesAsync(token);
                    }
                }
            });
        }

        if (generateAudioAssets && processedAudioPaths.Count > 0)
        {
            progress.Report(0.99, "Generating audio fingerprints...");

            var audioDirs = processedAudioPaths
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateFiles = await db.AudioFiles
                .Include(f => f.ParentFolder)
                .Include(f => f.Fingerprints)
                .Where(f => f.ParentFolder != null && audioDirs.Contains(f.ParentFolder.Path))
                .ToListAsync(ct);

            var audioFiles = candidateFiles
                .Where(file => file.ParentFolder != null && processedAudioPaths.Contains(NormalizePath(Path.Combine(file.ParentFolder.Path, file.Basename))))
                .ToList();

            var total = Math.Max(audioFiles.Count, 1);
            var completed = 0;
            var maxParallelism = ResolveMaxParallelism();
            await Parallel.ForEachAsync(audioFiles, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct }, async (audioFile, token) =>
            {
                var done = Interlocked.Increment(ref completed);
                progress.Report(0.99, $"Generating audio fingerprints ({done}/{audioFiles.Count})");

                if (audioFile.ParentFolder == null)
                    return;

                var filePath = Path.Combine(audioFile.ParentFolder.Path, audioFile.Basename);
                if (options.GenerateAudioPhashes)
                {
                    var phash = await fingerprintService.ComputeAudioPhashAsync(filePath, token);
                    if (!string.IsNullOrWhiteSpace(phash))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == audioFile.Id && fp.Type == "phash", token);
                        if (existing != null) existing.Value = phash;
                        else innerDb.FileFingerprints.Add(new FileFingerprint { FileId = audioFile.Id, Type = "phash", Value = phash });
                        await innerDb.SaveChangesAsync(token);
                    }
                }

                if (options.GenerateMd5)
                {
                    var md5 = await fingerprintService.ComputeMd5Async(filePath, token);
                    if (!string.IsNullOrWhiteSpace(md5))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == audioFile.Id && fp.Type == "md5", token);
                        if (existing != null) existing.Value = md5;
                        else innerDb.FileFingerprints.Add(new FileFingerprint { FileId = audioFile.Id, Type = "md5", Value = md5 });
                        await innerDb.SaveChangesAsync(token);
                    }
                }
            });
        }

        if (generateTextAssets && processedTextPaths.Count > 0)
        {
            progress.Report(0.99, "Generating text fingerprints...");

            var textDirs = processedTextPaths
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateFiles = await db.TextFiles
                .Include(f => f.ParentFolder)
                .Include(f => f.Fingerprints)
                .Where(f => f.ParentFolder != null && textDirs.Contains(f.ParentFolder.Path))
                .ToListAsync(ct);

            var textFiles = candidateFiles
                .Where(file => file.ParentFolder != null && processedTextPaths.Contains(NormalizePath(Path.Combine(file.ParentFolder.Path, file.Basename))))
                .ToList();

            var total = Math.Max(textFiles.Count, 1);
            var completed = 0;
            var maxParallelism = ResolveMaxParallelism();
            await Parallel.ForEachAsync(textFiles, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct }, async (textFile, token) =>
            {
                var done = Interlocked.Increment(ref completed);
                progress.Report(0.99, $"Generating text fingerprints ({done}/{textFiles.Count})");

                if (textFile.ParentFolder == null)
                    return;

                var filePath = Path.Combine(textFile.ParentFolder.Path, textFile.Basename);
                if (options.GenerateTextPhashes)
                {
                    var phash = await fingerprintService.ComputeTextPhashAsync(filePath, token);
                    if (!string.IsNullOrWhiteSpace(phash))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == textFile.Id && fp.Type == "phash", token);
                        if (existing != null) existing.Value = phash;
                        else innerDb.FileFingerprints.Add(new FileFingerprint { FileId = textFile.Id, Type = "phash", Value = phash });
                        await innerDb.SaveChangesAsync(token);
                    }
                }

                if (options.GenerateMd5)
                {
                    var md5 = await fingerprintService.ComputeMd5Async(filePath, token);
                    if (!string.IsNullOrWhiteSpace(md5))
                    {
                        using var innerScope = scopeFactory.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<CoveContext>();
                        var existing = await innerDb.FileFingerprints.FirstOrDefaultAsync(fp => fp.FileId == textFile.Id && fp.Type == "md5", token);
                        if (existing != null) existing.Value = md5;
                        else innerDb.FileFingerprints.Add(new FileFingerprint { FileId = textFile.Id, Type = "md5", Value = md5 });
                        await innerDb.SaveChangesAsync(token);
                    }
                }
            });
        }
    }

    /// <summary>
    /// Create folder-based galleries for folders that contain images but have no gallery yet.
    /// </summary>
    private async Task CreateGalleriesFromFoldersAsync(CoveContext db, bool createAllEligibleFolders, CancellationToken ct)
    {
        // Find folders that contain image files but don't already have a gallery
        var foldersWithImages = await db.ImageFiles
            .Where(f => f.ParentFolderId != 0 && f.ZipFileId == null) // Only real folders, not zip virtual folders
            .Select(f => f.ParentFolderId)
            .Distinct()
            .ToListAsync(ct);

        if (foldersWithImages.Count == 0) return;

        // Get existing folder-based galleries
        var existingGalleryFolderIds = await db.Galleries
            .Where(g => g.FolderId != null && foldersWithImages.Contains(g.FolderId.Value))
            .Select(g => g.FolderId!.Value)
            .ToListAsync(ct);

        var newFolderIds = foldersWithImages.Except(existingGalleryFolderIds).ToList();
        if (newFolderIds.Count == 0) return;

        // Load the folders
        var folders = await db.Folders
            .Where(f => newFolderIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        var eligibleFolderIds = folders
            .Where(item => ShouldCreateFolderGallery(item.Value.Path, createAllEligibleFolders))
            .Select(item => item.Key)
            .ToHashSet();

        if (eligibleFolderIds.Count == 0) return;

        // Get image IDs per folder
        var imagesByFolder = await db.ImageFiles
            .Where(f => eligibleFolderIds.Contains(f.ParentFolderId) && f.ZipFileId == null && f.ImageId != null)
            .GroupBy(f => f.ParentFolderId)
            .Select(g => new { FolderId = g.Key, ImageIds = g.Select(f => f.ImageId!.Value).ToList() })
            .ToListAsync(ct);

        foreach (var group in imagesByFolder)
        {
            if (!folders.TryGetValue(group.FolderId, out var folder)) continue;

            var gallery = new Gallery
            {
                Title = Path.GetFileName(folder.Path) ?? folder.Path,
                FolderId = folder.Id,
            };

            foreach (var imageId in group.ImageIds)
            {
                gallery.ImageGalleries.Add(new ImageGallery { ImageId = imageId, Gallery = gallery });
            }

            db.Galleries.Add(gallery);
            logger.LogDebug("Created folder gallery for: {Path} with {Count} images", folder.Path, group.ImageIds.Count);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Folder> EnsureFolderAsync(CoveContext db, string dirPath, CancellationToken ct)
    {
        dirPath = NormalizeStoredFolderPath(dirPath);
        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Path == dirPath, ct);
        if (folder != null) return folder;

        var folderLock = FolderCreationLocks.GetOrAdd(dirPath, static _ => new SemaphoreSlim(1, 1));
        await folderLock.WaitAsync(ct);
        try
        {
            folder = await db.Folders.FirstOrDefaultAsync(f => f.Path == dirPath, ct);
            if (folder != null) return folder;

            folder = new Folder
            {
                Path = dirPath,
                ModTime = Directory.GetLastWriteTimeUtc(dirPath)
            };

            // Link parent folder if path has a parent
            var parentDir = GetParentStoredFolderPath(dirPath);
            if (!string.IsNullOrEmpty(parentDir) && parentDir != dirPath)
            {
                var parentFolder = await db.Folders.FirstOrDefaultAsync(f => f.Path == parentDir, ct);
                if (parentFolder != null)
                    folder.ParentFolderId = parentFolder.Id;
            }

            db.Folders.Add(folder);
            try
            {
                await db.SaveChangesAsync(ct);
                return folder;
            }
            catch (DbUpdateException)
            {
                db.Entry(folder).State = EntityState.Detached;
                var existing = await db.Folders.FirstOrDefaultAsync(f => f.Path == dirPath, ct);
                if (existing != null)
                    return existing;

                throw;
            }
        }
        finally
        {
            folderLock.Release();
        }
    }

    private async Task<VideoFile> ProcessVideoFileAsync(CoveContext db, string path, int? sceneId, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        var dirPath = NormalizeStoredFolderPath(Path.GetDirectoryName(path) ?? path);
        var folder = await EnsureFolderAsync(db, dirPath, ct);

        var basename = Path.GetFileName(path);
        var existing = await db.VideoFiles
            .FirstOrDefaultAsync(f => f.ParentFolderId == folder.Id && f.Basename == basename, ct);

        Scene? targetScene = null;
        if (sceneId.HasValue)
        {
            targetScene = await db.Scenes.FirstOrDefaultAsync(s => s.Id == sceneId.Value, ct)
                ?? throw new InvalidOperationException($"Scene {sceneId.Value} was not found for downloaded media import");

            if (string.IsNullOrWhiteSpace(targetScene.Title))
                targetScene.Title = Path.GetFileNameWithoutExtension(path);
        }

        if (existing != null)
        {
            existing.Size = fileInfo.Length;
            existing.ModTime = fileInfo.LastWriteTimeUtc;

            if (targetScene != null)
                existing.SceneId = targetScene.Id;

            // Re-probe if metadata is missing (e.g., FFprobe wasn't available during initial scan)
            if (NeedsVideoMetadataProbe(existing))
            {
                await ProbeVideoAsync(existing, path, ct);
            }

            return existing;
        }

        // Create video file entry
        var videoFile = new VideoFile
        {
            Basename = basename,
            ParentFolderId = folder.Id,
            Size = fileInfo.Length,
            ModTime = fileInfo.LastWriteTimeUtc,
            Format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
            SceneId = targetScene?.Id
        };

        if (targetScene == null)
        {
            var scene = new Scene
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Files = [videoFile]
            };

            db.Scenes.Add(scene);
        }
        else
        {
            db.VideoFiles.Add(videoFile);
        }

        await EnrichVideoFileAsync(videoFile, path, ct);

        logger.LogDebug("Added scene file for: {Path}", path);
        return videoFile;
    }

    private async Task EnrichVideoFileAsync(VideoFile videoFile, string path, CancellationToken ct)
    {
        // Probe with FFprobe for metadata
        await ProbeVideoAsync(videoFile, path, ct);

        // Compute oshash fingerprint
        var oshash = await ComputeOshashAsync(path, ct);
        if (oshash != null)
        {
            videoFile.Fingerprints.Add(new FileFingerprint
            {
                Type = "oshash",
                Value = oshash
            });
        }

        if (config.CalculateMd5)
        {
            var md5 = await fingerprintService.ComputeMd5Async(path, ct);
            if (!string.IsNullOrWhiteSpace(md5))
            {
                videoFile.Fingerprints.Add(new FileFingerprint
                {
                    Type = "md5",
                    Value = md5,
                });
            }
        }

        SyncVideoCaptions(videoFile, path);
    }

    private static void SyncVideoCaptions(VideoFile videoFile, string path)
    {
        var sidecars = DiscoverCaptionSidecars(path);
        var expected = sidecars.ToDictionary(item => item.Filename, StringComparer.OrdinalIgnoreCase);

        foreach (var existing in videoFile.Captions.ToList())
        {
            if (!expected.TryGetValue(existing.Filename, out var sidecar))
            {
                videoFile.Captions.Remove(existing);
                continue;
            }

            existing.LanguageCode = sidecar.LanguageCode;
            existing.CaptionType = sidecar.CaptionType;
        }

        var existingFilenames = videoFile.Captions
            .Select(item => item.Filename)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sidecar in sidecars)
        {
            if (existingFilenames.Contains(sidecar.Filename))
                continue;

            videoFile.Captions.Add(new VideoCaption
            {
                LanguageCode = sidecar.LanguageCode,
                CaptionType = sidecar.CaptionType,
                Filename = sidecar.Filename,
            });
        }
    }

    private static List<CaptionSidecar> DiscoverCaptionSidecars(string path)
    {
        var videoDir = Path.GetDirectoryName(path);
        if (videoDir == null || !Directory.Exists(videoDir))
            return [];

        var videoBaseName = Path.GetFileNameWithoutExtension(path);
        return Directory.EnumerateFiles(videoDir)
            .Where(f => f.StartsWith(Path.Combine(videoDir, videoBaseName), StringComparison.OrdinalIgnoreCase)
                && (f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)))
            .Select(captionFile =>
            {
                var captionFilename = Path.GetFileName(captionFile);
                var ext = Path.GetExtension(captionFile).TrimStart('.').ToLowerInvariant();
                var langCode = "00";
                var nameWithoutExt = Path.GetFileNameWithoutExtension(captionFile);
                var parts = nameWithoutExt.Split('.');
                if (parts.Length >= 2)
                {
                    var candidate = parts[^1];
                    if (candidate.Length is 2 or 3)
                        langCode = candidate.ToLowerInvariant();
                }

                return new CaptionSidecar(captionFilename, langCode, ext);
            })
            .OrderBy(item => item.Filename, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Image> ProcessImageFileAsync(CoveContext db, string path, int? imageId, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        var dirPath = NormalizeStoredFolderPath(Path.GetDirectoryName(path) ?? path);
        var folder = await EnsureFolderAsync(db, dirPath, ct);

        var basename = Path.GetFileName(path);
        var existing = await db.ImageFiles
            .Include(f => f.Image)
            .FirstOrDefaultAsync(f => f.ParentFolderId == folder.Id && f.Basename == basename, ct);

        if (existing != null)
        {
            existing.Size = fileInfo.Length;
            existing.ModTime = fileInfo.LastWriteTimeUtc;
            return existing.Image ?? throw new InvalidOperationException($"Image file {path} is not attached to an image");
        }

        var imageFile = new ImageFile
        {
            Basename = basename,
            ParentFolderId = folder.Id,
            Size = fileInfo.Length,
            ModTime = fileInfo.LastWriteTimeUtc,
            Format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant()
        };

        Image image;
        if (imageId.HasValue)
        {
            image = await db.Images
                .Include(item => item.Files)
                .FirstOrDefaultAsync(item => item.Id == imageId.Value, ct)
                ?? throw new InvalidOperationException($"Image {imageId.Value} was not found for downloaded media import");

            if (string.IsNullOrWhiteSpace(image.Title))
                image.Title = Path.GetFileNameWithoutExtension(path);

            image.Files.Add(imageFile);
        }
        else
        {
            image = new Image
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Files = [imageFile]
            };

            db.Images.Add(image);
        }

        if (config.CalculateMd5)
        {
            var md5 = await fingerprintService.ComputeMd5Async(path, ct);
            if (!string.IsNullOrWhiteSpace(md5))
            {
                imageFile.Fingerprints.Add(new FileFingerprint
                {
                    Type = "md5",
                    Value = md5,
                });
            }
        }

        logger.LogDebug("Added image for: {Path}", path);
        return image;
    }

    private async Task<Gallery> ProcessGalleryFileAsync(CoveContext db, string path, int? galleryId, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        var dirPath = NormalizeStoredFolderPath(Path.GetDirectoryName(path) ?? path);
        var folder = await EnsureFolderAsync(db, dirPath, ct);

        var basename = Path.GetFileName(path);
        var existing = await db.Set<GalleryFile>()
            .Include(gf => gf.Gallery)
            .ThenInclude(g => g!.ImageGalleries)
            .FirstOrDefaultAsync(f => f.ParentFolderId == folder.Id && f.Basename == basename, ct);

        // If gallery exists and already has images, skip re-processing
        if (existing?.Gallery?.ImageGalleries.Count > 0)
        {
            logger.LogDebug("Gallery already processed with {Count} images: {Path}",
                existing.Gallery.ImageGalleries.Count, path);
            return existing.Gallery;
        }

        // Create or update the gallery file entry
        GalleryFile galleryFile;
        Gallery gallery;

        if (existing != null)
        {
            // Update existing file metadata
            galleryFile = existing;
            galleryFile.Size = fileInfo.Length;
            galleryFile.ModTime = fileInfo.LastWriteTimeUtc;
            gallery = existing.Gallery!;
        }
        else
        {
            galleryFile = new GalleryFile
            {
                Basename = basename,
                ParentFolderId = folder.Id,
                Size = fileInfo.Length,
                ModTime = fileInfo.LastWriteTimeUtc
            };

            if (galleryId.HasValue)
            {
                gallery = await db.Galleries
                    .Include(item => item.Files)
                    .Include(item => item.ImageGalleries)
                    .FirstOrDefaultAsync(item => item.Id == galleryId.Value, ct)
                    ?? throw new InvalidOperationException($"Gallery {galleryId.Value} was not found for downloaded media import");

                if (string.IsNullOrWhiteSpace(gallery.Title))
                    gallery.Title = Path.GetFileNameWithoutExtension(path);

                gallery.Files.Add(galleryFile);
            }
            else
            {
                gallery = new Gallery
                {
                    Title = Path.GetFileNameWithoutExtension(path),
                    Files = [galleryFile]
                };

                db.Galleries.Add(gallery);
            }
        }

        // Save to get the GalleryFile ID (needed for ZipFileId on images)
        await db.SaveChangesAsync(ct);

        // Now extract images from the zip file
        try
        {
            // Get all images from the zip, sorted by path
            var imageEntries = await zipGalleryReader.GetImageEntriesAsync(path, ct);

            if (imageEntries.Count == 0)
            {
                logger.LogWarning("No images found in gallery zip: {Path}", path);
                return gallery;
            }

            logger.LogDebug("Found {Count} images in gallery: {Path}", imageEntries.Count, path);

            // Create a virtual folder for this zip's contents
            // This ensures images from different zips don't conflict on the unique constraint (ParentFolderId + Basename)
            var virtualFolderPath = $"{path}#virtual";
            var virtualFolder = await db.Folders.FirstOrDefaultAsync(f => f.Path == virtualFolderPath, ct);
            if (virtualFolder == null)
            {
                virtualFolder = new Folder { Path = virtualFolderPath };
                db.Folders.Add(virtualFolder);
                await db.SaveChangesAsync(ct);
            }

            // Create Image entities for each image in the zip
            foreach (var entry in imageEntries)
            {
                // Create ImageFile record representing the image within the zip
                // Use FullName to preserve the internal zip path structure and avoid duplicate basenames
                var imageFile = new ImageFile
                {
                    Basename = entry.FullName,  // Use full internal path to avoid collisions
                    ParentFolderId = virtualFolder.Id,  // Use virtual folder specific to this zip
                    ZipFileId = galleryFile.Id,  // Link to parent zip file
                    Size = entry.Length,
                    ModTime = entry.LastWriteTime.UtcDateTime,
                    Format = Path.GetExtension(entry.Name).TrimStart('.').ToLowerInvariant(),
                    // TODO: Extract dimensions using image processing library
                    Width = 0,
                    Height = 0
                };

                // Create Image entity
                var image = new Image
                {
                    Title = Path.GetFileNameWithoutExtension(entry.Name),
                    Files = [imageFile]
                };

                db.Images.Add(image);

                // Link image to gallery via junction table
                // Note: We'll add this after the image is saved and has an ID
                gallery.ImageGalleries.Add(new ImageGallery
                {
                    Image = image,
                    Gallery = gallery
                });
            }

            // Save all images and their gallery associations
            await db.SaveChangesAsync(ct);

            logger.LogDebug("Added gallery with {Count} images: {Path}", imageEntries.Count, path);
        }
        catch (FileNotFoundException)
        {
            logger.LogError("Zip file not found (may have been moved/deleted): {Path}", path);
        }
        catch (InvalidDataException ex)
        {
            logger.LogError("Invalid or corrupt zip file: {Path} - {Error}", path, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing gallery zip file: {Path}", path);
        }

        return gallery;
    }

    private async Task<Audio> ProcessAudioFileAsync(CoveContext db, string path, int? audioId, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        var dirPath = NormalizeStoredFolderPath(Path.GetDirectoryName(path) ?? path);
        var folder = await EnsureFolderAsync(db, dirPath, ct);

        var basename = Path.GetFileName(path);
        var existing = await db.AudioFiles
            .Include(file => file.Fingerprints)
            .Include(file => file.Audio)
            .ThenInclude(audio => audio!.Files)
            .FirstOrDefaultAsync(file => file.ParentFolderId == folder.Id && file.Basename == basename, ct);

        if (existing != null)
        {
            existing.Size = fileInfo.Length;
            existing.ModTime = fileInfo.LastWriteTimeUtc;
            existing.Path = BaseFileEntity.ComputePath(dirPath, basename);

            var existingAudio = existing.Audio ?? throw new InvalidOperationException($"Audio file {path} is not attached to an audio entity");
            await EnrichAudioFileAsync(existingAudio, existing, path, ct);
            RefreshAudioSummary(existingAudio);
            return existingAudio;
        }

        var audioFile = new AudioFile
        {
            Basename = basename,
            ParentFolderId = folder.Id,
            ParentFolder = folder,
            Path = BaseFileEntity.ComputePath(dirPath, basename),
            Size = fileInfo.Length,
            ModTime = fileInfo.LastWriteTimeUtc,
            Format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
        };

        Audio audio;
        if (audioId.HasValue)
        {
            audio = await db.Audios
                .Include(item => item.Files)
                .FirstOrDefaultAsync(item => item.Id == audioId.Value, ct)
                ?? throw new InvalidOperationException($"Audio {audioId.Value} was not found for downloaded media import");

            audio.Files.Add(audioFile);
        }
        else
        {
            audio = new Audio
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Files = [audioFile],
            };

            db.Audios.Add(audio);
        }

        await EnrichAudioFileAsync(audio, audioFile, path, ct);
        RefreshAudioSummary(audio);

        logger.LogDebug("Added audio for: {Path}", path);
        return audio;
    }

    private async Task<TextDocument> ProcessTextFileAsync(CoveContext db, string path, int? textDocumentId, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        var dirPath = NormalizeStoredFolderPath(Path.GetDirectoryName(path) ?? path);
        var folder = await EnsureFolderAsync(db, dirPath, ct);

        var basename = Path.GetFileName(path);
        var existing = await db.TextFiles
            .Include(file => file.Fingerprints)
            .Include(file => file.TextDocument)
            .ThenInclude(text => text!.Files)
            .FirstOrDefaultAsync(file => file.ParentFolderId == folder.Id && file.Basename == basename, ct);

        if (existing != null)
        {
            existing.Size = fileInfo.Length;
            existing.ModTime = fileInfo.LastWriteTimeUtc;
            existing.Path = BaseFileEntity.ComputePath(dirPath, basename);

            var existingDocument = existing.TextDocument ?? throw new InvalidOperationException($"Text file {path} is not attached to a text document");
            await EnrichTextFileAsync(existingDocument, existing, path, ct);
            RefreshTextSummary(existingDocument);
            return existingDocument;
        }

        var textFile = new TextFile
        {
            Basename = basename,
            ParentFolderId = folder.Id,
            ParentFolder = folder,
            Path = BaseFileEntity.ComputePath(dirPath, basename),
            Size = fileInfo.Length,
            ModTime = fileInfo.LastWriteTimeUtc,
            Format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
        };

        TextDocument textDocument;
        if (textDocumentId.HasValue)
        {
            textDocument = await db.TextDocuments
                .Include(item => item.Files)
                .FirstOrDefaultAsync(item => item.Id == textDocumentId.Value, ct)
                ?? throw new InvalidOperationException($"Text document {textDocumentId.Value} was not found for downloaded media import");

            textDocument.Files.Add(textFile);
        }
        else
        {
            textDocument = new TextDocument
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Files = [textFile],
            };

            db.TextDocuments.Add(textDocument);
        }

        await EnrichTextFileAsync(textDocument, textFile, path, ct);
        RefreshTextSummary(textDocument);

        logger.LogDebug("Added text document for: {Path}", path);
        return textDocument;
    }

    private async Task EnrichAudioFileAsync(Audio audio, AudioFile audioFile, string path, CancellationToken ct)
    {
        var metadata = await ProbeAudioAsync(audioFile, path, ct);
        var fallbackTitle = Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrWhiteSpace(audio.Title) || string.Equals(audio.Title, fallbackTitle, StringComparison.OrdinalIgnoreCase))
            audio.Title = metadata.Title ?? fallbackTitle;

        if (config.CalculateMd5)
        {
            var md5 = await fingerprintService.ComputeMd5Async(path, ct);
            if (!string.IsNullOrWhiteSpace(md5))
            {
                UpsertFingerprint(audioFile, "md5", md5);
            }
        }
    }

    private async Task EnrichTextFileAsync(TextDocument textDocument, TextFile textFile, string path, CancellationToken ct)
    {
        try
        {
            var metadata = await textExtractionService.ExtractMetadataAsync(path, ct);
            var fallbackTitle = Path.GetFileNameWithoutExtension(path);
            textFile.PageCount = metadata.PageCount;
            textFile.WordCount = metadata.WordCount;
            textFile.ExcerptText = metadata.ExcerptText;

            if (string.IsNullOrWhiteSpace(textDocument.Title) || string.Equals(textDocument.Title, fallbackTitle, StringComparison.OrdinalIgnoreCase))
                textDocument.Title = metadata.Title ?? fallbackTitle;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text metadata for {Path}", path);
        }

        if (config.CalculateMd5)
        {
            var md5 = await fingerprintService.ComputeMd5Async(path, ct);
            if (!string.IsNullOrWhiteSpace(md5))
            {
                UpsertFingerprint(textFile, "md5", md5);
            }
        }
    }

    private async Task<AudioProbeMetadata> ProbeAudioAsync(AudioFile audioFile, string path, CancellationToken ct)
    {
        var ffprobePath = FindFfprobe();
        if (ffprobePath == null)
        {
            logger.LogDebug("FFprobe not found, skipping audio metadata probe for {Path}", path);
            return new AudioProbeMetadata(null);
        }

        audioFile.HasVideoTrack = false;
        audioFile.AudioCodec = string.Empty;
        audioFile.SampleRate = null;
        audioFile.Channels = null;

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            var json = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0 || string.IsNullOrEmpty(json))
                return new AudioProbeMetadata(null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? title = null;
            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var dur))
                {
                    if (double.TryParse(dur.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration))
                        audioFile.Duration = duration;
                }
                if (format.TryGetProperty("bit_rate", out var br))
                {
                    if (long.TryParse(br.GetString(), out var bitrate))
                        audioFile.BitRate = bitrate;
                }
                if (format.TryGetProperty("tags", out var tags))
                {
                    if (tags.TryGetProperty("title", out var titleProp))
                        title = titleProp.GetString();
                }
            }

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var typeProp) ? typeProp.GetString() : null;
                    if (codecType == "audio" && string.IsNullOrWhiteSpace(audioFile.AudioCodec))
                    {
                        if (stream.TryGetProperty("codec_name", out var codecName))
                            audioFile.AudioCodec = codecName.GetString() ?? string.Empty;
                        if (stream.TryGetProperty("sample_rate", out var sampleRateProp))
                        {
                            if (int.TryParse(sampleRateProp.GetString(), out var sampleRate))
                                audioFile.SampleRate = sampleRate;
                        }
                        if (stream.TryGetProperty("channels", out var channelsProp) && channelsProp.TryGetInt32(out var channels))
                            audioFile.Channels = channels;
                        if (audioFile.BitRate == 0 && stream.TryGetProperty("bit_rate", out var streamBitrateProp))
                        {
                            if (long.TryParse(streamBitrateProp.GetString(), out var streamBitrate))
                                audioFile.BitRate = streamBitrate;
                        }
                    }
                    else if (codecType == "video")
                    {
                        audioFile.HasVideoTrack = true;
                    }
                }
            }

            return new AudioProbeMetadata(title);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "FFprobe failed for audio {Path}", path);
            return new AudioProbeMetadata(null);
        }
    }

    private static void RefreshAudioSummary(Audio audio)
    {
        var files = audio.Files.ToList();
        audio.FileCount = files.Count;
        if (files.Count == 0)
        {
            audio.MaxDuration = 0;
            audio.MaxBitRate = 0;
            audio.MaxFileSize = 0;
            audio.MaxFileModTime = null;
            audio.MinPath = null;
            audio.MaxPath = null;
            audio.FileSearchText = null;
            audio.HasVideoFiles = false;
            return;
        }

        var paths = files
            .Select(file => string.IsNullOrWhiteSpace(file.Path) ? BaseFileEntity.ComputePath(file.ParentFolder?.Path, file.Basename) : file.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        audio.MaxDuration = files.Max(file => file.Duration);
        audio.MaxBitRate = files.Max(file => file.BitRate);
        audio.MaxFileSize = files.Max(file => file.Size);
        audio.MaxFileModTime = files.Max(file => (DateTime?)file.ModTime);
        audio.MinPath = paths.FirstOrDefault();
        audio.MaxPath = paths.LastOrDefault();
        audio.FileSearchText = BuildFileSearchText(paths);
        audio.HasVideoFiles = files.Any(file => file.HasVideoTrack);
    }

    private static void RefreshTextSummary(TextDocument textDocument)
    {
        var files = textDocument.Files.ToList();
        textDocument.FileCount = files.Count;
        if (files.Count == 0)
        {
            textDocument.MaxWordCount = null;
            textDocument.MaxPageCount = null;
            textDocument.MaxFileSize = 0;
            textDocument.MaxFileModTime = null;
            textDocument.MinPath = null;
            textDocument.MaxPath = null;
            textDocument.FileSearchText = null;
            return;
        }

        var paths = files
            .Select(file => string.IsNullOrWhiteSpace(file.Path) ? BaseFileEntity.ComputePath(file.ParentFolder?.Path, file.Basename) : file.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        textDocument.MaxWordCount = files.Max(file => file.WordCount);
        textDocument.MaxPageCount = files.Max(file => file.PageCount);
        textDocument.MaxFileSize = files.Max(file => file.Size);
        textDocument.MaxFileModTime = files.Max(file => (DateTime?)file.ModTime);
        textDocument.MinPath = paths.FirstOrDefault();
        textDocument.MaxPath = paths.LastOrDefault();
        textDocument.FileSearchText = BuildFileSearchText(paths);
    }

    private static string? BuildFileSearchText(IEnumerable<string> paths)
    {
        var values = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/').Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? null : string.Join('\n', values);
    }

    private static void UpsertFingerprint(BaseFileEntity file, string type, string value)
    {
        var existing = file.Fingerprints.FirstOrDefault(fingerprint => string.Equals(fingerprint.Type, type, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Value = value;
            return;
        }

        file.Fingerprints.Add(new FileFingerprint
        {
            Type = type,
            Value = value,
        });
    }

    /// <summary>
    /// Compute OpenSubtitles hash (oshash) for a video file.
    /// Standard oshash algorithm.
    /// </summary>
    private static async Task<string?> ComputeOshashAsync(string path, CancellationToken ct)
    {
        const int chunkSize = 65536; // 64KB
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, useAsync: true);
            var fileSize = stream.Length;
            if (fileSize < chunkSize) return null;

            ulong hash = (ulong)fileSize;
            var buf = new byte[chunkSize];

            // Hash first 64KB
            await stream.ReadExactlyAsync(buf, ct);
            for (int i = 0; i < chunkSize; i += 8)
                hash += BitConverter.ToUInt64(buf, i);

            // Hash last 64KB
            stream.Seek(-chunkSize, SeekOrigin.End);
            await stream.ReadExactlyAsync(buf, ct);
            for (int i = 0; i < chunkSize; i += 8)
                hash += BitConverter.ToUInt64(buf, i);

            return hash.ToString("x16");
        }
        catch
        {
            return null;
        }
    }

    internal static bool NeedsVideoMetadataProbe(VideoFile videoFile)
    {
        return videoFile.Width <= 0 || videoFile.Height <= 0 || videoFile.Duration <= 0;
    }

    private async Task ProbeVideoAsync(VideoFile videoFile, string path, CancellationToken ct)
    {
        var ffprobePath = FindFfprobe();
        if (ffprobePath == null)
        {
            logger.LogDebug("FFprobe not found, skipping metadata probe for {Path}", path);
            return;
        }

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var json = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0 || string.IsNullOrEmpty(json)) return;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract format duration
            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration))
                    videoFile.Duration = duration;
                if (format.TryGetProperty("bit_rate", out var br) && long.TryParse(br.GetString(), out var bitrate))
                    videoFile.BitRate = bitrate;
            }

            // Extract stream info
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct2) ? ct2.GetString() : null;
                    if (codecType == "video" && videoFile.Width == 0)
                    {
                        if (stream.TryGetProperty("width", out var w)) videoFile.Width = w.GetInt32();
                        if (stream.TryGetProperty("height", out var h)) videoFile.Height = h.GetInt32();
                        if (stream.TryGetProperty("codec_name", out var cn)) videoFile.VideoCodec = cn.GetString() ?? "";
                        if (stream.TryGetProperty("r_frame_rate", out var rfr))
                        {
                            var frs = rfr.GetString() ?? "";
                            var frParts = frs.Split('/');
                            if (frParts.Length == 2 && double.TryParse(frParts[0], out var num) && double.TryParse(frParts[1], out var den) && den > 0)
                                videoFile.FrameRate = num / den;
                        }
                    }
                    else if (codecType == "audio" && string.IsNullOrEmpty(videoFile.AudioCodec))
                    {
                        if (stream.TryGetProperty("codec_name", out var acn)) videoFile.AudioCodec = acn.GetString() ?? "";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "FFprobe failed for {Path}", path);
        }
    }

    private string? FindFfprobe()
    {
        // Check configured FFmpeg path directory for ffprobe
        if (!string.IsNullOrEmpty(config.FfmpegPath))
        {
            var dir = Path.GetDirectoryName(config.FfmpegPath);
            if (dir != null)
            {
                var probe = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
                if (File.Exists(probe)) return probe;
            }
        }

        // Search PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var probe = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (File.Exists(probe)) return probe;
        }

        return null;
    }

    private static bool IsExcluded(string path, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private IEnumerable<string> EnumerateFilesSafely(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                logger.LogWarning(ex, "Skipping files in unreadable scan directory: {Path}", directory);
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(directory).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                logger.LogWarning(ex, "Skipping nested scan directories under unreadable directory: {Path}", directory);
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                try
                {
                    if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
                {
                    logger.LogWarning(ex, "Skipping unreadable scan directory: {Path}", subdirectory);
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }

    private static bool IsExcludedByFolderIgnore(string path, string rootPath, Dictionary<string, List<IgnoreRule>> ruleCache)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return false;

        var fullPath = NormalizePath(path);
        var root = Directory.Exists(rootPath) ? NormalizePath(rootPath) : NormalizePath(Path.GetDirectoryName(rootPath) ?? rootPath);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        var ancestors = new Stack<string>();
        for (var current = NormalizePath(directory); !string.IsNullOrWhiteSpace(current) && IsPathWithin(current, root); current = Path.GetDirectoryName(current))
            ancestors.Push(current);

        bool ignored = false;
        while (ancestors.Count > 0)
        {
            var ruleDirectory = ancestors.Pop();
            foreach (var rule in GetIgnoreRules(ruleDirectory, ruleCache))
            {
                var relativePath = Path.GetRelativePath(ruleDirectory, fullPath).Replace('\\', '/');
                if (IgnoreRuleMatches(rule.Pattern, relativePath, Path.GetFileName(fullPath)))
                    ignored = !rule.Negated;
            }
        }

        return ignored;
    }

    private static List<IgnoreRule> GetIgnoreRules(string directory, Dictionary<string, List<IgnoreRule>> ruleCache)
    {
        if (ruleCache.TryGetValue(directory, out var cached))
            return cached;

        var rules = new List<IgnoreRule>();
        foreach (var fileName in FolderIgnoreFileNames)
        {
            var ignoreFile = Path.Combine(directory, fileName);
            if (!File.Exists(ignoreFile))
                continue;

            foreach (var line in File.ReadLines(ignoreFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                var negated = trimmed.StartsWith('!');
                var pattern = (negated ? trimmed[1..] : trimmed).Trim().Replace('\\', '/');
                if (pattern.Length > 0)
                    rules.Add(new IgnoreRule(pattern, negated));
            }
        }

        ruleCache[directory] = rules;
        return rules;
    }

    private static bool IgnoreRuleMatches(string pattern, string relativePath, string fileName)
    {
        var normalizedPattern = pattern.TrimStart('/');
        var directoryPattern = normalizedPattern.EndsWith('/');
        if (directoryPattern)
            normalizedPattern = normalizedPattern.TrimEnd('/');

        if (normalizedPattern.Length == 0)
            return false;

        if (directoryPattern)
        {
            return relativePath.StartsWith(normalizedPattern + '/', StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains('/' + normalizedPattern + '/', StringComparison.OrdinalIgnoreCase);
        }

        if (normalizedPattern.Contains('/'))
            return FileSystemName.MatchesSimpleExpression(normalizedPattern, relativePath, ignoreCase: true);

        return FileSystemName.MatchesSimpleExpression(normalizedPattern, fileName, ignoreCase: true)
            || relativePath.Split('/').Any(segment => FileSystemName.MatchesSimpleExpression(normalizedPattern, segment, ignoreCase: true));
    }

    private static bool HasForceGalleryHints(IEnumerable<DiscoveredFile> files)
    {
        return files
            .Select(file => Path.GetDirectoryName(file.Path))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(directory => File.Exists(Path.Combine(directory!, ".forcegallery")));
    }

    private static bool ShouldCreateFolderGallery(string folderPath, bool createAllEligibleFolders)
    {
        if (File.Exists(Path.Combine(folderPath, ".nogallery")))
            return false;

        return createAllEligibleFolders || File.Exists(Path.Combine(folderPath, ".forcegallery"));
    }

    private static List<ScanTarget> ResolveScanTargets(CoveConfiguration cfg, List<string>? selectedPaths)
    {
        if (selectedPaths == null)
        {
            return cfg.CovePaths
                .Select(path => new ScanTarget(NormalizePath(path.Path), path.ExcludeVideo, path.ExcludeImage, path.ExcludeAudio, path.ExcludeText, false))
                .ToList();
        }

        var targets = new List<ScanTarget>();
        foreach (var selectedPath in selectedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matchingConfig = cfg.CovePaths
                .Select(path => new { Config = path, NormalizedPath = NormalizePath(path.Path) })
                .Where(x => IsPathWithin(selectedPath, x.NormalizedPath) || IsPathWithin(x.NormalizedPath, selectedPath))
                .OrderByDescending(x => x.NormalizedPath.Length)
                .Select(x => x.Config)
                .FirstOrDefault();

            var excludeVideo = matchingConfig?.ExcludeVideo ?? false;
            var excludeImage = matchingConfig?.ExcludeImage ?? false;
            var excludeAudio = matchingConfig?.ExcludeAudio ?? false;
            var excludeText = matchingConfig?.ExcludeText ?? false;
            var isFile = File.Exists(selectedPath);

            if (!isFile && !Directory.Exists(selectedPath))
            {
                continue;
            }

            targets.Add(new ScanTarget(selectedPath, excludeVideo, excludeImage, excludeAudio, excludeText, isFile));
        }

        return targets;
    }

    private static bool IsPathWithin(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static string NormalizeStoredFolderPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var normalized = !string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalized.Replace('\\', '/');
    }

    private static string? GetParentStoredFolderPath(string storedPath)
    {
        var nativePath = storedPath.Replace('/', Path.DirectorySeparatorChar);
        var parentPath = Path.GetDirectoryName(nativePath);
        return string.IsNullOrWhiteSpace(parentPath) ? null : NormalizeStoredFolderPath(parentPath);
    }

    private static readonly string[] FolderIgnoreFileNames = [".coveignore", ".stashignore"];

    private record CaptionSidecar(string Filename, string LanguageCode, string CaptionType);
    private sealed record AudioProbeMetadata(string? Title);
    private record DiscoveredFile(string Path, string Extension);
    private record IgnoreRule(string Pattern, bool Negated);
    private record ScanTarget(string Path, bool ExcludeVideo, bool ExcludeImage, bool ExcludeAudio, bool ExcludeText, bool IsFile);

    /// <summary>
    /// Wraps a progress reporter to map 0-100% into a sub-range of the parent progress.
    /// Used to give extension scan participants their own slice of the overall progress bar.
    /// </summary>
    private sealed class ScopedProgress(Cove.Core.Interfaces.IJobProgress parent, double rangeStart, double rangeEnd) : ExtJobProgress
    {
        public void Report(double percent, string? message = null)
        {
            var mapped = rangeStart + (percent / 100.0) * (rangeEnd - rangeStart);
            parent.Report(mapped * 100, message);
        }
    }
}
