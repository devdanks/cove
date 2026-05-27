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
public class SceneSegmentsController(CoveContext db, SegmentSpanResolver spanResolver, IBlobService blobService, IFieldProvenanceService? fieldProvenanceService = null) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SegmentDto>>> GetByScene(int sceneId, CancellationToken ct)
    {
        if (!await SceneExistsAsync(sceneId, ct)) return NotFound();

            var segments = await db.VisibleSegments()
            .AsNoTracking()
            .Include(segment => segment.Tag)
            .Where(segment => segment.HostType == SegmentHostType.Scene && segment.HostId == sceneId)
            .OrderBy(segment => segment.StartSec)
            .ThenBy(segment => segment.Id)
            .ToListAsync(ct);

        return Ok(segments.Select(segment => MapToDto(segment)).ToList());
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
        var segment = await db.VisibleSegments()
            .AsNoTracking()
            .Include(item => item.Tag)
            .FirstOrDefaultAsync(item => item.Id == id && item.HostType == SegmentHostType.Scene && item.HostId == sceneId, ct);

        if (segment is null)
            return NotFound();

        return Ok(MapToDto(segment, await LoadSegmentFieldProvenanceAsync(segment.Id, ct)));
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
        await RecordManualSegmentFieldProvenanceAsync(segment, ct);
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

        var originalStartSec = segment.StartSec;
        var originalEndSec = segment.EndSec;
        var originalTagId = segment.TagId;
        var originalKind = segment.Kind;
        var originalRefId = segment.RefId;
        var originalPayload = segment.Payload?.RootElement.GetRawText();
        var originalSourceKey = segment.SourceKey;
        var originalSourceRunId = segment.SourceRunId;
        var originalConfidence = segment.Confidence;
        var originalTitle = segment.Title;
        var originalColorHint = segment.ColorHint;

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

        var manualFields = new Dictionary<string, object?>();
        if (!originalStartSec.Equals(segment.StartSec)) manualFields["start_sec"] = segment.StartSec;
        if (originalEndSec != segment.EndSec) manualFields["end_sec"] = segment.EndSec;
        if (originalTagId != segment.TagId) manualFields["tag_id"] = segment.TagId;
        if (!string.Equals(originalKind, segment.Kind, StringComparison.Ordinal)) manualFields["kind"] = segment.Kind;
        if (originalRefId != segment.RefId) manualFields["ref_id"] = segment.RefId;
        var updatedPayload = segment.Payload?.RootElement.GetRawText();
        if (!string.Equals(originalPayload, updatedPayload, StringComparison.Ordinal)) manualFields["payload"] = dto.Payload;
        if (!string.Equals(originalSourceKey, segment.SourceKey, StringComparison.Ordinal)) manualFields["source_key"] = segment.SourceKey;
        if (!string.Equals(originalSourceRunId, segment.SourceRunId, StringComparison.Ordinal)) manualFields["source_run_id"] = segment.SourceRunId;
        if (originalConfidence != segment.Confidence) manualFields["confidence"] = segment.Confidence;
        if (!string.Equals(originalTitle, segment.Title, StringComparison.Ordinal)) manualFields["title"] = segment.Title;
        if (!string.Equals(originalColorHint, segment.ColorHint, StringComparison.Ordinal)) manualFields["color_hint"] = segment.ColorHint;
        await RecordManualSegmentFieldProvenanceAsync(segment.Id, manualFields, ct);

        await db.SaveChangesAsync(ct);
        spanResolver.EvictScene(sceneId);
        await LoadTagAsync(segment, ct);
        return Ok(MapToDto(segment, await LoadSegmentFieldProvenanceAsync(segment.Id, ct)));
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

    private static SegmentDto MapToDto(Segment segment, IReadOnlyList<FieldProvenanceDto>? fieldProvenance = null) => new(
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
        segment.UpdatedAt.ToString("o"),
        fieldProvenance?.ToList());

    private async Task<IReadOnlyList<FieldProvenanceDto>?> LoadSegmentFieldProvenanceAsync(int segmentId, CancellationToken cancellationToken)
        => fieldProvenanceService == null
            ? null
            : await fieldProvenanceService.GetForHostAsync(AffinityHostType.Segment, segmentId, cancellationToken);

    private async Task RecordManualSegmentFieldProvenanceAsync(Segment segment, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        var fields = new Dictionary<string, object?>
        {
            ["start_sec"] = segment.StartSec,
            ["end_sec"] = segment.EndSec,
            ["tag_id"] = segment.TagId,
            ["kind"] = segment.Kind,
            ["ref_id"] = segment.RefId,
            ["payload"] = segment.Payload?.RootElement.Clone(),
            ["source_key"] = segment.SourceKey,
            ["source_run_id"] = segment.SourceRunId,
            ["confidence"] = segment.Confidence,
            ["title"] = segment.Title,
            ["color_hint"] = segment.ColorHint,
        };
        await RecordManualSegmentFieldProvenanceAsync(segment.Id, fields, cancellationToken);
    }

    private Task RecordManualSegmentFieldProvenanceAsync(int segmentId, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken)
        => fieldProvenanceService == null || fields.Count == 0
            ? Task.CompletedTask
            : fieldProvenanceService.RecordManyAsync(AffinityHostType.Segment, segmentId, fields, "user", cancellationToken: cancellationToken);

    private static JsonDocument? ToDocument(JsonElement? payload) =>
        payload.HasValue ? JsonDocument.Parse(payload.Value.GetRawText()) : null;

    private static string NormalizeSourceKey(string? sourceKey) =>
        string.IsNullOrWhiteSpace(sourceKey) ? "user" : sourceKey;
}