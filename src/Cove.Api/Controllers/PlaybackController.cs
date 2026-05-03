using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/playback")]
public class PlaybackController(IUserEngagementService engagementService, ICurrentPrincipalAccessor principalAccessor) : ControllerBase
{
    [HttpPost("intervals")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("interactions")]
    public async Task<IActionResult> RecordIntervals([FromBody] PlaybackIntervalsRequestDto dto, CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is null)
            return Forbid();
        if (!InteractionValueMapper.TryParseHostType(dto.HostType, out _))
            return BadRequest("Unsupported host type.");

        var recorded = await engagementService.RecordPlaybackIntervalsAsync(dto, ct);
        return recorded ? NoContent() : NotFound();
    }
}
