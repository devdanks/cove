using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<Dictionary<string, string>> ImportBlobsAsync(SqliteConnection conn, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = await CountAsync(conn, "blobs", ct);
        var processed = 0;
        progress.Report(startProgress, "Importing blobs...");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT checksum, blob FROM blobs WHERE blob IS NOT NULL";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            processed++;
            if (r.IsDBNull(1)) continue;
            var checksum = r.GetString(0);
            try
            {
                var bytes = (byte[])r.GetValue(1);
                using var ms = new MemoryStream(bytes);
                var contentType = DetectImageContentType(ms);
                ms.Position = 0;
                var blobId = await _blobService.StoreBlobAsync(ms, contentType, ct);
                map[checksum] = blobId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Blob {Checksum} import failed: {Err}", checksum, ex.Message);
            }

            if (processed % 100 == 0 || processed == total)
                ReportPhase(progress, startProgress, endProgress, processed, total, $"Importing blobs ({processed}/{total})");
        }
        _logger.LogInformation("Imported {Count} blobs", map.Count);
        return map;
    }

    private async Task<Dictionary<int, int>> ImportFoldersAsync(SqliteConnection conn, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        var folderData = new Dictionary<int, (string Path, int? ParentId, DateTime ModTime, DateTime CreatedAt)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, path, parent_folder_id, mod_time, created_at FROM folders";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                folderData[r.GetInt32(0)] = (r.GetString(1), ReadIntNull(r, 2),
                    ParseDateTime(r.GetString(3)), ParseDateTime(r.GetString(4)));
        }

        var folderIdMap = new Dictionary<int, int>();
        var ordered = TopologicalSort(folderData.Keys.ToList(),
            id => folderData[id].ParentId.HasValue ? [folderData[id].ParentId!.Value] : (IEnumerable<int>)[]);

        var allPaths = folderData.Values
            .SelectMany(fd => GetImportedPathLookupCandidates(fd.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingFoldersByPath = _db.Folders
            .Where(f => allPaths.Contains(f.Path))
            .AsEnumerable()
            .GroupBy(f => NormalizeImportedPath(f.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(f => f.Id).First().Id, StringComparer.OrdinalIgnoreCase);

        progress.Report(startProgress, "Importing folders...");

        const int FolderBatchSize = 1000;
        var pendingFolders = new List<(int StashId, string NormalizedPath, Folder Entity)>(FolderBatchSize);
        var createdFoldersByStashId = new Dictionary<int, Folder>();
        var createdFoldersByPath = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);

        async Task FlushFolderBatchAsync()
        {
            if (pendingFolders.Count == 0)
                return;

            await _db.SaveChangesAsync(ct);
            foreach (var (stashId, normalizedPath, entity) in pendingFolders)
            {
                folderIdMap[stashId] = entity.Id;
                existingFoldersByPath[normalizedPath] = entity.Id;
            }

            pendingFolders.Clear();
            _db.ChangeTracker.Clear();
            ReportPhase(progress, startProgress, endProgress, folderIdMap.Count, ordered.Count, $"Importing folders ({folderIdMap.Count}/{ordered.Count})");
        }

        foreach (var stashFolderId in ordered)
        {
            var fd = folderData[stashFolderId];
            var normalizedPath = NormalizeImportedPath(fd.Path);
            if (existingFoldersByPath.TryGetValue(normalizedPath, out var existingId))
            {
                folderIdMap[stashFolderId] = existingId;
                continue;
            }

            if (createdFoldersByPath.TryGetValue(normalizedPath, out var pendingFolder))
            {
                createdFoldersByStashId[stashFolderId] = pendingFolder;
                pendingFolders.Add((stashFolderId, normalizedPath, pendingFolder));
                if (pendingFolders.Count >= FolderBatchSize)
                    await FlushFolderBatchAsync();
                continue;
            }

            var folder = new Folder
            {
                Path = normalizedPath,
                ParentFolderId = fd.ParentId.HasValue && folderIdMap.TryGetValue(fd.ParentId.Value, out var pfId) ? pfId : null,
                ParentFolder = fd.ParentId.HasValue && !folderIdMap.ContainsKey(fd.ParentId.Value) && createdFoldersByStashId.TryGetValue(fd.ParentId.Value, out var parentFolder) ? parentFolder : null,
                ModTime = fd.ModTime,
                CreatedAt = fd.CreatedAt,
                UpdatedAt = fd.ModTime,
            };
            _db.Folders.Add(folder);
            createdFoldersByStashId[stashFolderId] = folder;
            createdFoldersByPath[normalizedPath] = folder;
            pendingFolders.Add((stashFolderId, normalizedPath, folder));

            if (pendingFolders.Count >= FolderBatchSize)
                await FlushFolderBatchAsync();
        }

        await FlushFolderBatchAsync();
        _logger.LogInformation("Imported {Count} folders", folderIdMap.Count);
        return folderIdMap;
    }

    private async Task ReconcileImportedZipLinksAsync(
        SqliteConnection conn,
        Dictionary<int, int> folderIdMap,
        Dictionary<int, int> imageIdMap,
        Dictionary<int, int> galleryFileIdMap,
        CancellationToken ct)
    {
        if (galleryFileIdMap.Count == 0 || !await TableExistsAsync(conn, "files", ct)
            || !await ColumnExistsAsync(conn, "files", "zip_file_id", ct))
        {
            return;
        }

        await ReconcileImportedFolderZipLinksAsync(conn, folderIdMap, galleryFileIdMap, ct);
        await ReconcileImportedImageFileZipLinksAsync(conn, folderIdMap, imageIdMap, galleryFileIdMap, ct);
    }

    private async Task ReconcileImportedFolderZipLinksAsync(
        SqliteConnection conn,
        Dictionary<int, int> folderIdMap,
        Dictionary<int, int> galleryFileIdMap,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "folders", ct)
            || !await ColumnExistsAsync(conn, "folders", "zip_file_id", ct))
        {
            return;
        }

        var folderZipLinks = new List<(int StashFolderId, int StashZipFileId)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, zip_file_id FROM folders WHERE zip_file_id IS NOT NULL";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                folderZipLinks.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        if (folderZipLinks.Count == 0) return;

        var targetFolderIds = folderZipLinks
            .Where(link => folderIdMap.ContainsKey(link.StashFolderId) && galleryFileIdMap.ContainsKey(link.StashZipFileId))
            .Select(link => folderIdMap[link.StashFolderId])
            .Distinct()
            .ToList();

        if (targetFolderIds.Count == 0) return;

        var foldersById = (await _db.Folders
            .Where(folder => targetFolderIds.Contains(folder.Id))
            .ToListAsync(ct))
            .ToDictionary(folder => folder.Id);

        var updated = 0;
        foreach (var (stashFolderId, stashZipFileId) in folderZipLinks)
        {
            if (!folderIdMap.TryGetValue(stashFolderId, out var coveFolderId)) continue;
            if (!galleryFileIdMap.TryGetValue(stashZipFileId, out var coveZipFileId)) continue;
            if (!foldersById.TryGetValue(coveFolderId, out var folder)) continue;
            if (folder.ZipFileId == coveZipFileId) continue;

            folder.ZipFileId = coveZipFileId;
            updated++;
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Reconciled {Count} imported folder zip links", updated);
        }
    }

    private async Task ReconcileImportedImageFileZipLinksAsync(
        SqliteConnection conn,
        Dictionary<int, int> folderIdMap,
        Dictionary<int, int> imageIdMap,
        Dictionary<int, int> galleryFileIdMap,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "images_files", ct))
        {
            return;
        }

        var sourceLinks = new List<(int StashImageId, string Basename, int ParentFolderId, int StashZipFileId)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT images_files.image_id, files.basename, files.parent_folder_id, files.zip_file_id
FROM images_files
JOIN files ON files.id = images_files.file_id
WHERE files.zip_file_id IS NOT NULL";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                sourceLinks.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3)));
            }
        }

        if (sourceLinks.Count == 0) return;

        var targetImageIds = sourceLinks
            .Where(link => imageIdMap.ContainsKey(link.StashImageId))
            .Select(link => imageIdMap[link.StashImageId])
            .Distinct()
            .ToList();

        if (targetImageIds.Count == 0) return;

        var imageFilesByKey = (await _db.ImageFiles
            .Where(file => file.ImageId.HasValue && targetImageIds.Contains(file.ImageId.Value))
            .ToListAsync(ct))
            .ToDictionary(file => GetImportedImageFileKey(file.ImageId ?? 0, file.ParentFolderId, file.Basename));

        var updated = 0;
        foreach (var (stashImageId, basename, stashParentFolderId, stashZipFileId) in sourceLinks)
        {
            if (!imageIdMap.TryGetValue(stashImageId, out var coveImageId)
                || !folderIdMap.TryGetValue(stashParentFolderId, out var coveParentFolderId)
                || !galleryFileIdMap.TryGetValue(stashZipFileId, out var coveZipFileId))
            {
                continue;
            }

            var key = GetImportedImageFileKey(coveImageId, coveParentFolderId, basename);
            if (!imageFilesByKey.TryGetValue(key, out var imageFile) || imageFile.ZipFileId == coveZipFileId)
            {
                continue;
            }

            imageFile.ZipFileId = coveZipFileId;
            updated++;
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Reconciled {Count} imported image file zip links", updated);
        }
    }

    private static string GetImportedImageFileKey(int imageId, int parentFolderId, string basename)
        => $"{imageId}|{parentFolderId}|{basename}";

    private static string GetImportedBaseFileKey(int parentFolderId, string basename)
        => $"{parentFolderId}|{basename}";

    private async Task ImportLibraryPathsAsync(string stashDbPath, CancellationToken ct)
    {
        try
        {
            var configDir = Path.GetDirectoryName(stashDbPath)!;
            var configPath = Path.Combine(configDir, "config.yml");
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("Stash config.yml not found at {Path}, skipping library path import", configPath);
                return;
            }

            var stashConfig = ParseStashConfig(configPath);
            var paths = stashConfig.Paths;
            if (paths.Count == 0)
            {
                _logger.LogInformation("No library paths found in Stash config");
                return;
            }

            var existingPaths = new HashSet<string>(_config.CovePaths.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
            var dto = _configService.GetConfig();
            foreach (var (path, excludeImage, excludeVideo) in paths)
            {
                if (existingPaths.Contains(path)) continue;
                dto.CovePaths.Add(new CovePathDto
                {
                    Path = path,
                    ExcludeImage = excludeImage,
                    ExcludeVideo = excludeVideo,
                    ExcludeAudio = false,
                });
            }

            await _configService.SaveConfigAsync(dto);
            _logger.LogInformation("Imported {Count} library paths from Stash config", paths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import library paths from Stash config");
        }
    }

    private async Task ApplyCoveGeneratedPathOverrideAsync(string? coveGeneratedPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coveGeneratedPath))
            return;

        var normalizedPath = coveGeneratedPath.Trim();
        var dto = _configService.GetConfig();
        if (string.Equals(dto.GeneratedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            return;

        await _configService.SaveConfigAsync(dto with { GeneratedPath = normalizedPath });
        _logger.LogInformation("Updated Cove generated path to {Path} before Stash import", normalizedPath);
    }

    private async Task CopyGeneratedContentAsync(string stashDbPath, Dictionary<int, SceneGeneratedData> sceneGeneratedMap, StashImportOptions options, IJobProgress progress, double startProgress, double endProgress, CancellationToken ct)
    {
        try
        {
            progress.Report(startProgress, "Copying generated scene assets...");
            var configDir = Path.GetDirectoryName(stashDbPath)!;
            var configPath = Path.Combine(configDir, "config.yml");
            var stashConfig = File.Exists(configPath)
                ? ParseStashConfig(configPath)
                : new StashConfigData([], null, "OSHASH");
            var stashGeneratedPath = stashConfig.GeneratedPath;
            if (string.IsNullOrWhiteSpace(stashGeneratedPath) || !Directory.Exists(stashGeneratedPath))
            {
                _logger.LogWarning("Stash generated path not found: {Path}", stashGeneratedPath);
                return;
            }

            var stashScreenshotsDir = Path.Combine(stashGeneratedPath, "screenshots");
            var stashVttDir = Path.Combine(stashGeneratedPath, "vtt");

            var previewHashes = Directory.Exists(stashScreenshotsDir)
                ? Directory.EnumerateFiles(stashScreenshotsDir, "*.mp4", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var spriteHashes = Directory.Exists(stashVttDir)
                ? Directory.EnumerateFiles(stashVttDir, "*_sprite.jpg", SearchOption.TopDirectoryOnly)
                    .Select(path => TrimGeneratedSuffix(Path.GetFileNameWithoutExtension(path), "_sprite"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var vttHashes = Directory.Exists(stashVttDir)
                ? Directory.EnumerateFiles(stashVttDir, "*_thumbs.vtt", SearchOption.TopDirectoryOnly)
                    .Select(path => TrimGeneratedSuffix(Path.GetFileNameWithoutExtension(path), "_thumbs"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int sourceScreenshots = 0;
            int migratedScreenshots = 0;
            int sourcePreviews = 0;
            int migratedPreviews = 0;
            int sourceSprites = 0;
            int migratedSprites = 0;
            int sourceVtts = 0;
            int migratedVtts = 0;

            var processed = 0;
            var totalScenes = sceneGeneratedMap.Count;
            foreach (var (coveSceneId, generatedData) in sceneGeneratedMap)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                if (!string.IsNullOrWhiteSpace(generatedData.CoverBlobId))
                {
                    sourceScreenshots++;
                    if (await TryWriteSceneScreenshotAsync(coveSceneId, generatedData.CoverBlobId!, ct))
                        migratedScreenshots++;
                }

                var previewHash = ResolveGeneratedHash(generatedData, stashConfig.VideoFileNamingAlgorithm, previewHashes);
                if (!string.IsNullOrWhiteSpace(previewHash))
                {
                    sourcePreviews++;
                    var srcPreviewPath = Path.Combine(stashScreenshotsDir, $"{previewHash}.mp4");
                    if (TryCopyGeneratedFile(srcPreviewPath, GetCoveScenePreviewPath(coveSceneId)))
                        migratedPreviews++;
                }

                var spriteHash = ResolveGeneratedHash(generatedData, stashConfig.VideoFileNamingAlgorithm, spriteHashes);
                if (!string.IsNullOrWhiteSpace(spriteHash))
                {
                    sourceSprites++;
                    var srcSpritePath = Path.Combine(stashVttDir, $"{spriteHash}_sprite.jpg");
                    if (TryCopyGeneratedFile(srcSpritePath, GetCoveSceneSpritePath(coveSceneId)))
                        migratedSprites++;
                }

                var vttHash = ResolveGeneratedHash(generatedData, stashConfig.VideoFileNamingAlgorithm, vttHashes);
                if (!string.IsNullOrWhiteSpace(vttHash))
                {
                    sourceVtts++;
                    var srcVttPath = Path.Combine(stashVttDir, $"{vttHash}_thumbs.vtt");
                    if (TryCopyGeneratedFile(srcVttPath, GetCoveSceneSpriteVttPath(coveSceneId)))
                        migratedVtts++;
                }

                if (processed % 25 == 0 || processed == totalScenes)
                    ReportPhase(progress, startProgress, endProgress, processed, totalScenes, $"Copying generated assets ({processed}/{totalScenes})");
            }

            _logger.LogInformation(
                "Migrated generated scene assets from Stash: screenshots {MigratedScreenshots}/{SourceScreenshots}, previews {MigratedPreviews}/{SourcePreviews}, sprites {MigratedSprites}/{SourceSprites}, vtt {MigratedVtts}/{SourceVtts}",
                migratedScreenshots,
                sourceScreenshots,
                migratedPreviews,
                sourcePreviews,
                migratedSprites,
                sourceSprites,
                migratedVtts,
                sourceVtts);

            progress.Report(endProgress, "Generated scene assets copied");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy generated content");
        }
    }

    private static string? ResolveGeneratedHash(SceneGeneratedData generatedData, string preferredAlgorithm, HashSet<string> availableHashes)
    {
        if (availableHashes.Count == 0) return null;

        foreach (var candidate in EnumerateHashCandidates(generatedData, preferredAlgorithm))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && availableHashes.Contains(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string?> EnumerateHashCandidates(SceneGeneratedData generatedData, string preferredAlgorithm)
    {
        if (string.Equals(preferredAlgorithm, "MD5", StringComparison.OrdinalIgnoreCase))
        {
            yield return generatedData.Md5;
            yield return generatedData.Oshash;
            yield break;
        }

        yield return generatedData.Oshash;
        yield return generatedData.Md5;
    }

    private bool TryCopyGeneratedFile(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        return File.Exists(destinationPath);
    }

    private async Task<bool> TryWriteSceneScreenshotAsync(int sceneId, string blobId, CancellationToken ct)
    {
        try
        {
            var blob = await _blobService.GetBlobAsync(blobId, ct);
            if (blob == null) return false;

            await using var blobStream = blob.Value.Stream;
            var destinationPath = GetCoveSceneThumbnailPath(sceneId);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            if (string.Equals(blob.Value.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                await using var jpegOutput = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await blobStream.CopyToAsync(jpegOutput, ct);
                return File.Exists(destinationPath);
            }

            await using var buffered = new MemoryStream();
            await blobStream.CopyToAsync(buffered, ct);
            buffered.Position = 0;

            using var image = await SixLabors.ImageSharp.Image.LoadAsync(buffered, ct);
            await using var convertedOutput = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await image.SaveAsync(convertedOutput, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 }, ct);
            return File.Exists(destinationPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SixLabors.ImageSharp.InvalidImageContentException ex)
        {
            _logger.LogWarning("Skipping corrupt scene screenshot for scene {SceneId} from blob {BlobId}: {Message}", sceneId, blobId, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to migrate scene screenshot for scene {SceneId} from blob {BlobId}", sceneId, blobId);
            return false;
        }
    }

    private string GetCoveSceneThumbnailPath(int sceneId)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(sceneId)));
        return Path.Combine(_config.GeneratedPath, "screenshots", hash[..2], $"{sceneId}.jpg");
    }

    private string GetCoveScenePreviewPath(int sceneId)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(sceneId)));
        return Path.Combine(_config.GeneratedPath, "previews", hash[..2], $"{sceneId}.mp4");
    }

    private string GetCoveSceneSpritePath(int sceneId)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(sceneId)));
        return Path.Combine(_config.GeneratedPath, "vtt", hash[..2], $"{sceneId}_sprite.jpg");
    }

    private string GetCoveSceneSpriteVttPath(int sceneId)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(sceneId)));
        return Path.Combine(_config.GeneratedPath, "vtt", hash[..2], $"{sceneId}_thumbs.vtt");
    }

    private static string DetectImageContentType(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        var buffer = new byte[256];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        if (stream.CanSeek)
            stream.Position = originalPosition;

        if (bytesRead >= 4)
        {
            if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
                return "image/png";

            if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
                return "image/jpeg";

            if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x38)
                return "image/gif";

            if (buffer[0] == 0x42 && buffer[1] == 0x4D)
                return "image/bmp";
        }

        if (bytesRead >= 12)
        {
            if (buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46
                && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
            {
                return "image/webp";
            }

            if (buffer[4] == 0x66 && buffer[5] == 0x74 && buffer[6] == 0x79 && buffer[7] == 0x70)
            {
                var brand = Encoding.ASCII.GetString(buffer, 8, 4);
                if (brand.StartsWith("avif", StringComparison.OrdinalIgnoreCase))
                    return "image/avif";
                if (brand.StartsWith("heic", StringComparison.OrdinalIgnoreCase))
                    return "image/heic";
            }
        }

        if (bytesRead >= 2 && buffer[0] == 0xFF && buffer[1] == 0x0A)
            return "image/jxl";

        if (bytesRead >= 8
            && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0x00 && buffer[3] == 0x0C
            && buffer[4] == 0x4A && buffer[5] == 0x58 && buffer[6] == 0x4C && buffer[7] == 0x20)
        {
            return "image/jxl";
        }

        if (bytesRead > 0 && buffer[0] == 0x3C)
        {
            var head = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            if (head.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                return "image/svg+xml";
        }

        return "image/jpeg";
    }

    private static StashConfigData ParseStashConfig(string configPath)
    {
        var paths = new List<(string Path, bool ExcludeImage, bool ExcludeVideo)>();
        string? generatedPath = null;
        string? videoFileNamingAlgorithm = null;
        bool? calculateMd5 = null;

        try
        {
            var lines = File.ReadAllLines(configPath);
            var inStashArray = false;
            string? currentPath = null;
            var currentExcludeImage = false;
            var currentExcludeVideo = false;

            foreach (var rawLine in lines)
            {
                var genMatch = Regex.Match(rawLine, @"^generated:\s*(.+)$");
                if (genMatch.Success)
                {
                    generatedPath = genMatch.Groups[1].Value.Trim().Trim('"', '\'');
                    continue;
                }

                var algoMatch = Regex.Match(rawLine, @"^video_file_naming_algorithm:\s*(.+)$", RegexOptions.IgnoreCase);
                if (algoMatch.Success)
                {
                    videoFileNamingAlgorithm = algoMatch.Groups[1].Value.Trim().Trim('"', '\'');
                    continue;
                }

                var md5Match = Regex.Match(rawLine, @"^calculate_md5:\s*(true|false)$", RegexOptions.IgnoreCase);
                if (md5Match.Success)
                {
                    calculateMd5 = string.Equals(md5Match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (rawLine.TrimStart().StartsWith("stash:"))
                {
                    inStashArray = true;
                    continue;
                }

                if (inStashArray && rawLine.Length > 0 && !char.IsWhiteSpace(rawLine[0]) && !rawLine.TrimStart().StartsWith("-"))
                {
                    if (currentPath != null)
                    {
                        paths.Add((currentPath, currentExcludeImage, currentExcludeVideo));
                        currentPath = null;
                    }
                    inStashArray = false;
                    continue;
                }

                if (!inStashArray) continue;

                var trimmed = rawLine.TrimStart();
                if (trimmed.StartsWith("- "))
                {
                    if (currentPath != null)
                        paths.Add((currentPath, currentExcludeImage, currentExcludeVideo));
                    currentPath = null;
                    currentExcludeImage = false;
                    currentExcludeVideo = false;
                    trimmed = trimmed[2..].TrimStart();
                }

                var pathMatch = Regex.Match(trimmed, @"^path:\s*(.+)$");
                if (pathMatch.Success)
                {
                    currentPath = pathMatch.Groups[1].Value.Trim().Trim('"', '\'');
                    continue;
                }

                var exImgMatch = Regex.Match(trimmed, @"^excludeimage:\s*(true|false)$", RegexOptions.IgnoreCase);
                if (exImgMatch.Success)
                {
                    currentExcludeImage = string.Equals(exImgMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                var exVidMatch = Regex.Match(trimmed, @"^excludevideo:\s*(true|false)$", RegexOptions.IgnoreCase);
                if (exVidMatch.Success)
                {
                    currentExcludeVideo = string.Equals(exVidMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
            }

            if (inStashArray && currentPath != null)
                paths.Add((currentPath, currentExcludeImage, currentExcludeVideo));
        }
        catch (Exception)
        {
        }

        return new StashConfigData(
            paths,
            generatedPath,
            videoFileNamingAlgorithm ?? (calculateMd5 == true ? "MD5" : "OSHASH"));
    }
}