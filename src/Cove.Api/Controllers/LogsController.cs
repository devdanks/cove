using Microsoft.AspNetCore.Mvc;
using Cove.Api.Services;
using Cove.Core.Auth;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.AuditRead)]
public class LogsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<LogEntry>> GetRecentLogs([FromQuery] string? level = null, [FromQuery] int limit = 200)
    {
        var logs = SignalRLogSink.GetRecentLogs();

        if (!string.IsNullOrEmpty(level))
            logs = logs.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase)).ToList();

        return Ok(logs.TakeLast(limit).ToList());
    }
}
