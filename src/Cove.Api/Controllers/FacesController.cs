using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.FacesRead)]
public class FacesController(
    CoveContext db,
    IEmbeddingService embeddingService,
    IBlobService blobService,
    IEnumerable<IFaceLifecycleParticipant> faceLifecycleParticipants,
    ILogger<FacesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<FaceDto>>> List(
        [FromQuery] string? q,
        [FromQuery] int? performerId,
        [FromQuery] bool? ignored,
        [FromQuery] bool? merged,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        perPage = Math.Clamp(perPage, 1, 250);

        var query = db.Faces
            .AsNoTracking()
            .Include(face => face.Performer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(face =>
                (face.Label != null && face.Label.Contains(q)) ||
                (face.Performer != null && face.Performer.Name.Contains(q)));
        }

        if (performerId.HasValue)
            query = query.Where(face => face.PerformerId == performerId.Value);

        if (ignored.HasValue)
            query = query.Where(face => face.Ignored == ignored.Value);

        if (merged.HasValue)
            query = merged.Value
                ? query.Where(face => face.MergedIntoFaceId != null)
                : query.Where(face => face.MergedIntoFaceId == null);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(face => face.MergedIntoFaceId != null)
            .ThenByDescending(face => face.DetectionCount)
            .ThenBy(face => face.Label)
            .ThenBy(face => face.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<FaceDto>(items.Select(MapToDto).ToList(), totalCount, page, perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FaceDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var face = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        return face is null ? NotFound() : Ok(MapToDto(face));
    }

    [HttpGet("{id:int}/detections")]
    public async Task<ActionResult<IReadOnlyList<DetectionDto>>> GetDetections(int id, CancellationToken cancellationToken)
    {
        var faceExists = await db.Faces.AsNoTracking().AnyAsync(face => face.Id == id, cancellationToken);
        if (!faceExists)
            return NotFound();

        var detections = await db.Detections
            .AsNoTracking()
            .Where(detection => detection.RefId == id && detection.RefKind != null && detection.RefKind.ToLower() == "face")
            .OrderByDescending(detection => detection.UpdatedAt)
            .ThenBy(detection => detection.Id)
            .ToListAsync(cancellationToken);

        return Ok(detections.Select(MapDetectionToDto).ToList());
    }

    [HttpGet("{id:int}/delete-impact")]
    [RequiresPermission(Permissions.FacesDelete)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesDelete)]
    public async Task<ActionResult<FaceDeleteImpactDto>> GetDeleteImpact(int id, CancellationToken cancellationToken)
    {
        var face = await db.Faces
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        return face is null
            ? NotFound()
            : Ok(await BuildDeleteImpactAsync(id, face.CoverBlobId is not null, cancellationToken));
    }

    [HttpPost]
    [RequiresPermission(Permissions.FacesWrite)]
    public async Task<ActionResult<FaceDto>> Create([FromBody] FaceCreateDto dto, CancellationToken cancellationToken)
    {
        if (dto.PerformerId.HasValue)
        {
            var performerExists = await db.Performers.AnyAsync(performer => performer.Id == dto.PerformerId.Value, cancellationToken);
            if (!performerExists)
                return ValidationProblem($"Performer {dto.PerformerId.Value} was not found.");
        }

        var face = new Face
        {
            Label = Clean(dto.Label),
            PerformerId = dto.PerformerId,
            Ignored = dto.Ignored,
            PrimarySourceKey = Clean(dto.PrimarySourceKey),
        };

        db.Faces.Add(face);
        await db.SaveChangesAsync(cancellationToken);

        var created = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstAsync(item => item.Id == face.Id, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = face.Id }, MapToDto(created));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<ActionResult<FaceDto>> Update(int id, [FromBody] FaceUpdateDto dto, CancellationToken cancellationToken)
    {
        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        if (dto.PerformerId.HasValue)
        {
            var performerExists = await db.Performers.AnyAsync(performer => performer.Id == dto.PerformerId.Value, cancellationToken);
            if (!performerExists)
                return ValidationProblem($"Performer {dto.PerformerId.Value} was not found.");
        }

        face.Label = Clean(dto.Label);
        face.PerformerId = dto.PerformerId;
        face.Ignored = dto.Ignored;
        face.PrimarySourceKey = Clean(dto.PrimarySourceKey);

        await db.SaveChangesAsync(cancellationToken);

        var updated = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstAsync(item => item.Id == id, cancellationToken);

        return Ok(MapToDto(updated));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.FacesDelete)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        var mergedFaces = await db.Faces
            .Where(item => item.MergedIntoFaceId == id)
            .ToListAsync(cancellationToken);
        var detections = await db.Detections
            .Where(detection => detection.RefId == id && detection.RefKind != null && detection.RefKind.ToLower() == "face")
            .ToListAsync(cancellationToken);
        var embeddings = await db.Embeddings
            .Where(embedding => embedding.HostType == EmbeddingHostType.Face && embedding.HostId == id)
            .ToListAsync(cancellationToken);
        var segments = await db.Segments
            .Where(segment => segment.RefId == id && segment.Kind != null && segment.Kind.ToLower() == "face")
            .ToListAsync(cancellationToken);
        var coverBlobId = face.CoverBlobId;

        foreach (var participant in faceLifecycleParticipants)
        {
            await participant.OnDeletingAsync(face, cancellationToken);
        }

        foreach (var mergedFace in mergedFaces)
        {
            mergedFace.MergedIntoFaceId = null;
        }

        if (detections.Count > 0)
            db.Detections.RemoveRange(detections);

        if (embeddings.Count > 0)
            db.Embeddings.RemoveRange(embeddings);

        if (segments.Count > 0)
            db.Segments.RemoveRange(segments);

        db.Faces.Remove(face);
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(coverBlobId))
        {
            try
            {
                await blobService.DeleteBlobAsync(coverBlobId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete face cover blob {BlobId} after deleting face {FaceId}.", coverBlobId, id);
            }
        }

        return NoContent();
    }

    [HttpPost("{id:int}/link")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<ActionResult<FaceDto>> Link(int id, [FromBody] FaceLinkDto dto, CancellationToken cancellationToken)
    {
        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        if (dto.PerformerId.HasValue)
        {
            var performerExists = await db.Performers.AnyAsync(performer => performer.Id == dto.PerformerId.Value, cancellationToken);
            if (!performerExists)
                return ValidationProblem($"Performer {dto.PerformerId.Value} was not found.");
        }

        face.PerformerId = dto.PerformerId;
        await db.SaveChangesAsync(cancellationToken);

        var linked = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstAsync(item => item.Id == id, cancellationToken);

        return Ok(MapToDto(linked));
    }

    [HttpPost("{id:int}/merge-into")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<ActionResult<FaceDto>> MergeInto(int id, [FromBody] FaceMergeDto dto, CancellationToken cancellationToken)
    {
        if (id == dto.TargetFaceId)
            return ValidationProblem("A face cannot be merged into itself.");

        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        var target = await db.Faces.FirstOrDefaultAsync(item => item.Id == dto.TargetFaceId, cancellationToken);
        if (target is null)
            return ValidationProblem($"Target face {dto.TargetFaceId} was not found.");

        if (target.MergedIntoFaceId.HasValue)
            return ValidationProblem("Cannot merge into a face that has already been merged.");

        face.MergedIntoFaceId = target.Id;
        if (face.PerformerId.HasValue && target.PerformerId == null)
            target.PerformerId = face.PerformerId;
        if (string.IsNullOrWhiteSpace(target.Label) && !string.IsNullOrWhiteSpace(face.Label))
            target.Label = face.Label;

        await db.SaveChangesAsync(cancellationToken);

        var merged = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstAsync(item => item.Id == id, cancellationToken);

        return Ok(MapToDto(merged));
    }

    [HttpPost("{id:int}/ignore")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<ActionResult<FaceDto>> SetIgnored(int id, [FromBody] FaceIgnoreDto dto, CancellationToken cancellationToken)
    {
        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        face.Ignored = dto.Ignored;
        await db.SaveChangesAsync(cancellationToken);

        var updated = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstAsync(item => item.Id == id, cancellationToken);

        return Ok(MapToDto(updated));
    }

    [HttpGet("{id:int}/similar")]
    public async Task<ActionResult<IReadOnlyList<FaceSimilarDto>>> GetSimilar(
        int id,
        [FromQuery] string? kindFamily,
        [FromQuery] int k = 20,
        CancellationToken cancellationToken = default)
    {
        k = Math.Clamp(k, 1, 100);

        var sourceEmbedding = await db.Embeddings
            .AsNoTracking()
            .Where(embedding =>
                embedding.HostType == EmbeddingHostType.Face &&
                embedding.HostId == id &&
                embedding.Modality == EmbeddingModality.Face &&
                (kindFamily == null || embedding.KindFamily == kindFamily))
            .OrderByDescending(embedding => embedding.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceEmbedding is null)
            return Ok(Array.Empty<FaceSimilarDto>());

        var results = await embeddingService.KnnAsync(
            sourceEmbedding.Vector,
            k + 1,
            new EmbeddingSearchOptions
            {
                HostType = EmbeddingHostType.Face,
                KindFamily = sourceEmbedding.KindFamily,
                Modality = EmbeddingModality.Face,
            },
            cancellationToken);

        var faceIds = results
            .Where(result => result.Embedding.HostId != id)
            .Select(result => result.Embedding.HostId)
            .Distinct()
            .Take(k)
            .ToArray();

        if (faceIds.Length == 0)
            return Ok(Array.Empty<FaceSimilarDto>());

        var faces = await db.Faces
            .AsNoTracking()
            .Include(face => face.Performer)
            .Where(face => faceIds.Contains(face.Id))
            .ToDictionaryAsync(face => face.Id, cancellationToken);

        var response = results
            .Where(result => result.Embedding.HostId != id)
            .GroupBy(result => result.Embedding.HostId)
            .Select(group => group.OrderBy(result => result.Distance).First())
            .OrderBy(result => result.Distance)
            .Take(k)
            .Where(result => faces.ContainsKey(result.Embedding.HostId))
            .Select(result =>
            {
                var face = faces[result.Embedding.HostId];
                return new FaceSimilarDto(
                    face.Id,
                    face.Label,
                    face.PerformerId,
                    face.Performer?.Name,
                    face.CoverBlobId is null ? null : EntityImageUrls.Face(face.Id, face.UpdatedAt),
                    result.Distance);
            })
            .ToList();

        return Ok(response);
    }

    private static FaceDto MapToDto(Face face) => new(
        face.Id,
        face.Label,
        face.PerformerId,
        face.Performer?.Name,
        face.CoverBlobId is null ? null : EntityImageUrls.Face(face.Id, face.UpdatedAt),
        face.Ignored,
        face.MergedIntoFaceId,
        face.DetectionCount,
        face.SceneCount,
        face.ImageCount,
        face.PrimarySourceKey,
        face.CreatedAt,
        face.UpdatedAt);

    private static DetectionDto MapDetectionToDto(Detection detection) => new(
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

    private async Task<FaceDeleteImpactDto> BuildDeleteImpactAsync(int faceId, bool hasCoverImage, CancellationToken cancellationToken)
    {
        var detectionCount = await db.Detections.CountAsync(
            detection => detection.RefId == faceId && detection.RefKind != null && detection.RefKind.ToLower() == "face",
            cancellationToken);
        var embeddingCount = await db.Embeddings.CountAsync(
            embedding => embedding.HostType == EmbeddingHostType.Face && embedding.HostId == faceId,
            cancellationToken);
        var segmentCount = await db.Segments.CountAsync(
            segment => segment.RefId == faceId && segment.Kind != null && segment.Kind.ToLower() == "face",
            cancellationToken);
        var releasedMergedFaceCount = await db.Faces.CountAsync(
            face => face.MergedIntoFaceId == faceId,
            cancellationToken);

        return new FaceDeleteImpactDto(
            detectionCount,
            embeddingCount,
            segmentCount,
            hasCoverImage,
            releasedMergedFaceCount);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}