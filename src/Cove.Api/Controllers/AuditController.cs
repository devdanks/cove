using Cove.Core.Auth;
using Cove.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly CoveContext _db;

    public AuditController(CoveContext db) => _db = db;

    [HttpGet]
    [RequiresPermission(Permissions.AuditRead)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        [FromQuery] string? action = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? outcome = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        perPage = Math.Clamp(perPage, 1, 200);

        var query = _db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(action)) query = query.Where(e => e.Action.StartsWith(action));
        if (!string.IsNullOrEmpty(outcome)) query = query.Where(e => e.Outcome == outcome);
        if (!string.IsNullOrEmpty(actor) && int.TryParse(actor, out var aid))
            query = query.Where(e => e.ActorUserId == aid);

        var total = await query.LongCountAsync(ct);
        var pageQuery = query
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage);

        var rows = await (
            from e in pageQuery
            join u in _db.Users.AsNoTracking() on e.ActorUserId equals (int?)u.Id into userGroup
            from u in userGroup.DefaultIfEmpty()
            select new AuditEventDto(
                e.Id,
                e.OccurredAt,
                e.ActorUserId,
                u != null ? u.Username : null,
                e.ActorKind,
                e.Ip,
                e.UserAgent,
                e.Action,
                e.TargetKind,
                e.TargetId,
                e.Outcome,
                e.Detail))
            .ToListAsync(ct);

        return Ok(new { items = rows, totalCount = total, page, perPage });
    }
}
