using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/scrape-attempts")]
public class ScrapeAttemptsController(ScrapeAttemptService scrapeAttemptService, SceneBatchScrapeService sceneBatchScrapeService, IJobService jobService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScrapeAttemptDto>>> List([FromQuery] string? entityType, [FromQuery] int? entityId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        return Ok(await scrapeAttemptService.ListAttemptsAsync(entityType, entityId, limit, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScrapeAttemptDto>> Get(Guid id, CancellationToken ct)
    {
        var attempt = await scrapeAttemptService.GetAttemptAsync(id, ct);
        return attempt == null ? NotFound() : Ok(attempt);
    }

    [HttpPost]
    public async Task<ActionResult<ScrapeAttemptDto>> Create([FromBody] CreateScrapeAttemptDto dto, CancellationToken ct)
    {
        var attempt = await scrapeAttemptService.CreateAttemptAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = attempt.Id }, attempt);
    }

    [HttpPost("{id:guid}/apply")]
    public async Task<ActionResult<ScrapeAttemptDto>> ApplyScene(Guid id, [FromBody] ApplySceneScrapeAttemptDto dto, CancellationToken ct)
    {
        var attempt = await scrapeAttemptService.ApplySceneAttemptAsync(id, dto, ct);
        return attempt == null ? NotFound() : Ok(attempt);
    }

    [HttpPost("batch-scenes")]
    public ActionResult<object> StartSceneBatch([FromBody] BatchSceneScrapeStartRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ScraperId))
            return BadRequest(new { error = "ScraperId is required." });

        if (dto.SceneIds.Count == 0)
            return BadRequest(new { error = "Select at least one scene to batch scrape." });

        var normalizedInputKind = dto.InputKind?.Trim().ToLowerInvariant();
        if (normalizedInputKind is not ("url" or "name"))
            return BadRequest(new { error = $"Unsupported batch scene scrape input kind: {dto.InputKind}" });

        var jobId = jobService.Enqueue(
            "scene-batch-scrape",
            $"Scraping {dto.SceneIds.Count} scene{(dto.SceneIds.Count == 1 ? string.Empty : "s")}",
            async (progress, ct) =>
            {
                var summary = await sceneBatchScrapeService.RunAsync(dto, progress, ct);
                progress.Report(1d, BuildBatchSceneScrapeCompletionMessage(summary));
            },
            exclusive: false);

        return Accepted(new { jobId, queuedCount = dto.SceneIds.Count });
    }

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