using System.Globalization;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using IAuthorizationService = Cove.Core.Auth.IAuthorizationService;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/scrape-attempts")]
[RequiresPermission(Permissions.VideosScrape, Permissions.VideosWrite, Permissions.AudiosWrite, Permissions.TextsWrite, Permissions.ImagesWrite, Permissions.GalleriesWrite, Permissions.GroupsWrite, Mode = PermissionMode.Any)]
public class ScrapeAttemptsController(ScrapeAttemptService scrapeAttemptService, VideoBatchScrapeService videoBatchScrapeService, ImageBatchScrapeService imageBatchScrapeService, IJobService jobService, ICurrentPrincipalAccessor principalAccessor, IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScrapeAttemptDto>>> List([FromQuery] string? entityType, [FromQuery] int? entityId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityType) || !entityId.HasValue)
            return BadRequest(new { error = "entityType and entityId are required." });

        var authorizationError = await AuthorizeEntityAsync(entityType, entityId.Value, write: false, ct);
        if (authorizationError != null)
            return authorizationError;

        return Ok(await scrapeAttemptService.ListAttemptsAsync(entityType, entityId, limit, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScrapeAttemptDto>> Get(Guid id, CancellationToken ct)
    {
        var attempt = await scrapeAttemptService.GetAttemptAsync(id, ct);
        if (attempt == null)
            return NotFound();

        var authorizationError = await AuthorizeAttemptAsync(attempt, write: false, ct);
        if (authorizationError != null)
            return authorizationError;

        return Ok(attempt);
    }

    [HttpPost]
    public async Task<ActionResult<ScrapeAttemptDto>> Create([FromBody] CreateScrapeAttemptDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EntityType) || dto.EntityId == null)
            return BadRequest(new { error = "EntityType and EntityId are required." });

        var authorizationError = await AuthorizeEntityAsync(dto.EntityType, dto.EntityId.Value, write: false, ct);
        if (authorizationError != null)
            return authorizationError;

        var attempt = await scrapeAttemptService.CreateAttemptAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = attempt.Id }, attempt);
    }

    [HttpPost("{id:guid}/apply")]
    public async Task<ActionResult<ScrapeAttemptDto>> Apply(Guid id, [FromBody] ApplyVideoScrapeAttemptDto dto, CancellationToken ct)
    {
        var existingAttempt = await scrapeAttemptService.GetAttemptAsync(id, ct);
        if (existingAttempt == null)
            return NotFound();

        var authorizationError = await AuthorizeAttemptAsync(existingAttempt, write: true, ct);
        if (authorizationError != null)
            return authorizationError;

        var attempt = await scrapeAttemptService.ApplyAttemptAsync(id, dto, ct);
        return attempt == null ? NotFound() : Ok(attempt);
    }

    [HttpPost("batch-videos")]
    [RequiresPermission(Permissions.JobsRun, Permissions.VideosScrape)]
    public async Task<ActionResult<object>> StartVideoBatch([FromBody] BatchVideoScrapeStartRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ScraperId))
            return BadRequest(new { error = "ScraperId is required." });

        if (dto.VideoIds.Count == 0)
            return BadRequest(new { error = "Select at least one video to batch scrape." });

        var normalizedInputKind = dto.InputKind?.Trim().ToLowerInvariant();
        if (normalizedInputKind is not ("url" or "name"))
            return BadRequest(new { error = $"Unsupported batch video scrape input kind: {dto.InputKind}" });

        var videoPermission = dto.AutoApply ? Permissions.VideosWrite : Permissions.VideosRead;
        foreach (var videoId in dto.VideoIds.Distinct())
        {
            var result = await authorizationService.AuthorizeAsync(
                principalAccessor.Current,
                videoPermission,
                new EntityRef(EntityKinds.Video, videoId.ToString(CultureInfo.InvariantCulture)),
                ct);

            if (!result.Allowed)
                return ForbiddenResult(result);
        }

        var jobId = jobService.Enqueue(
            "video-batch-scrape",
            $"Scraping {dto.VideoIds.Count} video{(dto.VideoIds.Count == 1 ? string.Empty : "s")}",
            async (progress, jobCt) =>
            {
                var summary = await videoBatchScrapeService.RunAsync(dto, progress, jobCt);
                progress.Report(1d, BuildBatchVideoScrapeCompletionMessage(summary));
            },
            exclusive: false);

        return Accepted(new { jobId, queuedCount = dto.VideoIds.Count });
    }

    [HttpPost("batch-images")]
    [RequiresPermission(Permissions.JobsRun, Permissions.ImagesWrite)]
    public async Task<ActionResult<object>> StartImageBatch([FromBody] BatchImageScrapeStartRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ScraperId))
            return BadRequest(new { error = "ScraperId is required." });

        if (dto.ImageIds.Count == 0)
            return BadRequest(new { error = "Select at least one image to batch scrape." });

        var normalizedInputKind = dto.InputKind?.Trim().ToLowerInvariant();
        if (normalizedInputKind is not ("url" or "name"))
            return BadRequest(new { error = $"Unsupported batch image scrape input kind: {dto.InputKind}" });

        foreach (var imageId in dto.ImageIds.Distinct())
        {
            var result = await authorizationService.AuthorizeAsync(
                principalAccessor.Current,
                Permissions.ImagesWrite,
                new EntityRef(EntityKinds.Image, imageId.ToString(CultureInfo.InvariantCulture)),
                ct);

            if (!result.Allowed)
                return ForbiddenResult(result);
        }

        var jobId = jobService.Enqueue(
            "image-batch-scrape",
            $"Scraping {dto.ImageIds.Count} image{(dto.ImageIds.Count == 1 ? string.Empty : "s")}",
            async (progress, jobCt) =>
            {
                var summary = await imageBatchScrapeService.RunAsync(dto, progress, jobCt);
                progress.Report(1d, BuildBatchImageScrapeCompletionMessage(summary));
            },
            exclusive: false);

        return Accepted(new { jobId, queuedCount = dto.ImageIds.Count });
    }

    private async Task<ObjectResult?> AuthorizeAttemptAsync(ScrapeAttemptDto attempt, bool write, CancellationToken ct)
    {
        if (!attempt.EntityId.HasValue)
            return BadRequest(new { error = "Scrape attempt is not attached to an entity." });

        return await AuthorizeEntityAsync(attempt.EntityType, attempt.EntityId.Value, write, ct);
    }

    private async Task<ObjectResult?> AuthorizeEntityAsync(string entityType, int entityId, bool write, CancellationToken ct)
    {
        if (!TryGetAttemptPermissions(entityType, write, out var entityKind, out var permissions))
            return BadRequest(new { error = $"Scrape attempts are not supported for entity type '{entityType}'." });

        AuthorizationResult? denied = null;
        foreach (var permission in permissions)
        {
            var result = await authorizationService.AuthorizeAsync(
                principalAccessor.Current,
                permission,
                new EntityRef(entityKind, entityId.ToString(CultureInfo.InvariantCulture)),
                ct);

            if (result.Allowed)
                return null;

            denied = result;
        }

        return denied == null
            ? BadRequest(new { error = $"Scrape attempts are not supported for entity type '{entityType}'." })
            : ForbiddenResult(denied.Value);
    }

    private static bool TryGetAttemptPermissions(string entityType, bool write, out string entityKind, out IReadOnlyList<string> permissions)
    {
        entityKind = entityType.Trim().ToLowerInvariant();
        switch (entityKind)
        {
            case EntityKinds.Video:
                permissions = write
                    ? [Permissions.VideosWrite]
                    : [Permissions.VideosScrape, Permissions.VideosWrite];
                return true;
            case EntityKinds.Audio:
                permissions = [Permissions.AudiosWrite];
                return true;
            case EntityKinds.Text:
                permissions = [Permissions.TextsWrite];
                return true;
            case EntityKinds.Image:
                permissions = [Permissions.ImagesWrite];
                return true;
            case EntityKinds.Gallery:
                permissions = [Permissions.GalleriesWrite];
                return true;
            case EntityKinds.Group:
                permissions = [Permissions.GroupsWrite];
                return true;
            default:
                permissions = [];
                return false;
        }
    }

    private static ObjectResult ForbiddenResult(AuthorizationResult result) => new(new
    {
        code = "FORBIDDEN",
        message = result.Reason ?? "Forbidden.",
        missing = result.MissingPermission,
    })
    { StatusCode = StatusCodes.Status403Forbidden };

    private static string BuildBatchVideoScrapeCompletionMessage(VideoBatchScrapeExecutionSummary summary)
    {
        var parts = new List<string>
        {
            $"Scraped {summary.ScrapedCount} of {summary.TotalCount} video{(summary.TotalCount == 1 ? string.Empty : "s")}."
        };

        if (summary.AppliedCount > 0)
            parts.Add($"Applied {summary.AppliedCount}.");

        if (summary.PartialAppliedCount > 0)
            parts.Add($"Applied partially {summary.PartialAppliedCount}.");

        if (summary.SkippedCount > 0)
            parts.Add($"Skipped {summary.SkippedCount}.");

        if (summary.FailedCount > 0)
            parts.Add($"Failed {summary.FailedCount}.");

        return string.Join(' ', parts);
    }

    private static string BuildBatchImageScrapeCompletionMessage(ImageBatchScrapeExecutionSummary summary)
    {
        var parts = new List<string>
        {
            $"Scraped {summary.ScrapedCount} of {summary.TotalCount} image{(summary.TotalCount == 1 ? string.Empty : "s")}."
        };

        if (summary.AppliedCount > 0)
            parts.Add($"Applied {summary.AppliedCount}.");

        if (summary.PartialAppliedCount > 0)
            parts.Add($"Applied partially {summary.PartialAppliedCount}.");

        if (summary.SkippedCount > 0)
            parts.Add($"Skipped {summary.SkippedCount}.");

        if (summary.FailedCount > 0)
            parts.Add($"Failed {summary.FailedCount}.");

        return string.Join(' ', parts);
    }
}
