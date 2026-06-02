using System.Text.Json;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/videos/{videoId:int}/detections")]
[RequiresPermission(Permissions.SegmentsRead)]
public class VideoDetectionsController(CoveContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DetectionDto>>> GetByVideo(int videoId, CancellationToken ct)
    {
        if (!await VideoExistsAsync(videoId, ct)) return NotFound();

        var detections = await db.VisibleDetections()
            .AsNoTracking()
            .Where(detection => detection.HostType == DetectionHostType.Video && detection.HostId == videoId)
            .OrderBy(detection => detection.ObservedAtSec ?? 0d)
            .ThenBy(detection => detection.Id)
            .ToListAsync(ct);

        return Ok(detections.Select(MapToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DetectionDto>> GetById(int videoId, int id, CancellationToken ct)
    {
        var detection = await db.VisibleDetections()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == DetectionHostType.Video && item.HostId == videoId, ct);

        return detection is null ? NotFound() : Ok(MapToDto(detection));
    }

    [HttpPost]
    [RequiresPermission(Permissions.SegmentsWrite)]
    [RequiresEntityAccess(EntityKinds.Video, Permissions.SegmentsWrite, RouteValueName = "videoId")]
    public async Task<ActionResult<DetectionDto>> Create(int videoId, [FromBody] DetectionCreateDto dto, CancellationToken ct)
    {
        if (!await VideoExistsAsync(videoId, ct)) return NotFound();
        if (dto.FrameWidth <= 0 || dto.FrameHeight <= 0)
            return BadRequest("Detection frame dimensions must be greater than zero.");
        if (dto.W <= 0 || dto.H <= 0)
            return BadRequest("Detection bounding boxes must have positive width and height.");

        var detection = new Detection
        {
            HostType = DetectionHostType.Video,
            HostId = videoId,
            ObservedAtSec = dto.ObservedAtSec,
            FrameWidth = dto.FrameWidth,
            FrameHeight = dto.FrameHeight,
            Class = dto.Class,
            Score = dto.Score,
            X = dto.X,
            Y = dto.Y,
            W = dto.W,
            H = dto.H,
            Extra = ToDocument(dto.Extra),
            RefKind = dto.RefKind,
            RefId = dto.RefId,
            GroupKey = dto.GroupKey,
            SourceKey = NormalizeSourceKey(dto.SourceKey),
            SourceRunId = dto.SourceRunId,
        };

        db.Detections.Add(detection);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { videoId, id = detection.Id }, MapToDto(detection));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.SegmentsWrite)]
    [RequiresEntityAccess(EntityKinds.Video, Permissions.SegmentsWrite, RouteValueName = "videoId")]
    public async Task<ActionResult<DetectionDto>> Update(int videoId, int id, [FromBody] DetectionUpdateDto dto, CancellationToken ct)
    {
        if (dto.FrameWidth <= 0 || dto.FrameHeight <= 0)
            return BadRequest("Detection frame dimensions must be greater than zero.");
        if (dto.W <= 0 || dto.H <= 0)
            return BadRequest("Detection bounding boxes must have positive width and height.");

        var detection = await db.Detections
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == DetectionHostType.Video && item.HostId == videoId, ct);
        if (detection is null) return NotFound();

        detection.ObservedAtSec = dto.ObservedAtSec;
        detection.FrameWidth = dto.FrameWidth;
        detection.FrameHeight = dto.FrameHeight;
        detection.Class = dto.Class;
        detection.Score = dto.Score;
        detection.X = dto.X;
        detection.Y = dto.Y;
        detection.W = dto.W;
        detection.H = dto.H;
        detection.Extra = ToDocument(dto.Extra);
        detection.RefKind = dto.RefKind;
        detection.RefId = dto.RefId;
        detection.GroupKey = dto.GroupKey;
        detection.SourceKey = NormalizeSourceKey(dto.SourceKey);
        detection.SourceRunId = dto.SourceRunId;

        await db.SaveChangesAsync(ct);
        return Ok(MapToDto(detection));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.SegmentsDelete)]
    [RequiresEntityAccess(EntityKinds.Video, Permissions.SegmentsDelete, RouteValueName = "videoId")]
    public async Task<IActionResult> Delete(int videoId, int id, CancellationToken ct)
    {
        var detection = await db.Detections
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == DetectionHostType.Video && item.HostId == videoId, ct);
        if (detection is null) return NotFound();

        db.Detections.Remove(detection);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> VideoExistsAsync(int videoId, CancellationToken ct) =>
        db.Videos.AsNoTracking().AnyAsync(video => video.Id == videoId, ct);

    private static DetectionDto MapToDto(Detection detection) => new(
        detection.Id,
        detection.HostType,
        detection.HostId,
        detection.ObservedAtSec,
        detection.FrameWidth,
        detection.FrameHeight,
        detection.Class,
        detection.Score,
        detection.X,
        detection.Y,
        detection.W,
        detection.H,
        detection.Extra?.RootElement.Clone(),
        detection.RefKind,
        detection.RefId,
        detection.GroupKey,
        detection.SourceKey,
        detection.SourceRunId,
        detection.CreatedAt.ToString("o"),
        detection.UpdatedAt.ToString("o"));

    private static JsonDocument? ToDocument(JsonElement? payload) =>
        payload.HasValue ? JsonDocument.Parse(payload.Value.GetRawText()) : null;

    private static string NormalizeSourceKey(string? sourceKey) =>
        string.IsNullOrWhiteSpace(sourceKey) ? "user" : sourceKey;
}
