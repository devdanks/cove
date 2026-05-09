using System.Collections.Concurrent;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Plugins;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public sealed record DownloaderBatchExecutionSummary(
    int TotalCount,
    int SucceededCount,
    int SkippedCount,
    int FailedCount,
    string? FollowUpJobId,
    IReadOnlyList<string> Issues);

public sealed record DownloaderBatchPreflightResult(
    IReadOnlyList<DownloaderBatchItemDto> ItemsToQueue,
    IReadOnlyList<DownloaderBatchStartIssueDto> Issues);

public class DownloaderService(
    ExtensionManager extensionManager,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    CoveConfiguration config,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<DownloaderService> logger)
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "cove", "downloaders");
    private readonly Lock _downloadSlotLock = new();
    private readonly Lock _libraryMoveLock = new();
    private SemaphoreSlim? _downloadSlots;
    private int _downloadSlotCapacity;

    public IReadOnlyList<DownloaderDescriptorDto> GetDownloaders()
    {
        Directory.CreateDirectory(_tempRoot);

        return extensionManager.GetDownloaderProviders()
            .SelectMany(provider => provider.GetDownloaders())
            .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
    }

    public async Task<IReadOnlyList<DownloaderMatchDto>> MatchUrlAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return [];

        var results = new List<DownloaderMatchDto>();
        foreach (var provider in extensionManager.GetDownloaderProviders())
        {
            try
            {
                var match = await provider.MatchAsync(url, ct);
                if (match == null)
                    continue;

                var descriptor = provider.GetDownloaders().FirstOrDefault(item => string.Equals(item.Id, match.DownloaderId, StringComparison.OrdinalIgnoreCase));
                if (descriptor == null)
                {
                    logger.LogWarning("Downloader provider {ProviderId} returned unknown downloader id {DownloaderId}", provider.Id, match.DownloaderId);
                    continue;
                }

                results.Add(ToDto(descriptor, match));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Downloader provider {ProviderId} failed URL match for {Url}", provider.Id, url);
            }
        }

        return results
            .OrderBy(result => result.DownloaderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.DownloaderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DownloaderResult?> DownloadAsync(DownloaderRequest request, Cove.Core.Interfaces.IJobProgress? progress, CancellationToken ct)
    {
        var registration = extensionManager.GetDownloaderProviders()
            .Select(provider => new
            {
                Provider = provider,
                Descriptor = provider.GetDownloaders().FirstOrDefault(descriptor => string.Equals(descriptor.Id, request.DownloaderId, StringComparison.OrdinalIgnoreCase))
            })
            .FirstOrDefault(item => item.Descriptor != null);

        if (registration?.Descriptor == null)
            throw new InvalidOperationException($"Downloader not found: {request.DownloaderId}");

        Directory.CreateDirectory(_tempRoot);
        var tempDirectory = Path.Combine(_tempRoot, SanitizePathSegment(registration.Descriptor.Id), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        var host = new DownloaderHost(tempDirectory, httpClientFactory, loggerFactory, progress);
        using var downloadSlotLease = await AcquireDownloadSlotAsync(progress, ct);
        var result = await registration.Provider.DownloadAsync(request, host, ct);
        if (result == null)
            return null;

        var localPath = Path.IsPathRooted(result.LocalPath)
            ? result.LocalPath
            : Path.GetFullPath(Path.Combine(tempDirectory, result.LocalPath));

        if (!File.Exists(localPath))
            throw new InvalidOperationException($"Downloader {registration.Descriptor.Id} completed without producing a file at {localPath}");

        return result with { LocalPath = localPath };
    }

    public async Task<(DownloaderResult? Result, int? ImportedEntityId)> DownloadAndIngestAsync(
        DownloaderRequest request,
        int? entityId,
        Cove.Core.Interfaces.IJobProgress? progress,
        CancellationToken ct,
        bool autoApplyMetadata = false,
        bool allowDuplicateDownload = false)
    {
        if (!allowDuplicateDownload)
            await EnsureDownloadAllowedAsync(request, entityId, ct);

        var result = await DownloadAsync(request, progress, ct);
        if (result == null)
            return (null, null);

        var libraryPath = MoveDownloadedFileToLibrary(result, request.Entity, request.DownloaderId, request.Url);
        using var scope = serviceScopeFactory.CreateScope();
        var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();

        var importedEntityId = request.Entity switch
        {
            DownloaderEntity.Scene => await ImportSceneAsync(scanService, libraryPath, entityId, progress, ct),
            DownloaderEntity.Image => await ImportImageAsync(scanService, libraryPath, entityId, progress, ct),
            DownloaderEntity.Gallery => await ImportGalleryAsync(scanService, libraryPath, entityId, progress, ct),
            _ => entityId,
        };

        if (autoApplyMetadata
            && request.Entity == DownloaderEntity.Scene
            && importedEntityId.HasValue)
        {
            var metadata = result.InlineSceneMetadata;
            if (metadata == null)
            {
                progress?.Report(0.97d, "Looking up scraper for downloaded URL...");
                var scraperService = scope.ServiceProvider.GetRequiredService<ScraperService>();
                var scraped = await scraperService.ScrapeUrlAutoAsync(request.Url, "scene", ct);
                if (scraped != null)
                    metadata = ConvertScrapeResultToSceneMetadata(scraped.Value.Result, request.Url);
            }

            if (metadata != null)
            {
                progress?.Report(0.99d, "Applying downloaded scene metadata...");
                var metadataApplyService = scope.ServiceProvider.GetRequiredService<ISceneMetadataApplyService>();
                await metadataApplyService.ApplyAsync(importedEntityId.Value, metadata, ct);
            }
        }

        return (result with { LocalPath = libraryPath }, importedEntityId);
    }

    public async Task<DownloaderBatchExecutionSummary> DownloadAndIngestBatchAsync(
        IReadOnlyList<DownloaderBatchItemDto> items,
        DownloaderBatchFollowUpDto? followUp,
        Cove.Core.Interfaces.IJobProgress? progress,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return new DownloaderBatchExecutionSummary(0, 0, 0, 0, null, []);

        followUp ??= new DownloaderBatchFollowUpDto();

        var batchItems = items.Select((item, index) => new IndexedBatchItem(item, index)).ToList();
        var issues = new ConcurrentQueue<string>();
        var importedPaths = new ConcurrentBag<string>();
        var reservedDownloads = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var succeeded = 0;
        var skipped = 0;
        var failed = 0;

        progress?.Report(0d, $"Preparing {batchItems.Count} download{(batchItems.Count == 1 ? string.Empty : "s")}...");

        await Parallel.ForEachAsync(
            batchItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = ResolveMaxConcurrentDownloads(),
                CancellationToken = ct,
            },
            async (batchItem, token) =>
            {
                var label = BuildBatchItemLabel(batchItem.Item, batchItem.Index);
                try
                {
                    var resolvedItem = await ResolveBatchItemAsync(batchItem.Item, batchItem.Index, followUp.AllowDuplicateDownloads, reservedDownloads, token);
                    label = resolvedItem.Label;

                    var (result, _) = await DownloadAndIngestAsync(
                        resolvedItem.Request,
                        resolvedItem.EntityId,
                        progress: null,
                        token,
                        autoApplyMetadata: resolvedItem.AutoApplyMetadata || (followUp.ScrapeScenes && resolvedItem.Request.Entity == DownloaderEntity.Scene),
                        allowDuplicateDownload: followUp.AllowDuplicateDownloads);

                    if (result != null)
                    {
                        importedPaths.Add(result.LocalPath);
                        Interlocked.Increment(ref succeeded);
                    }
                    else
                    {
                        Interlocked.Increment(ref skipped);
                        issues.Enqueue($"{label}: downloader returned no result.");
                    }
                }
                catch (InvalidOperationException ex) when (!followUp.AllowDuplicateDownloads && IsDuplicateDownloadMessage(ex.Message))
                {
                    Interlocked.Increment(ref skipped);
                    issues.Enqueue($"{label}: {ex.Message}");
                }
                catch (InvalidOperationException ex) when (IsBatchSkipMessage(ex.Message))
                {
                    Interlocked.Increment(ref skipped);
                    issues.Enqueue($"{label}: {ex.Message}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    issues.Enqueue($"{label}: {ex.Message}");
                    logger.LogWarning(ex, "Batch download failed for {Label}", label);
                }
                finally
                {
                    var completed = Interlocked.Increment(ref processed);
                    var percent = batchItems.Count == 0 ? 0.95d : (completed / (double)batchItems.Count) * 0.95d;
                    progress?.Report(percent, BuildBatchProgressMessage(completed, batchItems.Count, label));
                }
            });

        var followUpJobId = TryQueueFollowUpGenerateJob(followUp.Generate, importedPaths, progress);
        var summary = new DownloaderBatchExecutionSummary(
            batchItems.Count,
            succeeded,
            skipped,
            failed,
            followUpJobId,
            issues.ToArray());

        progress?.Report(1d, BuildBatchCompletionMessage(summary));
        return summary;
    }

    public async Task<DownloaderBatchPreflightResult> PreflightBatchAsync(
        IReadOnlyList<DownloaderBatchItemDto> items,
        DownloaderBatchFollowUpDto? followUp,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return new DownloaderBatchPreflightResult([], []);

        followUp ??= new DownloaderBatchFollowUpDto();
        if (followUp.AllowDuplicateDownloads)
            return new DownloaderBatchPreflightResult(items, []);

        var entities = items
            .Select(item => Enum.TryParse<DownloaderEntity>(item.Entity, true, out var entity) ? entity : (DownloaderEntity?)null)
            .OfType<DownloaderEntity>()
            .Distinct()
            .ToArray();
        var existingUrls = await LoadExistingDownloadUrlLookupAsync(entities, ct);
        var downloadedEntityIds = await LoadDownloadedEntityIdLookupAsync(entities, ct);
        var reservedDownloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemsToQueue = new List<DownloaderBatchItemDto>();
        var issues = new List<DownloaderBatchStartIssueDto>();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var label = BuildBatchItemLabel(item, index);

            if (!Enum.TryParse<DownloaderEntity>(item.Entity, true, out var entity))
            {
                itemsToQueue.Add(item);
                continue;
            }

            if (item.EntityId.HasValue
                && downloadedEntityIds.TryGetValue(entity, out var downloadedIds)
                && downloadedIds.Contains(item.EntityId.Value))
            {
                issues.Add(new DownloaderBatchStartIssueDto("skipped", label, $"{entity} {item.EntityId.Value} already has downloaded files."));
                continue;
            }

            var normalizedUrl = NormalizeUrlForLookup(item.Url);
            if (!string.IsNullOrWhiteSpace(normalizedUrl)
                && existingUrls.TryGetValue(entity, out var entityLookup)
                && entityLookup.TryGetValue(normalizedUrl, out var duplicateTargets)
                && duplicateTargets.FirstOrDefault(target => !item.EntityId.HasValue || target.EntityId != item.EntityId.Value) is { } duplicate)
            {
                issues.Add(new DownloaderBatchStartIssueDto("skipped", label, $"This URL is already downloaded for {duplicate.Label}."));
                continue;
            }

            var reservationKey = $"{entity}:{normalizedUrl}";
            if (!string.IsNullOrWhiteSpace(normalizedUrl) && !reservedDownloads.Add(reservationKey))
            {
                issues.Add(new DownloaderBatchStartIssueDto("skipped", label, "This URL is already queued elsewhere in this batch."));
                continue;
            }

            itemsToQueue.Add(item);
        }

        return new DownloaderBatchPreflightResult(itemsToQueue, issues);
    }

    internal static ScrapedSceneDto? ConvertScrapeResultToSceneMetadata(IReadOnlyDictionary<string, object> result, string sourceUrl)
    {
        if (result.Count == 0)
            return null;

        string? GetString(params string[] keys)
        {
            foreach (var key in keys)
            {
                foreach (var (entryKey, entryValue) in result)
                {
                    if (!string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (entryValue is string s && !string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                    if (entryValue is not null && entryValue is not System.Collections.IEnumerable)
                        return entryValue.ToString();
                }
            }
            return null;
        }

        List<string> GetStringList(params string[] keys)
        {
            var values = new List<string>();
            foreach (var key in keys)
            {
                foreach (var (entryKey, entryValue) in result)
                {
                    if (!string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    switch (entryValue)
                    {
                        case string s when !string.IsNullOrWhiteSpace(s):
                            foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                values.Add(part);
                            break;
                        case System.Collections.IEnumerable list:
                            foreach (var item in list)
                            {
                                switch (item)
                                {
                                    case string str when !string.IsNullOrWhiteSpace(str):
                                        values.Add(str.Trim());
                                        break;
                                    case IDictionary<string, string> map:
                                        if (map.TryGetValue("Name", out var n) || map.TryGetValue("name", out n))
                                            values.Add(n);
                                        break;
                                    case System.Collections.IDictionary genericMap:
                                        var nameValue = genericMap["Name"] ?? genericMap["name"] ?? genericMap["Title"] ?? genericMap["title"];
                                        if (nameValue is string nameStr && !string.IsNullOrWhiteSpace(nameStr))
                                            values.Add(nameStr.Trim());
                                        break;
                                }
                            }
                            break;
                    }
                }
            }
            return values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var dto = new ScrapedSceneDto
        {
            Title = GetString("Title", "title", "Name", "name"),
            Code = GetString("Code", "code"),
            Details = GetString("Details", "details", "Description", "description"),
            Director = GetString("Director", "director"),
            Date = GetString("Date", "date", "ReleaseDate", "releaseDate"),
            ImageUrl = GetString("Image", "image", "ImageUrl", "imageUrl"),
            StudioName = GetString("Studio", "studio", "StudioName", "studioName"),
            Urls = GetStringList("URLs", "urls", "URL", "url"),
            TagNames = GetStringList("Tags", "tags", "Tag", "tag", "TagNames", "tagNames"),
            PerformerNames = GetStringList("Performers", "performers", "Performer", "performer", "PerformerNames", "performerNames"),
        };

        if (!dto.Urls.Contains(sourceUrl, StringComparer.OrdinalIgnoreCase))
            dto.Urls.Add(sourceUrl);

        var hasContent = !string.IsNullOrWhiteSpace(dto.Title)
            || !string.IsNullOrWhiteSpace(dto.Code)
            || !string.IsNullOrWhiteSpace(dto.Details)
            || !string.IsNullOrWhiteSpace(dto.Date)
            || !string.IsNullOrWhiteSpace(dto.StudioName)
            || dto.PerformerNames.Count > 0
            || dto.TagNames.Count > 0;

        return hasContent ? dto : null;
    }

    private async Task<IDisposable> AcquireDownloadSlotAsync(Cove.Core.Interfaces.IJobProgress? progress, CancellationToken ct)
    {
        var slots = GetDownloadSemaphore();
        if (!await slots.WaitAsync(0, ct))
        {
            progress?.Report(0.01d, $"Waiting for a download slot ({ResolveMaxConcurrentDownloads()} max concurrent downloads)...");
            await slots.WaitAsync(ct);
        }

        return new DownloadSlotLease(slots);
    }

    private SemaphoreSlim GetDownloadSemaphore()
    {
        var desiredCapacity = ResolveMaxConcurrentDownloads();

        lock (_downloadSlotLock)
        {
            if (_downloadSlots == null)
            {
                _downloadSlots = new SemaphoreSlim(desiredCapacity, desiredCapacity);
                _downloadSlotCapacity = desiredCapacity;
                return _downloadSlots;
            }

            if (_downloadSlotCapacity != desiredCapacity && _downloadSlots.CurrentCount == _downloadSlotCapacity)
            {
                _downloadSlots.Dispose();
                _downloadSlots = new SemaphoreSlim(desiredCapacity, desiredCapacity);
                _downloadSlotCapacity = desiredCapacity;
            }

            return _downloadSlots;
        }
    }

    private int ResolveMaxConcurrentDownloads()
    {
        var configured = config.MaxConcurrentDownloads;
        return Math.Clamp(configured <= 0 ? 3 : configured, 1, 16);
    }

    private static DownloaderDescriptorDto ToDto(DownloaderDescriptor descriptor)
    {
        return new DownloaderDescriptorDto(
            descriptor.Id,
            descriptor.Name,
            descriptor.SupportedEntity.ToString(),
            descriptor.SupportedUrlPatterns.ToList(),
            GetCapabilityNames(descriptor.Capabilities));
    }

    private static DownloaderMatchDto ToDto(DownloaderDescriptor descriptor, DownloaderUrlMatch match)
    {
        return new DownloaderMatchDto(
            descriptor.Id,
            descriptor.Name,
            descriptor.SupportedEntity.ToString(),
            match.NormalizedUrl,
            match.Label,
            match.QualityOptions?.Select(option => new DownloaderQualityOptionDto(option.Id, option.Label, option.Description)).ToList() ?? []);
    }

    private static List<string> GetCapabilityNames(DownloaderCapabilities capabilities)
    {
        return Enum.GetValues<DownloaderCapabilities>()
            .Where(capability => capability != DownloaderCapabilities.None && capabilities.HasFlag(capability))
            .Select(capability => capability.ToString())
            .ToList();
    }

    private static string SanitizePathSegment(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');

        return value.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
    }

    public async Task<string?> GetDuplicateDownloadReasonAsync(DownloaderEntity entity, int? entityId, string url, CancellationToken ct)
    {
        return await FindDuplicateDownloadReasonAsync(entity, entityId, url, ct);
    }

    private async Task EnsureDownloadAllowedAsync(DownloaderRequest request, int? entityId, CancellationToken ct)
    {
        var duplicateReason = await GetDuplicateDownloadReasonAsync(request.Entity, entityId, request.Url, ct);
        if (!string.IsNullOrWhiteSpace(duplicateReason))
            throw new InvalidOperationException(duplicateReason);
    }

    private async Task<string?> FindDuplicateDownloadReasonAsync(DownloaderEntity entity, int? entityId, string url, CancellationToken ct)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<CoveContext>();
        if (db == null)
            return null;

        if (entityId.HasValue)
        {
            var currentHasFiles = entity switch
            {
                DownloaderEntity.Scene => await db.VideoFiles.AnyAsync(item => item.SceneId == entityId.Value, ct),
                DownloaderEntity.Image => await db.ImageFiles.AnyAsync(item => item.ImageId == entityId.Value, ct),
                DownloaderEntity.Gallery => await db.GalleryFiles.AnyAsync(item => item.GalleryId == entityId.Value, ct),
                _ => false,
            };

            if (currentHasFiles)
                return $"{entity} {entityId.Value} already has downloaded files.";
        }

        var normalizedUrl = NormalizeUrlForLookup(url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
            return null;

        var duplicateLabel = entity switch
        {
            DownloaderEntity.Scene => await FindDuplicateSceneLabelAsync(db, entityId, normalizedUrl, ct),
            DownloaderEntity.Image => await FindDuplicateImageLabelAsync(db, entityId, normalizedUrl, ct),
            DownloaderEntity.Gallery => await FindDuplicateGalleryLabelAsync(db, entityId, normalizedUrl, ct),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(duplicateLabel)
            ? null
            : $"This URL is already downloaded for {duplicateLabel}.";
    }

    private async Task<Dictionary<DownloaderEntity, Dictionary<string, List<ExistingDownloadTarget>>>> LoadExistingDownloadUrlLookupAsync(
        IReadOnlyCollection<DownloaderEntity> entities,
        CancellationToken ct)
    {
        var result = new Dictionary<DownloaderEntity, Dictionary<string, List<ExistingDownloadTarget>>>();
        if (entities.Count == 0)
            return result;

        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<CoveContext>();
        if (db == null)
            return result;

        if (entities.Contains(DownloaderEntity.Scene))
        {
            var rows = await db.Set<Cove.Core.Entities.SceneUrl>()
                .AsNoTracking()
                .Select(item => new { item.SceneId, item.Url, item.Scene!.Title })
                .ToListAsync(ct);
            result[DownloaderEntity.Scene] = BuildExistingUrlLookup(rows.Select(item => new ExistingUrlRow(item.SceneId, item.Url, item.Title ?? $"Scene {item.SceneId}")));
        }

        if (entities.Contains(DownloaderEntity.Image))
        {
            var rows = await db.Set<Cove.Core.Entities.ImageUrl>()
                .AsNoTracking()
                .Select(item => new { item.ImageId, item.Url, item.Image!.Title })
                .ToListAsync(ct);
            result[DownloaderEntity.Image] = BuildExistingUrlLookup(rows.Select(item => new ExistingUrlRow(item.ImageId, item.Url, item.Title ?? $"Image {item.ImageId}")));
        }

        if (entities.Contains(DownloaderEntity.Gallery))
        {
            var rows = await db.Set<Cove.Core.Entities.GalleryUrl>()
                .AsNoTracking()
                .Select(item => new { item.GalleryId, item.Url, item.Gallery!.Title })
                .ToListAsync(ct);
            result[DownloaderEntity.Gallery] = BuildExistingUrlLookup(rows.Select(item => new ExistingUrlRow(item.GalleryId, item.Url, item.Title ?? $"Gallery {item.GalleryId}")));
        }

        return result;
    }

    private async Task<Dictionary<DownloaderEntity, HashSet<int>>> LoadDownloadedEntityIdLookupAsync(
        IReadOnlyCollection<DownloaderEntity> entities,
        CancellationToken ct)
    {
        var result = new Dictionary<DownloaderEntity, HashSet<int>>();
        if (entities.Count == 0)
            return result;

        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<CoveContext>();
        if (db == null)
            return result;

        if (entities.Contains(DownloaderEntity.Scene))
        {
            var ids = await db.VideoFiles
                .AsNoTracking()
                .Where(item => item.SceneId != null)
                .Select(item => item.SceneId!.Value)
                .Distinct()
                .ToListAsync(ct);
            result[DownloaderEntity.Scene] = ids.ToHashSet();
        }

        if (entities.Contains(DownloaderEntity.Image))
        {
            var ids = await db.ImageFiles
                .AsNoTracking()
                .Where(item => item.ImageId != null)
                .Select(item => item.ImageId!.Value)
                .Distinct()
                .ToListAsync(ct);
            result[DownloaderEntity.Image] = ids.ToHashSet();
        }

        if (entities.Contains(DownloaderEntity.Gallery))
        {
            var ids = await db.GalleryFiles
                .AsNoTracking()
                .Where(item => item.GalleryId != null)
                .Select(item => item.GalleryId!.Value)
                .Distinct()
                .ToListAsync(ct);
            result[DownloaderEntity.Gallery] = ids.ToHashSet();
        }

        return result;
    }

    private static Dictionary<string, List<ExistingDownloadTarget>> BuildExistingUrlLookup(IEnumerable<ExistingUrlRow> rows)
    {
        var lookup = new Dictionary<string, List<ExistingDownloadTarget>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var normalizedUrl = NormalizeUrlForLookup(row.Url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
                continue;

            if (!lookup.TryGetValue(normalizedUrl, out var targets))
            {
                targets = [];
                lookup[normalizedUrl] = targets;
            }

            targets.Add(new ExistingDownloadTarget(row.EntityId, row.Label));
        }

        return lookup;
    }

    private string MoveDownloadedFileToLibrary(DownloaderResult result, DownloaderEntity entity, string? downloaderId, string? sourceUrl)
    {
        var sourcePath = result.LocalPath;
        var (destinationRoot, useEntitySubdirectory) = ResolveLibraryRoot(entity, downloaderId, sourceUrl);
        var destinationDirectory = useEntitySubdirectory
            ? Path.Combine(destinationRoot, "_downloads", GetEntityDownloadFolder(entity))
            : destinationRoot;
        Directory.CreateDirectory(destinationDirectory);

        var preferredFileName = string.IsNullOrWhiteSpace(result.OriginalFilename)
            ? Path.GetFileName(sourcePath)
            : result.OriginalFilename;
        var sanitizedFileName = SanitizePathSegment(string.IsNullOrWhiteSpace(preferredFileName) ? Path.GetFileName(sourcePath) : preferredFileName);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitizedFileName)))
            sanitizedFileName += Path.GetExtension(sourcePath);

        string destinationPath;
        lock (_libraryMoveLock)
        {
            destinationPath = GetUniquePath(destinationDirectory, sanitizedFileName);
            File.Move(sourcePath, destinationPath);
        }

        TryDeleteParentDirectory(sourcePath);
        return destinationPath;
    }

    private (string Root, bool UseEntitySubdirectory) ResolveLibraryRoot(DownloaderEntity entity, string? downloaderId = null, string? sourceUrl = null)
    {
        var overrideRoot = ResolveDownloaderOverrideRoot(downloaderId, sourceUrl);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            Directory.CreateDirectory(overrideRoot);
            return (Path.GetFullPath(overrideRoot), false);
        }

        var root = entity switch
        {
            DownloaderEntity.Scene => config.CovePaths.FirstOrDefault(path => !path.ExcludeVideo)?.Path,
            DownloaderEntity.Image => config.CovePaths.FirstOrDefault(path => !path.ExcludeImage)?.Path,
            DownloaderEntity.Gallery => config.CovePaths.FirstOrDefault(path => !path.ExcludeImage)?.Path,
            DownloaderEntity.Audio => config.CovePaths.FirstOrDefault(path => !path.ExcludeAudio)?.Path,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException($"No Cove library path is configured for {entity} downloads");

        Directory.CreateDirectory(root);
        return (Path.GetFullPath(root), true);
    }

    private string? ResolveDownloaderOverrideRoot(string? downloaderId, string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(downloaderId))
            return null;

        var matchingOverrides = config.DownloaderPathOverrides
            .Where(overridePath => !string.IsNullOrWhiteSpace(overridePath.Path))
            .Where(overridePath => string.Equals(overridePath.DownloaderId, downloaderId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingOverrides.Count == 0)
            return null;

        var normalizedSite = NormalizeOverrideSite(sourceUrl);
        if (!string.IsNullOrWhiteSpace(normalizedSite))
        {
            var siteOverride = matchingOverrides.FirstOrDefault(overridePath =>
                string.Equals(NormalizeOverrideSite(overridePath.Site), normalizedSite, StringComparison.OrdinalIgnoreCase));
            if (siteOverride != null)
                return siteOverride.Path;
        }

        return matchingOverrides.FirstOrDefault(overridePath => string.IsNullOrWhiteSpace(overridePath.Site))?.Path;
    }

    internal static string NormalizeUrlForLookup(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return trimmed.TrimEnd('/').ToLowerInvariant();

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
        host = host.ToLowerInvariant();

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
            path = "/";

        return string.Concat(host, port, path.ToLowerInvariant(), NormalizeQueryForLookup(uri.Query));
    }

    private static string NormalizeQueryForLookup(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query == "?")
            return string.Empty;

        var parts = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseQueryPart)
            .Where(part => !IsTrackingQueryParameter(part.Key))
            .OrderBy(part => part.Key, StringComparer.Ordinal)
            .ThenBy(part => part.Value, StringComparer.Ordinal)
            .Select(part => string.IsNullOrEmpty(part.Value) ? part.Key : $"{part.Key}={part.Value}")
            .ToList();

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static (string Key, string Value) ParseQueryPart(string part)
    {
        var separator = part.IndexOf('=');
        var rawKey = separator < 0 ? part : part[..separator];
        var rawValue = separator < 0 ? string.Empty : part[(separator + 1)..];
        return (NormalizeQueryToken(rawKey), NormalizeQueryToken(rawValue));
    }

    private static string NormalizeQueryToken(string value)
    {
        var plusAsSpace = value.Replace('+', ' ');
        try
        {
            return Uri.UnescapeDataString(plusAsSpace).Trim().ToLowerInvariant();
        }
        catch
        {
            return plusAsSpace.Trim().ToLowerInvariant();
        }
    }

    private static bool IsTrackingQueryParameter(string key)
        => key.StartsWith("utm_", StringComparison.Ordinal)
            || key is "fbclid" or "gclid" or "dclid" or "gbraid" or "wbraid" or "msclkid" or "mc_cid" or "mc_eid" or "igshid";

    private string? TryQueueFollowUpGenerateJob(GenerateOptionsDto? generate, IEnumerable<string> importedPaths, Cove.Core.Interfaces.IJobProgress? progress)
    {
        if (generate == null || !HasGenerateFollowUp(generate))
            return null;

        var directories = importedPaths
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (directories.Count == 0)
            return null;

        if (generate.Markers)
            logger.LogInformation("Batch download generate follow-up does not currently support marker generation; skipping marker option.");

        using var scope = serviceScopeFactory.CreateScope();
        var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
        progress?.Report(0.98d, "Queueing follow-up generation scan...");

        return scanService.StartScan(new ScanOperationOptions
        {
            Paths = directories,
            GenerateCovers = generate.Thumbnails,
            GeneratePreviews = generate.Previews,
            GenerateSprites = generate.Sprites,
            GeneratePhashes = generate.Phashes,
            GenerateMd5 = generate.Md5,
            GenerateImageThumbnails = generate.ImageThumbnails,
            GenerateImagePhashes = generate.ImagePhashes,
            Rescan = generate.Overwrite,
        });
    }

    private static bool HasGenerateFollowUp(GenerateOptionsDto generate)
    {
        return generate.Thumbnails
            || generate.Previews
            || generate.Sprites
            || generate.Phashes
            || generate.Md5
            || generate.ImageThumbnails
            || generate.ImagePhashes;
    }

    private static bool IsDuplicateDownloadMessage(string message)
    {
        return message.Contains("already downloaded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already has downloaded files", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBatchSkipMessage(string message)
    {
        return message.Contains("No compatible", StringComparison.OrdinalIgnoreCase)
            || message.Contains("is missing a URL", StringComparison.OrdinalIgnoreCase)
            || message.Contains("is missing an entity id", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unsupported entity type", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already queued elsewhere in this batch", StringComparison.OrdinalIgnoreCase)
            || message.Contains("do not support creating new", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBatchProgressMessage(int completed, int total, string label)
    {
        return $"Processed {completed}/{total}: {label}";
    }

    private static string BuildBatchCompletionMessage(DownloaderBatchExecutionSummary summary)
    {
        var parts = new List<string>
        {
            $"Downloaded {summary.SucceededCount} of {summary.TotalCount} item{(summary.TotalCount == 1 ? string.Empty : "s")}."
        };

        if (summary.SkippedCount > 0)
            parts.Add($"Skipped {summary.SkippedCount}.");

        if (summary.FailedCount > 0)
            parts.Add($"Failed {summary.FailedCount}.");

        if (!string.IsNullOrWhiteSpace(summary.FollowUpJobId))
            parts.Add($"Queued follow-up generate job {summary.FollowUpJobId}.");

        if (summary.Issues.Count > 0)
        {
            parts.Add(string.Join(' ', summary.Issues.Take(2)));
            if (summary.Issues.Count > 2)
                parts.Add($"+{summary.Issues.Count - 2} more issue(s).");
        }

        return string.Join(' ', parts);
    }

    private async Task<ResolvedBatchItem> ResolveBatchItemAsync(
        DownloaderBatchItemDto item,
        int index,
        bool allowDuplicateDownload,
        ConcurrentDictionary<string, byte> reservedDownloads,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Url))
            throw new InvalidOperationException($"Batch download item {index + 1} is missing a URL.");

        if (!Enum.TryParse<DownloaderEntity>(item.Entity, true, out var entity))
            throw new InvalidOperationException($"Batch download item {index + 1} has an unsupported entity type '{item.Entity}'.");

        var normalizedUrl = item.Url.Trim();
        var label = BuildBatchItemLabel(item, index);
        var matched = await ResolveBatchMatchAsync(item, entity, normalizedUrl, ct);
        var effectiveUrl = matched.NormalizedUrl;

        if (!allowDuplicateDownload)
        {
            var duplicateReason = await GetDuplicateDownloadReasonAsync(entity, item.EntityId, effectiveUrl, ct);
            if (!string.IsNullOrWhiteSpace(duplicateReason))
                throw new InvalidOperationException(duplicateReason);

            var reservationKey = $"{entity}:{NormalizeUrlForLookup(effectiveUrl)}";
            if (!reservedDownloads.TryAdd(reservationKey, 0))
                throw new InvalidOperationException("This URL is already queued elsewhere in this batch.");
        }

        var entityId = item.EntityId;
        if (!entityId.HasValue && item.CreateEntityIfMissing)
            entityId = await CreatePlaceholderEntityAsync(entity, effectiveUrl, ResolvePlaceholderTitle(item, effectiveUrl, label), ct);

        if (!entityId.HasValue && entity is DownloaderEntity.Scene or DownloaderEntity.Image or DownloaderEntity.Gallery)
            throw new InvalidOperationException($"Batch download item {index + 1} is missing an entity id.");

        return new ResolvedBatchItem(
            new DownloaderRequest(matched.DownloaderId, effectiveUrl, entity, BuildDownloaderPermissions(effectiveUrl), matched.QualityId),
            entityId,
            label,
            item.AutoApplyMetadata);
    }

    private async Task<ResolvedBatchMatch> ResolveBatchMatchAsync(DownloaderBatchItemDto item, DownloaderEntity entity, string url, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(item.DownloaderId))
            return new ResolvedBatchMatch(item.DownloaderId.Trim(), url, item.QualityId);

        var selectedMatch = (await MatchUrlAsync(url, ct))
            .FirstOrDefault(match => string.Equals(match.SupportedEntity, entity.ToString(), StringComparison.OrdinalIgnoreCase));

        if (selectedMatch == null)
            throw new InvalidOperationException($"No compatible {entity.ToString().ToLowerInvariant()} downloader matched this URL.");

        return new ResolvedBatchMatch(
            selectedMatch.DownloaderId,
            string.IsNullOrWhiteSpace(selectedMatch.NormalizedUrl) ? url : selectedMatch.NormalizedUrl,
            item.QualityId ?? selectedMatch.QualityOptions.FirstOrDefault()?.Id);
    }

    private async Task<int> CreatePlaceholderEntityAsync(DownloaderEntity entity, string url, string title, CancellationToken ct)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();

        switch (entity)
        {
            case DownloaderEntity.Scene:
            {
                var scene = new Scene
                {
                    Title = title,
                    Organized = false,
                    Urls = [new SceneUrl { Url = url }],
                };
                db.Scenes.Add(scene);
                await db.SaveChangesAsync(ct);
                return scene.Id;
            }
            case DownloaderEntity.Image:
            {
                var image = new Image
                {
                    Title = title,
                    Organized = false,
                    Urls = [new ImageUrl { Url = url }],
                };
                db.Images.Add(image);
                await db.SaveChangesAsync(ct);
                return image.Id;
            }
            case DownloaderEntity.Gallery:
            {
                var gallery = new Gallery
                {
                    Title = title,
                    Organized = false,
                    Urls = [new GalleryUrl { Url = url }],
                };
                db.Galleries.Add(gallery);
                await db.SaveChangesAsync(ct);
                return gallery.Id;
            }
            default:
                throw new InvalidOperationException($"Batch imports do not support creating new {entity.ToString().ToLowerInvariant()} records.");
        }
    }

    private static string ResolvePlaceholderTitle(DownloaderBatchItemDto item, string url, string label)
    {
        if (!string.IsNullOrWhiteSpace(item.Title))
            return item.Title.Trim();

        if (!string.IsNullOrWhiteSpace(item.Label))
            return item.Label.Trim();

        return DeriveTitleFromUrl(url, label);
    }

    private static string BuildBatchItemLabel(DownloaderBatchItemDto item, int index)
    {
        if (!string.IsNullOrWhiteSpace(item.Label))
            return item.Label.Trim();

        if (!string.IsNullOrWhiteSpace(item.Title))
            return item.Title.Trim();

        return string.IsNullOrWhiteSpace(item.Url)
            ? $"Batch item {index + 1}"
            : DeriveTitleFromUrl(item.Url, item.Url.Trim());
    }

    private static string DeriveTitleFromUrl(string url, string fallback)
    {
        try
        {
            var parsed = new Uri(url, UriKind.Absolute);
            var fileName = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return Uri.UnescapeDataString(fileName)
                    .Replace('_', ' ')
                    .Replace('-', ' ')
                    .Trim();
            }

            return parsed.Host;
        }
        catch
        {
            return fallback;
        }
    }

    private static DownloaderPermissions BuildDownloaderPermissions(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return new DownloaderPermissions([uri.Host]);

        return new DownloaderPermissions();
    }

    private sealed record ResolvedBatchItem(DownloaderRequest Request, int? EntityId, string Label, bool AutoApplyMetadata);

    private sealed record ResolvedBatchMatch(string DownloaderId, string NormalizedUrl, string? QualityId);

    private sealed record IndexedBatchItem(DownloaderBatchItemDto Item, int Index);

    private sealed record ExistingUrlRow(int EntityId, string Url, string Label);

    private sealed record ExistingDownloadTarget(int EntityId, string Label);

    private static string? NormalizeOverrideSite(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            trimmed = absoluteUri.Host;

        trimmed = trimmed.ToLowerInvariant();
        return trimmed.StartsWith("www.", StringComparison.Ordinal) ? trimmed[4..] : trimmed;
    }

    private static async Task<string?> FindDuplicateSceneLabelAsync(CoveContext db, int? entityId, string normalizedUrl, CancellationToken ct)
    {
        var candidateUrls = await db.Set<Cove.Core.Entities.SceneUrl>()
            .Where(item => !entityId.HasValue || item.SceneId != entityId.Value)
            .Select(item => new { item.SceneId, item.Url })
            .AsNoTracking()
            .ToListAsync(ct);
        var duplicateId = candidateUrls
            .Where(item => NormalizeUrlForLookup(item.Url) == normalizedUrl)
            .Select(item => item.SceneId)
            .FirstOrDefault();

        if (duplicateId == 0)
            return null;

        var duplicate = await db.Scenes.FirstOrDefaultAsync(item => item.Id == duplicateId, ct);
        return duplicate == null ? null : duplicate.Title ?? $"Scene {duplicate.Id}";
    }

    private static async Task<string?> FindDuplicateImageLabelAsync(CoveContext db, int? entityId, string normalizedUrl, CancellationToken ct)
    {
        var candidateUrls = await db.Set<Cove.Core.Entities.ImageUrl>()
            .Where(item => !entityId.HasValue || item.ImageId != entityId.Value)
            .Select(item => new { item.ImageId, item.Url })
            .AsNoTracking()
            .ToListAsync(ct);
        var duplicateId = candidateUrls
            .Where(item => NormalizeUrlForLookup(item.Url) == normalizedUrl)
            .Select(item => item.ImageId)
            .FirstOrDefault();

        if (duplicateId == 0)
            return null;

        var duplicate = await db.Images.FirstOrDefaultAsync(item => item.Id == duplicateId, ct);
        return duplicate == null ? null : duplicate.Title ?? $"Image {duplicate.Id}";
    }

    private static async Task<string?> FindDuplicateGalleryLabelAsync(CoveContext db, int? entityId, string normalizedUrl, CancellationToken ct)
    {
        var candidateUrls = await db.Set<Cove.Core.Entities.GalleryUrl>()
            .Where(item => !entityId.HasValue || item.GalleryId != entityId.Value)
            .Select(item => new { item.GalleryId, item.Url })
            .AsNoTracking()
            .ToListAsync(ct);
        var duplicateId = candidateUrls
            .Where(item => NormalizeUrlForLookup(item.Url) == normalizedUrl)
            .Select(item => item.GalleryId)
            .FirstOrDefault();

        if (duplicateId == 0)
            return null;

        var duplicate = await db.Galleries.FirstOrDefaultAsync(item => item.Id == duplicateId, ct);
        return duplicate == null ? null : duplicate.Title ?? $"Gallery {duplicate.Id}";
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName;
        var extension = Path.GetExtension(safeFileName);
        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var candidate = Path.Combine(directory, safeFileName);
        var counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}-{counter}{extension}");
            counter++;
        }

        return candidate;
    }

    private static string GetEntityDownloadFolder(DownloaderEntity entity)
    {
        return entity switch
        {
            DownloaderEntity.Scene => "scenes",
            DownloaderEntity.Image => "images",
            DownloaderEntity.Gallery => "galleries",
            DownloaderEntity.Audio => "audio",
            _ => entity.ToString().ToLowerInvariant() + "s",
        };
    }

    private static async Task<int> ImportSceneAsync(IScanService scanService, string libraryPath, int? entityId, Cove.Core.Interfaces.IJobProgress? progress, CancellationToken ct)
    {
        progress?.Report(0.98d, entityId.HasValue ? "Importing downloaded scene..." : "Creating scene from download...");
        return await scanService.ImportDownloadedSceneAsync(libraryPath, entityId, ct);
    }

    private static async Task<int> ImportImageAsync(IScanService scanService, string libraryPath, int? entityId, Cove.Core.Interfaces.IJobProgress? progress, CancellationToken ct)
    {
        progress?.Report(0.98d, entityId.HasValue ? "Importing downloaded image..." : "Creating image from download...");
        return await scanService.ImportDownloadedImageAsync(libraryPath, entityId, ct);
    }

    private static async Task<int> ImportGalleryAsync(IScanService scanService, string libraryPath, int? entityId, Cove.Core.Interfaces.IJobProgress? progress, CancellationToken ct)
    {
        progress?.Report(0.98d, entityId.HasValue ? "Importing downloaded gallery..." : "Creating gallery from download...");
        return await scanService.ImportDownloadedGalleryAsync(libraryPath, entityId, ct);
    }

    private static void TryDeleteParentDirectory(string filePath)
    {
        try
        {
            var parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent, recursive: false);
        }
        catch
        {
            // Best-effort cleanup for the downloader temp directory.
        }
    }

    private sealed class DownloaderHost(
        string tempDirectory,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        Cove.Core.Interfaces.IJobProgress? progress) : IDownloaderHost
    {
        public string TempDirectory { get; } = tempDirectory;
        public IHttpClientFactory HttpClients { get; } = httpClientFactory;

        public ILogger CreateLogger(string categoryName) => loggerFactory.CreateLogger(categoryName);

        public void ReportProgress(double progressValue, string? message = null)
        {
            progress?.Report(progressValue, message);
        }
    }

    private sealed class DownloadSlotLease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose()
        {
            semaphore.Release();
        }
    }
}