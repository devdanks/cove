using System.Text.Json;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/scenes/{sceneId:int}/segments")]
[RequiresPermission(Permissions.SegmentsRead)]
public class SceneSegmentsController(CoveContext db, SegmentSpanResolver spanResolver, IBlobService blobService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SegmentDto>>> GetByScene(int sceneId, CancellationToken ct)
    {
        if (!await SceneExistsAsync(sceneId, ct)) return NotFound();

        var segments = await db.Segments
            .AsNoTracking()
            .Include(segment => segment.Tag)
            .Where(segment => segment.HostType == SegmentHostType.Scene && segment.HostId == sceneId)
            .OrderBy(segment => segment.StartSec)
            .ThenBy(segment => segment.Id)
            .ToListAsync(ct);

        return Ok(segments.Select(MapToDto).ToList());
    }

    [HttpGet("spans")]
    public async Task<ActionResult<SceneResolvedSpansDto>> GetSpans(int sceneId, [FromQuery] int? profile = null, CancellationToken ct = default)
    {
        if (!await SceneExistsAsync(sceneId, ct))
            return NotFound();

        try
        {
            return Ok(await spanResolver.ResolveSceneAsync(sceneId, profile, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("spans/query")]
    public async Task<ActionResult<ResolvedSpanListDto>> QuerySpans(int sceneId, [FromBody] SegmentSpanQueryRequestDto request, CancellationToken ct)
    {
        if (!await SceneExistsAsync(sceneId, ct))
            return NotFound();

        try
        {
            var spans = await spanResolver.QuerySceneAsync(sceneId, request, ct);
            return Ok(new ResolvedSpanListDto(spans));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("/api/scenes/{sceneId:int}/spans/{spanKey}")]
    public async Task<ActionResult<ResolvedSpanDetailDto>> GetSpanDetail(int sceneId, string spanKey, [FromQuery] int? profile = null, CancellationToken ct = default)
    {
        if (!await SceneExistsAsync(sceneId, ct))
            return NotFound();

        try
        {
            var detail = await spanResolver.GetSpanDetailAsync(sceneId, spanKey, profile, ct);
            return detail is null ? NotFound() : Ok(detail);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SegmentDto>> GetById(int sceneId, int id, CancellationToken ct)
    {
        var segment = await db.Segments
            .AsNoTracking()
            .Include(item => item.Tag)
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == SegmentHostType.Scene && item.HostId == sceneId, ct);

        return segment is null ? NotFound() : Ok(MapToDto(segment));
    }

    [HttpPost]
    [RequiresPermission(Permissions.SegmentsWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.SegmentsWrite, RouteValueName = "sceneId")]
    public async Task<ActionResult<SegmentDto>> Create(int sceneId, [FromBody] SegmentCreateDto dto, CancellationToken ct)
    {
        if (!await SceneExistsAsync(sceneId, ct)) return NotFound();
        if (dto.EndSec.HasValue && dto.EndSec.Value < dto.StartSec)
            return BadRequest("Segment end must be greater than or equal to the start.");

        var segment = new Segment
        {
            HostType = SegmentHostType.Scene,
            HostId = sceneId,
            StartSec = dto.StartSec,
            EndSec = dto.EndSec,
            TagId = dto.TagId,
            Kind = dto.Kind,
            RefId = dto.RefId,
            Payload = ToDocument(dto.Payload),
            SourceKey = NormalizeSourceKey(dto.SourceKey),
            SourceRunId = dto.SourceRunId,
            Confidence = dto.Confidence,
            Title = dto.Title,
            ColorHint = dto.ColorHint,
        };

        db.Segments.Add(segment);
        await db.SaveChangesAsync(ct);
        spanResolver.EvictScene(sceneId);
        await LoadTagAsync(segment, ct);

        return CreatedAtAction(nameof(GetById), new { sceneId, id = segment.Id }, MapToDto(segment));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.SegmentsWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.SegmentsWrite, RouteValueName = "sceneId")]
    public async Task<ActionResult<SegmentDto>> Update(int sceneId, int id, [FromBody] SegmentUpdateDto dto, CancellationToken ct)
    {
        if (dto.EndSec.HasValue && dto.EndSec.Value < dto.StartSec)
            return BadRequest("Segment end must be greater than or equal to the start.");

        var segment = await db.Segments
            .Include(item => item.Tag)
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == SegmentHostType.Scene && item.HostId == sceneId, ct);
        if (segment is null) return NotFound();

        segment.StartSec = dto.StartSec;
        segment.EndSec = dto.EndSec;
        segment.TagId = dto.TagId;
        segment.Kind = dto.Kind;
        segment.RefId = dto.RefId;
        segment.Payload = ToDocument(dto.Payload);
        segment.SourceKey = NormalizeSourceKey(dto.SourceKey);
        segment.SourceRunId = dto.SourceRunId;
        segment.Confidence = dto.Confidence;
        segment.Title = dto.Title;
        segment.ColorHint = dto.ColorHint;
        segment.Tag = null;

        await db.SaveChangesAsync(ct);
        spanResolver.EvictScene(sceneId);
        await LoadTagAsync(segment, ct);
        return Ok(MapToDto(segment));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.SegmentsDelete)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.SegmentsDelete, RouteValueName = "sceneId")]
    public async Task<IActionResult> Delete(int sceneId, int id, CancellationToken ct)
    {
        // SceneSegmentsController.Delete only deletes persisted raw Segment rows.
        // Derived spans are computed by SegmentSpanResolver and never reach this endpoint.
        var segment = await db.Segments
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == SegmentHostType.Scene && item.HostId == sceneId, ct);
        if (segment is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(segment.ImageBlobId))
            await blobService.DeleteBlobAsync(segment.ImageBlobId, ct);

        db.Segments.Remove(segment);
        await db.SaveChangesAsync(ct);
        spanResolver.EvictScene(sceneId);
        return NoContent();
    }

    private Task<bool> SceneExistsAsync(int sceneId, CancellationToken ct) =>
        db.Scenes.AsNoTracking().AnyAsync(scene => scene.Id == sceneId, ct);

    private async Task LoadTagAsync(Segment segment, CancellationToken ct)
    {
        if (segment.TagId.HasValue)
            await db.Entry(segment).Reference(item => item.Tag).LoadAsync(ct);
    }

    private static SegmentDto MapToDto(Segment segment) => new(
        segment.Id,
        segment.HostType,
        segment.HostId,
        segment.StartSec,
        segment.EndSec,
        segment.TagId,
        segment.Tag?.Name,
        segment.Kind,
        segment.RefId,
        segment.Payload?.RootElement.Clone(),
        segment.SourceKey,
        segment.SourceRunId,
        segment.Confidence,
        segment.Title,
        segment.ColorHint,
        segment.CreatedAt.ToString("o"),
        segment.UpdatedAt.ToString("o"));

    private static JsonDocument? ToDocument(JsonElement? payload) =>
        payload.HasValue ? JsonDocument.Parse(payload.Value.GetRawText()) : null;

    private static string NormalizeSourceKey(string? sourceKey) =>
        string.IsNullOrWhiteSpace(sourceKey) ? "user" : sourceKey;
}