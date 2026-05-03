using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Data.Common;

namespace Cove.Api.Services;

public record StashPreviewResult(bool IsValid, string? Error, int Scenes, int Performers, int Tags, int Studios, int Groups, int Images, int Galleries);
public record StashImportResult(int Scenes, int Performers, int Tags, int Studios, int Groups, int Images, int Galleries);
public record StashAiImportResult(int AiRuns, int Segments);
public record StashImportOptions(string? CoveGeneratedPath, bool MigrateGeneratedContent = true, string? AiDataSource = null);

public sealed class StashMigrationInProgressException(string message) : InvalidOperationException(message);

public partial class StashMigrationService
{
    private readonly CoveContext _db;
    private readonly IBlobService _blobService;
    private readonly ConfigService _configService;
    private readonly CoveConfiguration _config;
    private readonly IJobService _jobService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StashMigrationService> _logger;

    private sealed record SceneGeneratedData(string? Oshash, string? Md5, string? CoverBlobId);
    private sealed record StashConfigData(
        List<(string Path, bool ExcludeImage, bool ExcludeVideo)> Paths,
        string? GeneratedPath,
        string VideoFileNamingAlgorithm);

    private static readonly object ImportSync = new();
    private static readonly Queue<string> importResultOrder = new();
    private static readonly Dictionary<string, StashImportResult> importResults = [];
    private static readonly Queue<string> aiImportResultOrder = new();
    private static readonly Dictionary<string, StashAiImportResult> aiImportResults = [];
    private static string? activeImportPath;
    private static string? activeImportJobId;
    private static string? activeAiImportPath;
    private static string? activeAiImportJobId;

    private const double BlobsStart = 0.02;
    private const double BlobsEnd = 0.08;
    private const double FoldersStart = 0.08;
    private const double FoldersEnd = 0.12;
    private const double StudiosStart = 0.12;
    private const double StudiosEnd = 0.18;
    private const double TagsStart = 0.18;
    private const double TagsEnd = 0.24;
    private const double PerformersStart = 0.24;
    private const double PerformersEnd = 0.34;
    private const double GroupsStart = 0.34;
    private const double GroupsEnd = 0.38;
    private const double ScenesStart = 0.38;
    private const double ScenesEnd = 0.68;
    private const double SceneMarkersStart = 0.68;
    private const double SceneMarkersEnd = 0.72;
    private const double ImagesStart = 0.72;
    private const double ImagesEnd = 0.93;
    private const double GalleriesStart = 0.93;
    private const double GalleriesEnd = 0.97;
    private const double AiDataStart = 0.97;
    private const double AiDataEnd = 0.985;
    private const double LibraryPathsStart = 0.985;
    private const double LibraryPathsEnd = 0.9925;
    private const double GeneratedAssetsStart = 0.9925;
    private const double GeneratedAssetsEnd = 1.0;

    public StashMigrationService(
        CoveContext db,
        IBlobService blobService,
        ConfigService configService,
        CoveConfiguration config,
        IJobService jobService,
        IServiceScopeFactory scopeFactory,
        ILogger<StashMigrationService> logger)
    {
        _db = db;
        _blobService = blobService;
        _configService = configService;
        _config = config;
        _jobService = jobService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<StashPreviewResult> PreviewAsync(string stashDbPath, CancellationToken ct = default)
    {
        if (!File.Exists(stashDbPath))
            return new StashPreviewResult(false, $"Database file not found: {stashDbPath}", 0, 0, 0, 0, 0, 0, 0);
        try
        {
            var cs = OpenReadOnly(stashDbPath);
            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync(ct);
            return new StashPreviewResult(true, null,
                await CountAsync(conn, "scenes", ct),
                await CountAsync(conn, "performers", ct),
                await CountAsync(conn, "tags", ct),
                await CountAsync(conn, "studios", ct),
                await CountAsync(conn, "groups", ct),
                await CountAsync(conn, "images", ct),
                await CountAsync(conn, "galleries", ct));
        }
        catch (Exception ex)
        {
            return new StashPreviewResult(false, ex.Message, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    public Task<StashImportResult> ImportAsync(string stashDbPath, StashImportOptions? options = null, CancellationToken ct = default)
    {
        return RunImportAsync(stashDbPath, options, NullJobProgress.Instance, ct);
    }

    public string StartImport(string stashDbPath, StashImportOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(stashDbPath))
            throw new ArgumentException("Stash database path is required.", nameof(stashDbPath));

        options ??= new StashImportOptions(null, true);

        lock (ImportSync)
        {
            if (!string.IsNullOrWhiteSpace(activeImportJobId) || !string.IsNullOrWhiteSpace(activeAiImportJobId))
            {
                if (!string.IsNullOrWhiteSpace(activeImportJobId)
                    && string.Equals(activeImportPath, stashDbPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Stash migration already running for {Path}; joining existing import", stashDbPath);
                    return activeImportJobId;
                }

                var activePath = activeImportPath ?? activeAiImportPath;
                throw new StashMigrationInProgressException($"A Stash migration is already running for {activePath}.");
            }

            activeImportPath = stashDbPath;
            string? jobId = null;
            jobId = _jobService.Enqueue("stash-import", "Importing Stash library", async (progress, ct) =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedMigration = scope.ServiceProvider.GetRequiredService<StashMigrationService>();
                    var result = await scopedMigration.RunImportAsync(stashDbPath, options, progress, ct);

                    lock (ImportSync)
                    {
                        importResults[jobId!] = result;
                        importResultOrder.Enqueue(jobId!);
                        TrimImportResultsLocked();
                    }
                }
                finally
                {
                    lock (ImportSync)
                    {
                        if (string.Equals(activeImportJobId, jobId, StringComparison.OrdinalIgnoreCase))
                        {
                            activeImportJobId = null;
                            activeImportPath = null;
                        }
                    }
                }
            });

            activeImportJobId = jobId;
            return jobId;
        }
    }

    public string StartAiTagImport(string stashDbPath, string aiDataSource)
    {
        if (string.IsNullOrWhiteSpace(stashDbPath))
            throw new ArgumentException("Stash database path is required.", nameof(stashDbPath));
        if (string.IsNullOrWhiteSpace(aiDataSource))
            throw new ArgumentException("AI data source is required.", nameof(aiDataSource));

        lock (ImportSync)
        {
            if (!string.IsNullOrWhiteSpace(activeImportJobId) || !string.IsNullOrWhiteSpace(activeAiImportJobId))
            {
                if (!string.IsNullOrWhiteSpace(activeAiImportJobId)
                    && string.Equals(activeAiImportPath, stashDbPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("AI tag import already running for {Path}; joining existing job", stashDbPath);
                    return activeAiImportJobId;
                }

                var activePath = activeImportPath ?? activeAiImportPath;
                throw new StashMigrationInProgressException($"A Stash migration is already running for {activePath}.");
            }

            activeAiImportPath = stashDbPath;
            string? jobId = null;
            jobId = _jobService.Enqueue("stash-ai-import", "Importing AI tag data", async (progress, ct) =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedMigration = scope.ServiceProvider.GetRequiredService<StashMigrationService>();
                    var result = await scopedMigration.RunAiTagImportAsync(stashDbPath, aiDataSource, progress, ct);

                    lock (ImportSync)
                    {
                        aiImportResults[jobId!] = result;
                        aiImportResultOrder.Enqueue(jobId!);
                        TrimAiImportResultsLocked();
                    }
                }
                finally
                {
                    lock (ImportSync)
                    {
                        if (string.Equals(activeAiImportJobId, jobId, StringComparison.OrdinalIgnoreCase))
                        {
                            activeAiImportJobId = null;
                            activeAiImportPath = null;
                        }
                    }
                }
            });

            activeAiImportJobId = jobId;
            return jobId;
        }
    }

    public StashImportResult? GetImportResult(string jobId)
    {
        lock (ImportSync)
        {
            return importResults.TryGetValue(jobId, out var result) ? result : null;
        }
    }

    public StashAiImportResult? GetAiImportResult(string jobId)
    {
        lock (ImportSync)
        {
            return aiImportResults.TryGetValue(jobId, out var result) ? result : null;
        }
    }

    public async Task<StashImportResult> RunImportAsync(string stashDbPath, StashImportOptions? options, IJobProgress progress, CancellationToken ct = default)
    {
        options ??= new StashImportOptions(null, true);
        progress.Report(0.01, "Opening Stash database...");
        var result = await ImportCoreAsync(stashDbPath, options, progress, ct);
        progress.Report(1.0, "Import complete");
        return result;
    }

    public async Task<StashAiImportResult> RunAiTagImportAsync(string stashDbPath, string aiDataSource, IJobProgress progress, CancellationToken ct = default)
    {
        if (!File.Exists(stashDbPath))
            throw new FileNotFoundException($"Database file not found: {stashDbPath}", stashDbPath);
        if (string.IsNullOrWhiteSpace(aiDataSource))
            throw new ArgumentException("AI data source is required.", nameof(aiDataSource));

        progress.Report(0.02, "Opening Stash database...");
        await using var conn = new SqliteConnection(OpenReadOnly(stashDbPath));
        await conn.OpenAsync(ct);

        var sceneIdMap = await BuildExistingSceneIdMapAsync(ct);
        if (sceneIdMap.Count == 0)
            throw new InvalidOperationException("No imported Stash scenes were found. Run the main Stash migration before importing AI tag data.");

        var tagNameToCoveIdMap = await BuildCoveTagNameMapAsync(ct);
        var tagIdMap = await BuildExistingTagIdMapAsync(conn, tagNameToCoveIdMap, ct);
        if (tagIdMap.Count == 0)
            throw new InvalidOperationException("No imported Stash tag mappings were found. Ensure your Stash tags were imported before importing AI tag data.");

        var (aiRunCount, segmentCount) = await ImportAiTagDataAsync(
            aiDataSource,
            sceneIdMap,
            new Dictionary<int, int>(),
            tagIdMap,
            tagNameToCoveIdMap,
            progress,
            0.08,
            1.0,
            ct);

        progress.Report(1.0, "AI tag import complete");
        return new StashAiImportResult(aiRunCount, segmentCount);
    }

    private async Task<StashImportResult> ImportCoreAsync(string stashDbPath, StashImportOptions options, IJobProgress progress, CancellationToken ct)
    {
        if (!File.Exists(stashDbPath))
            throw new FileNotFoundException($"Database file not found: {stashDbPath}", stashDbPath);

        await using var conn = new SqliteConnection(OpenReadOnly(stashDbPath));
        await conn.OpenAsync(ct);
        _logger.LogInformation("Starting Stash migration from {Path}", stashDbPath);

        await ApplyCoveGeneratedPathOverrideAsync(options.CoveGeneratedPath, ct);

        var blobMap = await ImportBlobsAsync(conn, progress, BlobsStart, BlobsEnd, ct);
        var folderIdMap = await ImportFoldersAsync(conn, progress, FoldersStart, FoldersEnd, ct);
        _db.ChangeTracker.Clear();

        var studioIdMap = await ImportStudiosAsync(conn, blobMap, progress, StudiosStart, StudiosEnd, ct);
        _db.ChangeTracker.Clear();

        var tagIdMap = await ImportTagsAsync(conn, blobMap, progress, TagsStart, TagsEnd, ct);
        _db.ChangeTracker.Clear();

        var performerIdMap = await ImportPerformersAsync(conn, blobMap, tagIdMap, progress, PerformersStart, PerformersEnd, ct);
        _db.ChangeTracker.Clear();

        var groupIdMap = await ImportGroupsAsync(conn, studioIdMap, progress, GroupsStart, GroupsEnd, ct);
        _db.ChangeTracker.Clear();

        var (sceneCount, sceneIdMap, sceneGeneratedMap) = await ImportScenesAsync(conn, blobMap, folderIdMap, studioIdMap, tagIdMap, performerIdMap, groupIdMap, progress, ScenesStart, ScenesEnd, ct);
        _db.ChangeTracker.Clear();

        await ImportSceneMarkerSegmentsAsync(conn, sceneIdMap, tagIdMap, progress, SceneMarkersStart, SceneMarkersEnd, ct);
        _db.ChangeTracker.Clear();

        var imageIdMap = await ImportImagesAsync(conn, folderIdMap, studioIdMap, tagIdMap, performerIdMap, progress, ImagesStart, ImagesEnd, ct);
        _db.ChangeTracker.Clear();

        var (galleryCount, galleryFileIdMap) = await ImportGalleriesAsync(conn, folderIdMap, studioIdMap, tagIdMap, performerIdMap, imageIdMap, progress, GalleriesStart, GalleriesEnd, ct);
        _db.ChangeTracker.Clear();

        await ReconcileImportedZipLinksAsync(conn, folderIdMap, imageIdMap, galleryFileIdMap, ct);
        _db.ChangeTracker.Clear();

        IReadOnlyDictionary<string, int> tagNameToCoveIdMap = string.IsNullOrWhiteSpace(options.AiDataSource)
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : await BuildStashTagNameToCoveIdMapAsync(conn, tagIdMap, ct);

        await ImportAiTagDataAsync(options.AiDataSource, sceneIdMap, imageIdMap, tagIdMap, tagNameToCoveIdMap, progress, AiDataStart, AiDataEnd, ct);
        _db.ChangeTracker.Clear();

        progress.Report(LibraryPathsStart, "Importing library paths...");
        await ImportLibraryPathsAsync(stashDbPath, ct);
        progress.Report(LibraryPathsEnd, "Library paths imported");

        if (options.MigrateGeneratedContent)
        {
            await CopyGeneratedContentAsync(stashDbPath, sceneGeneratedMap, options, progress, GeneratedAssetsStart, GeneratedAssetsEnd, ct);
        }
        else
        {
            _logger.LogInformation("Skipping generated content migration for {Path}", stashDbPath);
            progress.Report(GeneratedAssetsEnd, "Skipping generated scene assets");
        }

            _logger.LogInformation("Migration complete: {S} scenes, {P} performers, {T} tags, {St} studios, {G} groups, {I} images, {Ga} galleries",
            sceneCount, performerIdMap.Count, tagIdMap.Count, studioIdMap.Count, groupIdMap.Count, imageIdMap.Count, galleryCount);

        return new StashImportResult(sceneCount, performerIdMap.Count, tagIdMap.Count, studioIdMap.Count, groupIdMap.Count, imageIdMap.Count, galleryCount);
    }
}
