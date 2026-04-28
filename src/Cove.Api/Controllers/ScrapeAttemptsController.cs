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
[RequiresPermission(Permissions.ScenesScrape)]
public class ScrapeAttemptsController(ScrapeAttemptService scrapeAttemptService, SceneBatchScrapeService sceneBatchScrapeService, IJobService jobService, ICurrentPrincipalAccessor principalAccessor, IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead, ActionArgumentName = "entityId")]
    public async Task<ActionResult<IReadOnlyList<ScrapeAttemptDto>>> List([FromQuery] string? entityType, [FromQuery] int? entityId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        return Ok(await scrapeAttemptService.ListAttemptsAsync(entityType, entityId, limit, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScrapeAttemptDto>> Get(Guid id, CancellationToken ct)
    {
        var attempt = await scrapeAttemptService.GetAttemptAsync(id, ct);
        if (attempt == null)
            return NotFound();

        var forbidden = await AuthorizeSceneAttemptAsync(attempt, Permissions.ScenesRead, ct);
        return forbidden ?? Ok(attempt);
    }

    [HttpPost]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead, ActionArgumentName = "dto", PropertyName = "EntityId")]
    public async Task<ActionResult<ScrapeAttemptDto>> Create([FromBody] CreateScrapeAttemptDto dto, CancellationToken ct)
    {
        var attempt = await scrapeAttemptService.CreateAttemptAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = attempt.Id }, attempt);
    }

    [HttpPost("{id:guid}/apply")]
    [RequiresPermission(Permissions.ScenesWrite)]
    public async Task<ActionResult<ScrapeAttemptDto>> ApplyScene(Guid id, [FromBody] ApplySceneScrapeAttemptDto dto, CancellationToken ct)
    {
        var existingAttempt = await scrapeAttemptService.GetAttemptAsync(id, ct);
        if (existingAttempt == null)
            return NotFound();

        var forbidden = await AuthorizeSceneAttemptAsync(existingAttempt, Permissions.ScenesWrite, ct);
        if (forbidden != null)
            return forbidden;

        var attempt = await scrapeAttemptService.ApplySceneAttemptAsync(id, dto, ct);
        return attempt == null ? NotFound() : Ok(attempt);
    }

    [HttpPost("batch-scenes")]
    [RequiresPermission(Permissions.JobsRun, Permissions.ScenesScrape)]
    public async Task<ActionResult<object>> StartSceneBatch([FromBody] BatchSceneScrapeStartRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ScraperId))
            return BadRequest(new { error = "ScraperId is required." });

        if (dto.SceneIds.Count == 0)
            return BadRequest(new { error = "Select at least one scene to batch scrape." });

        var normalizedInputKind = dto.InputKind?.Trim().ToLowerInvariant();
        if (normalizedInputKind is not ("url" or "name"))
            return BadRequest(new { error = $"Unsupported batch scene scrape input kind: {dto.InputKind}" });

        var scenePermission = dto.AutoApply ? Permissions.ScenesWrite : Permissions.ScenesRead;
        foreach (var sceneId in dto.SceneIds.Distinct())
        {
            var result = await authorizationService.AuthorizeAsync(
                principalAccessor.Current,
                scenePermission,
                new EntityRef(EntityKinds.Scene, sceneId.ToString(CultureInfo.InvariantCulture)),
                ct);

            if (!result.Allowed)
                return ForbiddenResult(result);
        }

        var jobId = jobService.Enqueue(
            "scene-batch-scrape",
            $"Scraping {dto.SceneIds.Count} scene{(dto.SceneIds.Count == 1 ? string.Empty : "s")}",
            async (progress, jobCt) =>
            {
                var summary = await sceneBatchScrapeService.RunAsync(dto, progress, jobCt);
                progress.Report(1d, BuildBatchSceneScrapeCompletionMessage(summary));
            },
            exclusive: false);

        return Accepted(new { jobId, queuedCount = dto.SceneIds.Count });
    }

    private async Task<ActionResult<ScrapeAttemptDto>?> AuthorizeSceneAttemptAsync(ScrapeAttemptDto attempt, string permission, CancellationToken ct)
    {
        if (!string.Equals(attempt.EntityType, EntityKinds.Scene, StringComparison.OrdinalIgnoreCase) || !attempt.EntityId.HasValue)
            return null;

        var result = await authorizationService.AuthorizeAsync(
            principalAccessor.Current,
            permission,
            new EntityRef(EntityKinds.Scene, attempt.EntityId.Value.ToString(CultureInfo.InvariantCulture)),
            ct);

        if (result.Allowed)
            return null;

        return ForbiddenResult(result);
    }

    private static ObjectResult ForbiddenResult(AuthorizationResult result) => new(new
    {
        code = "FORBIDDEN",
        message = result.Reason ?? "Forbidden.",
        missing = result.MissingPermission,
    })
    { StatusCode = StatusCodes.Status403Forbidden };

    private static string BuildBatchSceneScrapeCompletionMessage(SceneBatchScrapeExecutionSummary summary)
    {
        var parts = new List<string>
        {
            $"Scraped {summary.ScrapedCount} of {summary.TotalCount} scene{(summary.TotalCount == 1 ? string.Empty : "s")}."
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