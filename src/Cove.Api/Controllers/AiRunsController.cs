using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/ai-runs")]
[RequiresPermission(Permissions.AiRunsRead)]
public class AiRunsController(CoveContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AiRunDto>>> List(
        [FromQuery] AiRunTargetType? targetType,
        [FromQuery] int? targetId,
        [FromQuery] string? sourceKey,
        [FromQuery] string? runKey,
        [FromQuery] AiRunStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        perPage = Math.Clamp(perPage, 1, 250);

        var query = db.AiRuns.AsNoTracking().AsQueryable();

        if (targetType.HasValue)
            query = query.Where(run => run.TargetType == targetType.Value);

        if (targetId.HasValue)
            query = query.Where(run => run.TargetId == targetId.Value);

        if (!string.IsNullOrWhiteSpace(sourceKey))
            query = query.Where(run => run.SourceKey == sourceKey);

        if (!string.IsNullOrWhiteSpace(runKey))
            query = query.Where(run => run.RunKey == runKey);

        if (status.HasValue)
            query = query.Where(run => run.Status == status.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<AiRunDto>(items.Select(MapToDto).ToList(), totalCount, page, perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AiRunDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var run = await db.AiRuns.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return run is null ? NotFound() : Ok(MapToDto(run));
    }

    private static AiRunDto MapToDto(AiRun run) => new(
        run.Id,
        run.RunKey,
        run.SourceKey,
        run.TargetType,
        run.TargetId,
        run.Trigger,
        run.JobId,
        run.Status,
        run.StartedAt,
        run.CompletedAt,
        run.LoadPolicy,
        run.FrameIntervalSec,
        run.Vr,
        run.Request?.RootElement.Clone(),
        run.Models?.RootElement.Clone(),
        run.Summary?.RootElement.Clone(),
        run.Error,
        run.CreatedAt,
        run.UpdatedAt);
}