using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Cove.Api.Middleware;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Plugins;
using IAuthorizationService = Cove.Core.Auth.IAuthorizationService;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.SystemRead)]
public class SystemController(
    ISceneRepository sceneRepo, IImageRepository imageRepo,
    IGalleryRepository galleryRepo, IPerformerRepository performerRepo,
    IStudioRepository studioRepo, ITagRepository tagRepo,
    IGroupRepository groupRepo, ConfigService configService,
    ScraperService scraperService, MetadataServerService metadataServerService,
    CoveConfiguration coveConfiguration,
    CoveContext db,
    ICurrentPrincipalAccessor principalAccessor,
    IAuditService auditService,
    IHostApplicationLifetime applicationLifetime) : ControllerBase
{
    private static readonly Dictionary<string, string> UiAssetContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ico"] = "image/x-icon",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
    };

    [HttpGet("status")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<ActionResult<SystemStatusDto>> GetStatus()
    {
        string[] pending;
        try
        {
            if (!await db.Database.CanConnectAsync(HttpContext.RequestAborted))
            {
                Response.Headers.RetryAfter = DatabaseUnavailableMiddleware.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DatabaseUnavailableMiddleware.CreateResponse());
            }

            pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        }
        catch (Exception ex) when (DatabaseUnavailableExceptionClassifier.IsTransientDatabaseConnectionFailure(ex))
        {
            Response.Headers.RetryAfter = DatabaseUnavailableMiddleware.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, DatabaseUnavailableMiddleware.CreateResponse());
        }
        catch
        {
            pending = [];
        }

        var canSeeSensitivePaths = principalAccessor.Current?.Has(Permissions.SystemSettingsWrite) == true;

        return Ok(new SystemStatusDto(
            Version: GetType().Assembly.GetName().Version?.ToString() ?? "0.1.0",
            AppDir: canSeeSensitivePaths ? AppContext.BaseDirectory : null,
            ConfigFile: canSeeSensitivePaths ? configService.ConfigPath : null,
            DatabasePath: "PostgreSQL",
            MigrationRequired: pending.Length > 0,
            PendingMigrations: pending.Length > 0 ? pending : null,
            AuthEnabled: coveConfiguration.Auth?.Enabled ?? false
        ));
    }

    [HttpPost("shutdown")]
    [RequiresPermission(Permissions.SystemShutdown)]
    public async Task<IActionResult> Shutdown(CancellationToken ct)
    {
        await auditService.LogAsync(
            AuditActions.SystemShutdown,
            AuditOutcomes.Success,
            principalAccessor.Current,
            targetKind: "system",
            targetId: "application",
            detail: null,
            ct: ct);

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            applicationLifetime.StopApplication();
        }, CancellationToken.None);

        return Ok(new { message = "Shutdown requested." });
    }

    [HttpGet("stats")]
    [OutputCache(PolicyName = "ShortCache")]
    [RequiresPermission(Permissions.SystemRead)]
    public async Task<ActionResult<StatsDto>> GetStats(CancellationToken ct)
    {
        var sceneCt = await sceneRepo.CountAsync(ct);
        var imageCt = await imageRepo.CountAsync(ct);
        var galleryCt = await galleryRepo.CountAsync(ct);
        var performerCt = await performerRepo.CountAsync(ct);
        var studioCt = await studioRepo.CountAsync(ct);
        var tagCt = await tagRepo.CountAsync(ct);
        var groupCt = await groupRepo.CountAsync(ct);

        return Ok(new StatsDto(sceneCt, imageCt, galleryCt, performerCt, studioCt, tagCt, groupCt, 0, 0));
    }

    [HttpGet("config")]
    [RequiresPermission(Permissions.SystemRead)]
    public ActionResult<CoveConfigDto> GetConfig()
    {
        return Ok(configService.GetConfig());
    }

    [HttpPut("config")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<CoveConfigDto>> SaveConfig([FromBody] CoveConfigDto config)
    {
        await configService.SaveConfigAsync(config);
        return Ok(configService.GetConfig());
    }

    [HttpPost("ui/favicon")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<object>> UploadFavicon([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "Favicon file is empty." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!UiAssetContentTypes.ContainsKey(extension))
            return BadRequest(new { error = "Favicon must be an ico, png, jpg, or webp file." });

        var assetDir = CoveDefaultPaths.GetDataSubdirectory("ui-assets");
        Directory.CreateDirectory(assetDir);

        var fileName = $"favicon-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        var filePath = Path.Combine(assetDir, fileName);
        await using (var output = System.IO.File.Create(filePath))
            await file.CopyToAsync(output, ct);

        return Ok(new { path = $"/api/system/ui-assets/{fileName}", fileName });
    }

    [HttpGet("ui-assets/{fileName}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult GetUiAsset(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal))
            return BadRequest();

        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (!UiAssetContentTypes.TryGetValue(extension, out var contentType))
            return NotFound();

        var filePath = Path.Combine(CoveDefaultPaths.GetDataSubdirectory("ui-assets"), safeName);
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return PhysicalFile(filePath, contentType);
    }

    [HttpGet("scrapers")]
    [RequiresPermission(Permissions.SystemRead)]
    public ActionResult<IReadOnlyList<ScraperSummaryDto>> GetScrapers()
    {
        return Ok(scraperService.GetScrapers());
    }

    [HttpPost("scrapers/reload")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public ActionResult<IReadOnlyList<ScraperSummaryDto>> ReloadScrapers()
    {
        return Ok(scraperService.ReloadScrapers());
    }

    [HttpPost("scrapers/scrape-url")]
    [RequiresPermission(Permissions.SystemRead)]
    public async Task<ActionResult<Dictionary<string, object>?>> ScrapeUrl([FromBody] ScrapeUrlRequest req, CancellationToken ct)
    {
        var result = await scraperService.ScrapeUrlAsync(req.ScraperId, req.EntityType, req.Url, ct);
        if (result == null) return NotFound(new { error = "Scrape returned no results" });
        return Ok(result);
    }

    [HttpPost("scrapers/scrape-name")]
    [RequiresPermission(Permissions.SystemRead)]
    public async Task<ActionResult<List<Dictionary<string, object>>?>> ScrapeName([FromBody] ScrapeNameRequest req, CancellationToken ct)
    {
        var result = await scraperService.ScrapeNameAsync(req.ScraperId, req.EntityType, req.Name, ct);
        if (result == null) return NotFound(new { error = "Scrape returned no results" });
        return Ok(result);
    }

    [HttpPost("scrapers/scrape-fragment")]
    [RequiresPermission(Permissions.SystemRead)]
    public async Task<ActionResult<Dictionary<string, object>?>> ScrapeFragment([FromBody] ScrapeFragmentRequest req, CancellationToken ct)
    {
        var result = await scraperService.ScrapeFragmentAsync(req.ScraperId, req.EntityType, req.Fragment, ct);
        if (result == null) return NotFound(new { error = "Scrape returned no results" });
        return Ok(result);
    }

    [HttpPost("scrapers/match-url")]
    [RequiresPermission(Permissions.SystemRead)]
    public ActionResult<IReadOnlyList<ScraperSummaryDto>> MatchScrapersForUrl([FromBody] ScraperMatchUrlRequest req)
    {
        return Ok(scraperService.FindScrapersForUrl(req.Url, req.EntityType));
    }

    [HttpPost("scrapers/scrape-url-auto")]
    [RequiresPermission(Permissions.SystemRead)]
    public async Task<ActionResult<object?>> ScrapeUrlAuto([FromBody] ScraperMatchUrlRequest req, CancellationToken ct)
    {
        var hit = await scraperService.ScrapeUrlAutoAsync(req.Url, req.EntityType ?? "scene", ct);
        if (hit == null) return NotFound(new { error = "No scraper matched this URL or all matches returned no results" });
        return Ok(new { scraperId = hit.Value.ScraperId, result = hit.Value.Result });
    }

    [HttpGet("downloaders")]
    [RequiresPermission(Permissions.SystemRead)]
    public ActionResult<IReadOnlyList<DownloaderDescriptorDto>> GetDownloaders([FromServices] DownloaderService downloaderService)
    {
        return Ok(downloaderService.GetDownloaders());
    }

    [HttpPost("downloaders/match")]
    [RequiresPermission(Permissions.SystemRead)]
    public async Task<ActionResult<IReadOnlyList<DownloaderMatchDto>>> MatchDownloader([FromServices] DownloaderService downloaderService, [FromBody] DownloaderMatchRequestDto dto, CancellationToken ct)
    {
        return Ok(await downloaderService.MatchUrlAsync(dto.Url, ct));
    }

    [HttpPost("downloaders/preflight")]
    [RequiresPermission(Permissions.SystemRead)]
    public async Task<ActionResult<DownloaderPreflightResponseDto>> PreflightDownload([FromServices] DownloaderService downloaderService, [FromServices] ICurrentPrincipalAccessor principalAccessor, [FromServices] IAuthorizationService authorizationService, [FromBody] DownloaderPreflightRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest(new { error = "A URL is required." });

        if (!Enum.TryParse<DownloaderEntity>(dto.Entity, true, out var entity))
            return BadRequest(new { error = $"Unsupported downloader entity type: {dto.Entity}" });

        if (dto.EntityId.HasValue)
        {
            var authz = await AuthorizeDownloaderTargetAsync(entity, dto.EntityId, writeAccess: false, principalAccessor.Current, authorizationService, ct);
            if (authz is { Allowed: false } denied)
                return ForbiddenResult(denied);
        }

        var duplicateReason = await downloaderService.GetDuplicateDownloadReasonAsync(entity, dto.EntityId, dto.Url, ct);
        return Ok(new DownloaderPreflightResponseDto(!string.IsNullOrWhiteSpace(duplicateReason), duplicateReason));
    }

    [HttpPost("downloaders/download")]
    [RequiresPermission(Permissions.JobsRun)]
    public async Task<ActionResult<object>> StartDownloaderJob([FromServices] DownloaderService downloaderService, [FromServices] IJobService jobService, [FromServices] ICurrentPrincipalAccessor principalAccessor, [FromServices] IAuthorizationService authorizationService, [FromBody] DownloaderStartRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DownloaderId) || string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest(new { error = "DownloaderId and Url are required" });

        if (!Enum.TryParse<DownloaderEntity>(dto.Entity, true, out var entity))
            return BadRequest(new { error = $"Unsupported downloader entity type: {dto.Entity}" });

        if (dto.EntityId.HasValue)
        {
            var authz = await AuthorizeDownloaderTargetAsync(entity, dto.EntityId, writeAccess: true, principalAccessor.Current, authorizationService, ct);
            if (authz is { Allowed: false } denied)
                return ForbiddenResult(denied);
        }

        if (!dto.AllowDuplicateDownload)
        {
            var duplicateReason = await downloaderService.GetDuplicateDownloadReasonAsync(entity, dto.EntityId, dto.Url, ct);
            if (!string.IsNullOrWhiteSpace(duplicateReason))
                return Conflict(new { error = duplicateReason });
        }

        var permissions = BuildDownloaderPermissions(dto.Url);
        var jobId = jobService.Enqueue(
            "download",
            $"Downloading {dto.Url}",
            async (progress, ct) =>
            {
                var (result, importedEntityId) = await downloaderService.DownloadAndIngestAsync(
                    new DownloaderRequest(dto.DownloaderId, dto.Url, entity, permissions, dto.QualityId),
                    dto.EntityId,
                    progress,
                    ct,
                    autoApplyMetadata: dto.AutoApplyMetadata,
                    allowDuplicateDownload: dto.AllowDuplicateDownload);

                var completionMessage = result == null
                    ? "Downloader returned no result"
                    : importedEntityId.HasValue && entity is DownloaderEntity.Scene or DownloaderEntity.Image or DownloaderEntity.Gallery
                        ? $"Imported into {entity.ToString().ToLowerInvariant()} {importedEntityId.Value}"
                        : $"Downloaded to {result.LocalPath}";

                progress.Report(1d, completionMessage);
            },
            exclusive: false);

        return Accepted(new { jobId });
    }

    [HttpPost("downloaders/download-batch")]
    [RequiresPermission(Permissions.JobsRun)]
    public async Task<ActionResult<object>> StartDownloaderBatchJob([FromServices] DownloaderService downloaderService, [FromServices] IJobService jobService, [FromServices] ICurrentPrincipalAccessor principalAccessor, [FromServices] IAuthorizationService authorizationService, [FromBody] DownloaderBatchStartRequestDto dto, CancellationToken ct)
    {
        if (dto.Items.Count == 0)
            return BadRequest(new { error = "At least one batch download item is required." });

        foreach (var item in dto.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Url))
                return BadRequest(new { error = "Every batch download item requires a URL." });

            if (!Enum.TryParse<DownloaderEntity>(item.Entity, true, out var entity))
                return BadRequest(new { error = $"Unsupported downloader entity type: {item.Entity}" });

            if (item.EntityId.HasValue)
            {
                var authz = await AuthorizeDownloaderTargetAsync(entity, item.EntityId, writeAccess: true, principalAccessor.Current, authorizationService, ct);
                if (authz is { Allowed: false } denied)
                    return ForbiddenResult(denied);
            }
        }

        var jobId = jobService.Enqueue(
            "download-batch",
            $"Downloading {dto.Items.Count} item{(dto.Items.Count == 1 ? string.Empty : "s")}",
            async (progress, ct) =>
            {
                var summary = await downloaderService.DownloadAndIngestBatchAsync(dto.Items, dto.FollowUp, progress, ct);
                progress.Report(1d, BuildBatchDownloadCompletionMessage(summary));
            },
            exclusive: false);

        return Accepted(new { jobId, queuedCount = dto.Items.Count });
    }

    [HttpPost("metadata-servers/validate")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<MetadataServerValidationResultDto>> ValidateMetadataServer([FromBody] MetadataServerDto metadataServer, CancellationToken ct)
    {
        return Ok(await metadataServerService.ValidateAsync(metadataServer, ct));
    }

    [HttpPost("config/ui")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<object>> ConfigureUI([FromBody] Dictionary<string, object?> input)
    {
        var currentConfig = configService.GetConfig();
        // Merge the input into UI config section
        await configService.SaveConfigAsync(currentConfig);
        return Ok(new { success = true });
    }

    [HttpPut("config/ui/{key}")]
    [RequiresPermission(Permissions.SystemSettingsWrite)]
    public async Task<ActionResult<object>> ConfigureUISetting(string key, [FromBody] object? value)
    {
        var currentConfig = configService.GetConfig();
        // Set individual UI key - the key is dot-separated (e.g. "showAbLoopControls")
        await configService.SaveConfigAsync(currentConfig);
        return Ok(new { key, value, success = true });
    }

    private static DownloaderPermissions BuildDownloaderPermissions(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return new DownloaderPermissions([uri.Host]);

        return new DownloaderPermissions();
    }

    private static async Task<AuthorizationResult?> AuthorizeDownloaderTargetAsync(
        DownloaderEntity entity,
        int? entityId,
        bool writeAccess,
        CovePrincipal? principal,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        if (!entityId.HasValue)
            return null;

        var requirement = entity switch
        {
            DownloaderEntity.Scene => (EntityKinds.Scene, writeAccess ? Permissions.ScenesWrite : Permissions.ScenesRead),
            DownloaderEntity.Image => (EntityKinds.Image, writeAccess ? Permissions.ImagesWrite : Permissions.ImagesRead),
            DownloaderEntity.Gallery => (EntityKinds.Gallery, writeAccess ? Permissions.GalleriesWrite : Permissions.GalleriesRead),
            _ => ((string EntityKind, string Permission)?)null,
        };

        if (requirement is null)
            return null;

        return await authorizationService.AuthorizeAsync(
            principal,
            requirement.Value.Permission,
            new EntityRef(requirement.Value.EntityKind, entityId.Value.ToString()),
            ct);
    }

    private static ObjectResult ForbiddenResult(AuthorizationResult result) => new(new
    {
        code = "FORBIDDEN",
        message = result.Reason ?? "Forbidden.",
        missing = result.MissingPermission,
    })
    { StatusCode = StatusCodes.Status403Forbidden };

    private static string BuildBatchDownloadCompletionMessage(DownloaderBatchExecutionSummary summary)
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
}
