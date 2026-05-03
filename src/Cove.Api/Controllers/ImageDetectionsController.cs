using System.Text.Json;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/images/{imageId:int}/detections")]
[RequiresPermission(Permissions.MarkersRead)]
public class ImageDetectionsController(CoveContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DetectionDto>>> GetByImage(int imageId, CancellationToken ct)
    {
        if (!await ImageExistsAsync(imageId, ct)) return NotFound();

        var detections = await db.Detections
            .AsNoTracking()
            .Where(detection => detection.HostType == DetectionHostType.Image && detection.HostId == imageId)
            .OrderBy(detection => detection.ObservedAtSec ?? 0d)
            .ThenBy(detection => detection.Id)
            .ToListAsync(ct);

        return Ok(detections.Select(MapToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DetectionDto>> GetById(int imageId, int id, CancellationToken ct)
    {
        var detection = await db.Detections
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == DetectionHostType.Image && item.HostId == imageId, ct);

        return detection is null ? NotFound() : Ok(MapToDto(detection));
    }

    [HttpPost]
    [RequiresPermission(Permissions.MarkersWrite)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.MarkersWrite, RouteValueName = "imageId")]
    public async Task<ActionResult<DetectionDto>> Create(int imageId, [FromBody] DetectionCreateDto dto, CancellationToken ct)
    {
        if (!await ImageExistsAsync(imageId, ct)) return NotFound();
        if (dto.FrameWidth <= 0 || dto.FrameHeight <= 0)
            return BadRequest("Detection frame dimensions must be greater than zero.");
        if (dto.W <= 0 || dto.H <= 0)
            return BadRequest("Detection bounding boxes must have positive width and height.");

        var detection = new Detection
        {
            HostType = DetectionHostType.Image,
            HostId = imageId,
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

        return CreatedAtAction(nameof(GetById), new { imageId, id = detection.Id }, MapToDto(detection));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.MarkersWrite)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.MarkersWrite, RouteValueName = "imageId")]
    public async Task<ActionResult<DetectionDto>> Update(int imageId, int id, [FromBody] DetectionUpdateDto dto, CancellationToken ct)
    {
        if (dto.FrameWidth <= 0 || dto.FrameHeight <= 0)
            return BadRequest("Detection frame dimensions must be greater than zero.");
        if (dto.W <= 0 || dto.H <= 0)
            return BadRequest("Detection bounding boxes must have positive width and height.");

        var detection = await db.Detections
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == DetectionHostType.Image && item.HostId == imageId, ct);
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
    [RequiresPermission(Permissions.MarkersDelete)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.MarkersDelete, RouteValueName = "imageId")]
    public async Task<IActionResult> Delete(int imageId, int id, CancellationToken ct)
    {
        var detection = await db.Detections
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == DetectionHostType.Image && item.HostId == imageId, ct);
        if (detection is null) return NotFound();

        db.Detections.Remove(detection);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> ImageExistsAsync(int imageId, CancellationToken ct) =>
        db.Images.AsNoTracking().AnyAsync(image => image.Id == imageId, ct);

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