using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/playback")]
[AllowWithoutPermission]
public class PlaybackController(IUserEngagementService engagementService, ICurrentPrincipalAccessor principalAccessor) : ControllerBase
{
    [HttpPost("intervals")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("interactions")]
    public async Task<IActionResult> RecordIntervals([FromBody] PlaybackIntervalsRequestDto dto, CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is null)
            return Forbid();
        if (!InteractionValueMapper.TryParseHostType(dto.HostType, out var hostType))
            return BadRequest("Unsupported host type.");
        if (!HasInteractionPermission(hostType))
            return Forbid();

        var recorded = await engagementService.RecordPlaybackIntervalsAsync(dto, ct);
        return recorded ? NoContent() : NotFound();
    }

    private bool HasInteractionPermission(InteractionHostType hostType)
    {
        var permission = hostType switch
        {
            InteractionHostType.Video => Permissions.VideosRead,
            InteractionHostType.Image => Permissions.ImagesRead,
            InteractionHostType.Audio => Permissions.AudiosRead,
            InteractionHostType.Text => Permissions.TextsRead,
            InteractionHostType.Segment => Permissions.SegmentsRead,
            _ => null,
        };

        return permission is null || principalAccessor.Current?.Has(permission) == true;
    }
}

