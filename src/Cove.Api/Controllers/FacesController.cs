using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Services;
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
    ILogger<FacesController> logger,
    IEnumerable<IFaceSuggester>? faceSuggesters = null,
    ICurrentPrincipalAccessor? principalAccessor = null) : ControllerBase
{
    private const int TopSuggestionCandidateCount = 3;

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
            .ThenByDescending(face => face.AppearanceCount)
            .ThenByDescending(face => face.FrameSampleCount)
            .ThenBy(face => face.Label)
            .ThenBy(face => face.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(cancellationToken);

        var computedCounts = await LoadComputedCountsAsync(items.Select(face => face.Id).ToArray(), cancellationToken);
        var topSuggestions = await BuildTopSuggestionsAsync(items, cancellationToken);

        return Ok(new PaginatedResponse<FaceDto>(
            items.Select(face => MapToDto(
                face,
                computedCounts.TryGetValue(face.Id, out var counts) ? counts : null,
                topSuggestions.GetValueOrDefault(face.Id))).ToList(),
            totalCount,
            page,
            perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FaceDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var face = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        var computedCounts = await LoadComputedCountsAsync(new[] { id }, cancellationToken);
        var topSuggestion = await BuildTopSuggestionAsync(face, cancellationToken);
        return Ok(MapToDto(
            face,
            computedCounts.TryGetValue(face.Id, out var counts) ? counts : null,
            topSuggestion));
    }

    [HttpGet("{id:int}/appearances")]
    public async Task<ActionResult<FaceAppearancesResponseDto>> GetAppearances(int id, CancellationToken cancellationToken)
    {
        var faceExists = await db.Faces.AsNoTracking().AnyAsync(face => face.Id == id, cancellationToken);
        if (!faceExists)
            return NotFound();

        var appearances = await db.FaceAppearances
            .AsNoTracking()
            .Where(appearance => appearance.FaceId == id)
            .OrderBy(appearance => appearance.HostType)
            .ThenByDescending(appearance => appearance.LastSeenAtSec ?? appearance.FirstSeenAtSec ?? double.MinValue)
            .ThenByDescending(appearance => appearance.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (appearances.Count == 0)
            return Ok(await BuildFallbackAppearanceResponseAsync(id, cancellationToken));

        Dictionary<int, string?> sceneTitles = [];
        var sceneIds = appearances
            .Where(appearance => appearance.HostType == FaceAppearanceHostType.Scene)
            .Select(appearance => appearance.HostId)
            .Distinct()
            .ToArray();
        if (sceneIds.Length > 0)
        {
            sceneTitles = await db.Scenes
                .AsNoTracking()
                .Where(scene => sceneIds.Contains(scene.Id))
                .ToDictionaryAsync(scene => scene.Id, scene => scene.Title, cancellationToken);
        }

        Dictionary<int, string?> imageTitles = [];
        var imageIds = appearances
            .Where(appearance => appearance.HostType == FaceAppearanceHostType.Image)
            .Select(appearance => appearance.HostId)
            .Distinct()
            .ToArray();
        if (imageIds.Length > 0)
        {
            imageTitles = await db.Images
                .AsNoTracking()
                .Where(image => imageIds.Contains(image.Id))
                .ToDictionaryAsync(image => image.Id, image => image.Title, cancellationToken);
        }

        var items = appearances
            .Select(appearance => new FaceAppearanceDto(
                appearance.Id,
                appearance.HostType == FaceAppearanceHostType.Scene ? "scene" : "image",
                appearance.HostId,
                ResolveAppearanceTitle(appearance, sceneTitles, imageTitles),
                ResolveAppearanceThumbnailUrl(appearance.HostType, appearance.HostId),
                appearance.SampleCount,
                appearance.RetainedSpatialSampleCount,
                appearance.SegmentCount,
                appearance.FirstSeenAtSec,
                appearance.LastSeenAtSec,
                appearance.TopConfidence))
            .ToList();

        return Ok(new FaceAppearancesResponseDto(items, sceneIds.Length, imageIds.Length));
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

    [HttpGet("{id:int}/suggestions")]
    [RequiresPermission(Permissions.FacesRead)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesRead)]
    public async Task<ActionResult<IReadOnlyList<FaceSuggestionDto>>> GetSuggestions(
        int id,
        [FromQuery] int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        var face = await db.Faces
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        if (face.PerformerId.HasValue)
            return Ok(Array.Empty<FaceSuggestionDto>());

        return Ok(await BuildRankedSuggestionsAsync(id, maxResults, cancellationToken));
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
        var appearances = await db.FaceAppearances
            .Where(appearance => appearance.FaceId == id)
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

        if (appearances.Count > 0)
            db.FaceAppearances.RemoveRange(appearances);

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

    [HttpPost("{id:int}/suggestions/decision")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<IActionResult> RecordSuggestionDecision(int id, [FromBody] FaceSuggestionDecisionDto dto, CancellationToken cancellationToken)
    {
        if (principalAccessor?.Current?.UserId is not int userId)
            return Unauthorized();

        var normalizedDecision = dto.Decision.Trim().ToLowerInvariant();
        if (normalizedDecision is not FaceSuggestionDecisionValues.Accept and not FaceSuggestionDecisionValues.Reject)
            return ValidationProblem("Decision must be 'accept' or 'reject'.");

        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        var performerExists = await db.Performers.AnyAsync(performer => performer.Id == dto.PerformerId, cancellationToken);
        if (!performerExists)
            return ValidationProblem($"Performer {dto.PerformerId} was not found.");

        var decision = await db.FaceSuggestionDecisions
            .FirstOrDefaultAsync(item => item.FaceId == id && item.PerformerId == dto.PerformerId && item.UserId == userId, cancellationToken);

        if (decision is null)
        {
            db.FaceSuggestionDecisions.Add(new FaceSuggestionDecision
            {
                FaceId = id,
                PerformerId = dto.PerformerId,
                UserId = userId,
                Decision = normalizedDecision,
            });
        }
        else
        {
            decision.Decision = normalizedDecision;
        }

        if (normalizedDecision == FaceSuggestionDecisionValues.Accept)
            face.PerformerId = dto.PerformerId;

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
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

    private async Task<FaceTopSuggestionDto?> BuildTopSuggestionAsync(Face face, CancellationToken cancellationToken)
    {
        if (face.PerformerId.HasValue)
        {
            return null;
        }

        var suggestions = await BuildRankedSuggestionsAsync(face.Id, TopSuggestionCandidateCount, cancellationToken);
        var topSuggestion = suggestions.FirstOrDefault();
        return topSuggestion is null ? null : MapTopSuggestion(topSuggestion);
    }

    private async Task<Dictionary<int, FaceTopSuggestionDto>> BuildTopSuggestionsAsync(IReadOnlyCollection<Face> faces, CancellationToken cancellationToken)
    {
        // Short-circuit when no real suggesters are registered (only the empty stub).
        // This avoids per-face DB hits for blocked-decision lookups on the list endpoint.
        var activeSuggesters = (faceSuggesters ?? []).Where(s => s is not EmptyFaceSuggester).ToArray();
        if (activeSuggesters.Length == 0)
        {
            return [];
        }

        var eligibleFaceIds = faces
            .Where(face => !face.PerformerId.HasValue)
            .Select(face => face.Id)
            .ToArray();
        if (eligibleFaceIds.Length == 0)
        {
            return [];
        }

        var blockedByFaceId = await LoadBlockedSuggestionIdsAsync(eligibleFaceIds, cancellationToken);

        // Run per-face ranked-suggestion lookups sequentially: each suggester reuses the
        // shared scoped DbContext, so concurrent EF queries would trigger
        // NpgsqlOperationInProgressException. Within a face, the suggester fan-out is
        // still concurrent inside BuildRankedSuggestionsAsync.
        var topSuggestions = new Dictionary<int, FaceTopSuggestionDto>(eligibleFaceIds.Length);
        foreach (var faceId in eligibleFaceIds)
        {
            blockedByFaceId.TryGetValue(faceId, out var blockedIds);
            var suggestions = await BuildRankedSuggestionsAsync(faceId, blockedIds, TopSuggestionCandidateCount, cancellationToken);
            var top = suggestions.FirstOrDefault();
            if (top is not null)
            {
                topSuggestions[faceId] = MapTopSuggestion(top);
            }
        }
        return topSuggestions;
    }

    private async Task<IReadOnlyList<FaceSuggestionDto>> BuildRankedSuggestionsAsync(int faceId, int maxResults, CancellationToken cancellationToken)
    {
        var blockedByFaceId = await LoadBlockedSuggestionIdsAsync(new[] { faceId }, cancellationToken);
        blockedByFaceId.TryGetValue(faceId, out var blockedIds);
        return await BuildRankedSuggestionsAsync(faceId, blockedIds, maxResults, cancellationToken);
    }

    private async Task<IReadOnlyList<FaceSuggestionDto>> BuildRankedSuggestionsAsync(
        int faceId,
        IReadOnlySet<int>? blockedPerformerIds,
        int maxResults,
        CancellationToken cancellationToken)
    {
        maxResults = Math.Clamp(maxResults, 1, 20);

        var activeSuggesters = (faceSuggesters ?? []).ToArray();
        if (activeSuggesters.Length == 0)
        {
            return [];
        }

        var suggestions = await Task.WhenAll(activeSuggesters.Select(suggester => suggester.SuggestForAsync(faceId, maxResults, cancellationToken)));
        return suggestions
            .SelectMany(items => items)
            .Where(item => blockedPerformerIds is null || !blockedPerformerIds.Contains(item.PerformerId))
            .GroupBy(item => item.PerformerId)
            .Select(group => group
                .OrderByDescending(item => item.Confidence)
                .ThenByDescending(item => item.Evidence.Count)
                .ThenBy(item => item.PerformerName)
                .First())
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.PerformerName)
            .Take(maxResults)
            .ToList();
    }

    private async Task<Dictionary<int, HashSet<int>>> LoadBlockedSuggestionIdsAsync(IReadOnlyCollection<int> faceIds, CancellationToken cancellationToken)
    {
        if (faceIds.Count == 0 || principalAccessor?.Current?.UserId is not int userId)
        {
            return [];
        }

        var blockedRows = await db.FaceSuggestionDecisions
            .AsNoTracking()
            .Where(decision => faceIds.Contains(decision.FaceId) && decision.UserId == userId)
            .Select(decision => new { decision.FaceId, decision.PerformerId })
            .ToListAsync(cancellationToken);

        return blockedRows
            .GroupBy(item => item.FaceId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.PerformerId).ToHashSet());
    }

    private static FaceTopSuggestionDto MapTopSuggestion(FaceSuggestionDto suggestion) => new(
        suggestion.PerformerId,
        suggestion.PerformerName,
        suggestion.CoverImageUrl,
        suggestion.Confidence,
        suggestion.LocalPerformerId ?? (suggestion.PerformerId > 0 ? suggestion.PerformerId : null),
        suggestion.ExternalUrl);

    private static FaceDto MapToDto(Face face, FaceComputedCounts? computedCounts = null, FaceTopSuggestionDto? topSuggestion = null) => new(
        face.Id,
        face.Label,
        face.PerformerId,
        face.Performer?.Name,
        face.CoverBlobId is null ? null : EntityImageUrls.Face(face.Id, face.UpdatedAt),
        face.Ignored,
        face.MergedIntoFaceId,
        computedCounts?.DetectionCount ?? face.DetectionCount,
        computedCounts?.SceneCount ?? face.SceneCount,
        computedCounts?.ImageCount ?? face.ImageCount,
        face.PrimarySourceKey,
        face.CreatedAt,
        face.UpdatedAt,
        computedCounts?.AppearanceCount ?? face.AppearanceCount,
        computedCounts?.FrameSampleCount ?? face.FrameSampleCount,
        topSuggestion);

    private async Task<Dictionary<int, FaceComputedCounts>> LoadComputedCountsAsync(
        IReadOnlyCollection<int> faceIds,
        CancellationToken cancellationToken)
    {
        if (faceIds.Count == 0)
            return [];

        var distinctFaceIds = faceIds.Distinct().ToArray();
        var faceIdLongs = distinctFaceIds.Select(static id => (long)id).ToArray();

        // Aggregate at the database tier so we don't materialize every detection row
        // for every face on the page (a face can have tens of thousands of detections).
        var detectionAggregates = await db.Detections
            .AsNoTracking()
            .Where(detection =>
                detection.RefId.HasValue &&
                faceIdLongs.Contains(detection.RefId.Value) &&
                detection.RefKind != null &&
                detection.RefKind.ToLower() == "face")
            .GroupBy(detection => new
            {
                FaceId = (int)detection.RefId!.Value,
                detection.HostType,
                detection.HostId,
            })
            .Select(group => new
            {
                group.Key.FaceId,
                group.Key.HostType,
                group.Key.HostId,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        var detectionCounts = detectionAggregates
            .GroupBy(row => row.FaceId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var rows = group.ToList();
                    var totalDetections = rows.Sum(row => row.Count);
                    var sceneCount = rows.Where(row => row.HostType == DetectionHostType.Scene).Select(row => row.HostId).Distinct().Count();
                    var imageCount = rows.Where(row => row.HostType == DetectionHostType.Image).Select(row => row.HostId).Distinct().Count();
                    var hostCount = rows.Select(row => (row.HostType, row.HostId)).Distinct().Count();
                    return new FaceComputedCounts(
                        totalDetections,
                        sceneCount,
                        imageCount,
                        hostCount,
                        totalDetections);
                });

        var storedCounts = await db.FaceAppearances
            .AsNoTracking()
            .Where(appearance => distinctFaceIds.Contains(appearance.FaceId))
            .GroupBy(appearance => appearance.FaceId)
            .Select(group => new
            {
                FaceId = group.Key,
                AppearanceCount = group.Count(),
                FrameSampleCount = group.Sum(item => item.SampleCount),
                SceneCount = group.Where(item => item.HostType == FaceAppearanceHostType.Scene).Select(item => item.HostId).Distinct().Count(),
                ImageCount = group.Where(item => item.HostType == FaceAppearanceHostType.Image).Select(item => item.HostId).Distinct().Count(),
            })
            .ToDictionaryAsync(
                item => item.FaceId,
                item => new FaceStoredCounts(item.AppearanceCount, item.FrameSampleCount, item.SceneCount, item.ImageCount),
                cancellationToken);

        var computedCounts = new Dictionary<int, FaceComputedCounts>(distinctFaceIds.Length);
        foreach (var faceId in distinctFaceIds)
        {
            var detectionCount = detectionCounts.GetValueOrDefault(faceId);
            var storedCount = storedCounts.GetValueOrDefault(faceId);

            computedCounts[faceId] = new FaceComputedCounts(
                detectionCount.DetectionCount,
                detectionCount.SceneCount > 0 ? detectionCount.SceneCount : storedCount.SceneCount,
                detectionCount.ImageCount > 0 ? detectionCount.ImageCount : storedCount.ImageCount,
                storedCount.AppearanceCount > 0 ? storedCount.AppearanceCount : detectionCount.AppearanceCount,
                storedCount.FrameSampleCount > 0 ? storedCount.FrameSampleCount : detectionCount.FrameSampleCount);
        }

        return computedCounts;
    }

    private async Task<FaceAppearancesResponseDto> BuildFallbackAppearanceResponseAsync(int faceId, CancellationToken cancellationToken)
    {
        var detections = await db.Detections
            .AsNoTracking()
            .Where(detection => detection.RefId == faceId && detection.RefKind != null && detection.RefKind.ToLower() == "face")
            .Select(detection => new
            {
                detection.HostType,
                detection.HostId,
                detection.ObservedAtSec,
                detection.Score,
            })
            .ToListAsync(cancellationToken);

        var groupedDetections = detections
            .GroupBy(detection => (detection.HostType, detection.HostId))
            .OrderBy(group => group.Key.HostType)
            .ThenByDescending(group => group.Max(item => item.ObservedAtSec ?? double.MinValue))
            .ThenBy(group => group.Key.HostId)
            .ToList();

        Dictionary<int, string?> sceneTitles = [];
        var sceneIds = groupedDetections
            .Where(group => group.Key.HostType == DetectionHostType.Scene)
            .Select(group => group.Key.HostId)
            .ToArray();
        if (sceneIds.Length > 0)
        {
            sceneTitles = await db.Scenes
                .AsNoTracking()
                .Where(scene => sceneIds.Contains(scene.Id))
                .ToDictionaryAsync(scene => scene.Id, scene => scene.Title, cancellationToken);
        }

        Dictionary<int, string?> imageTitles = [];
        var imageIds = groupedDetections
            .Where(group => group.Key.HostType == DetectionHostType.Image)
            .Select(group => group.Key.HostId)
            .ToArray();
        if (imageIds.Length > 0)
        {
            imageTitles = await db.Images
                .AsNoTracking()
                .Where(image => imageIds.Contains(image.Id))
                .ToDictionaryAsync(image => image.Id, image => image.Title, cancellationToken);
        }

        var items = groupedDetections
            .Select((group, index) =>
            {
                var hostType = group.Key.HostType == DetectionHostType.Scene
                    ? FaceAppearanceHostType.Scene
                    : FaceAppearanceHostType.Image;

                return new FaceAppearanceDto(
                    -(index + 1),
                    hostType == FaceAppearanceHostType.Scene ? "scene" : "image",
                    group.Key.HostId,
                    ResolveAppearanceTitle(hostType, group.Key.HostId, sceneTitles, imageTitles),
                    ResolveAppearanceThumbnailUrl(hostType, group.Key.HostId),
                    group.Count(),
                    group.Count(),
                    0,
                    group.Min(item => item.ObservedAtSec),
                    group.Max(item => item.ObservedAtSec),
                    group.Max(item => (float?)item.Score));
            })
            .ToList();

        return new FaceAppearancesResponseDto(items, sceneIds.Length, imageIds.Length);
    }

    private static string ResolveAppearanceTitle(
        FaceAppearance appearance,
        IReadOnlyDictionary<int, string?> sceneTitles,
        IReadOnlyDictionary<int, string?> imageTitles)
        => ResolveAppearanceTitle(appearance.HostType, appearance.HostId, sceneTitles, imageTitles);

    private static string ResolveAppearanceTitle(
        FaceAppearanceHostType hostType,
        int hostId,
        IReadOnlyDictionary<int, string?> sceneTitles,
        IReadOnlyDictionary<int, string?> imageTitles) => hostType switch
    {
        FaceAppearanceHostType.Scene => Clean(sceneTitles.GetValueOrDefault(hostId)) ?? $"Scene {hostId}",
        FaceAppearanceHostType.Image => Clean(imageTitles.GetValueOrDefault(hostId)) ?? $"Image {hostId}",
        _ => $"Host {hostId}",
    };

    private static string ResolveAppearanceThumbnailUrl(FaceAppearanceHostType hostType, int hostId) => hostType switch
    {
        FaceAppearanceHostType.Scene => $"/api/stream/scene/{hostId}/screenshot",
        FaceAppearanceHostType.Image => $"/api/stream/image/{hostId}/thumbnail?max=320",
        _ => string.Empty,
    };

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

    private readonly record struct FaceComputedCounts(
        int DetectionCount,
        int SceneCount,
        int ImageCount,
        int AppearanceCount,
        int FrameSampleCount);

    private readonly record struct FaceStoredCounts(
        int AppearanceCount,
        int FrameSampleCount,
        int SceneCount,
        int ImageCount);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}






