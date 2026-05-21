using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
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
    FacePerformerPropagationService facePerformerPropagationService,
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
        [FromQuery] string? performerIds,
        [FromQuery] bool? linked,
        [FromQuery] bool? ignored,
        [FromQuery] bool? merged,
        [FromQuery] float? minSuggestionConfidence,
        [FromQuery] float? suggestionConfidence,
        [FromQuery] float? suggestionConfidence2,
        [FromQuery] string? suggestionConfidenceModifier,
        [FromQuery] string? topSuggestionPerformerIds,
        [FromQuery] string? sort,
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

        query = FullTextSearchHelpers.Apply(db, query, q,
            face => face.Label,
            face => face.PrimarySourceKey,
            face => face.SearchText,
            face => face.Performer != null ? face.Performer.Name : null);

        if (performerId.HasValue)
            query = query.Where(face => face.PerformerId == performerId.Value);

        var parsedPerformerIds = ParseIntList(performerIds);
        if (parsedPerformerIds.Count > 0)
            query = query.Where(face => face.PerformerId.HasValue && parsedPerformerIds.Contains(face.PerformerId.Value));

        if (linked.HasValue)
            query = linked.Value
                ? query.Where(face => face.PerformerId != null)
                : query.Where(face => face.PerformerId == null);

        if (ignored.HasValue)
            query = query.Where(face => face.Ignored == ignored.Value);

        if (merged.HasValue)
            query = merged.Value
                ? query.Where(face => face.MergedIntoFaceId != null)
                : query.Where(face => face.MergedIntoFaceId == null);

        var sortedQuery = FullTextSearchHelpers.IsActive(db, q)
            ? FullTextSearchHelpers.OrderByRelevance(db, query, q)
            : ApplyFaceSort(query, sort);

        var parsedTopSuggestionPerformerIds = ParseIntList(topSuggestionPerformerIds);
        var hasTopSuggestionFilter = minSuggestionConfidence.HasValue || suggestionConfidence.HasValue || parsedTopSuggestionPerformerIds.Count > 0;
        if (hasTopSuggestionFilter)
        {
            var normalizedMinSuggestionConfidence = minSuggestionConfidence.HasValue
                ? NormalizeConfidenceThreshold(minSuggestionConfidence.Value)
                : (float?)null;
            var normalizedSuggestionConfidence = suggestionConfidence.HasValue
                ? NormalizeConfidenceThreshold(suggestionConfidence.Value)
                : (float?)null;
            var normalizedSuggestionConfidence2 = suggestionConfidence2.HasValue
                ? NormalizeConfidenceThreshold(suggestionConfidence2.Value)
                : (float?)null;
            var normalizedSuggestionConfidenceModifier = NormalizeCriterionModifier(suggestionConfidenceModifier)
                ?? (minSuggestionConfidence.HasValue ? "GREATER_THAN" : null);
            var candidates = await sortedQuery.ToListAsync(cancellationToken);
            var candidateTopSuggestions = await BuildTopSuggestionsAsync(candidates, cancellationToken);
            var filtered = candidates
                .Where(face =>
                    candidateTopSuggestions.TryGetValue(face.Id, out var suggestion)
                    && (!normalizedMinSuggestionConfidence.HasValue || NormalizeConfidenceThreshold(suggestion.Confidence) >= normalizedMinSuggestionConfidence.Value)
                    && MatchesConfidenceCriterion(NormalizeConfidenceThreshold(suggestion.Confidence), normalizedSuggestionConfidenceModifier, normalizedSuggestionConfidence, normalizedSuggestionConfidence2)
                    && (parsedTopSuggestionPerformerIds.Count == 0 || parsedTopSuggestionPerformerIds.Contains(suggestion.LocalPerformerId ?? suggestion.PerformerId)))
                .ToList();

            if (IsSuggestionConfidenceSort(sort))
            {
                filtered = filtered
                    .OrderByDescending(face => candidateTopSuggestions.TryGetValue(face.Id, out var suggestion) ? suggestion.Confidence : -1f)
                    .ThenByDescending(face => face.UpdatedAt)
                    .ThenBy(face => face.Id)
                    .ToList();
            }

            var totalFilteredCount = filtered.Count;
            var filteredPage = filtered
                .Skip((page - 1) * perPage)
                .Take(perPage)
                .ToList();
            var filteredComputedCounts = await LoadComputedCountsAsync(filteredPage.Select(face => face.Id).ToArray(), cancellationToken);

            return Ok(new PaginatedResponse<FaceDto>(
                filteredPage.Select(face => MapToDto(
                    face,
                    filteredComputedCounts.TryGetValue(face.Id, out var counts) ? counts : null,
                    candidateTopSuggestions.GetValueOrDefault(face.Id))).ToList(),
                totalFilteredCount,
                page,
                perPage));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await sortedQuery
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(cancellationToken);

        var computedCounts = await LoadComputedCountsAsync(items.Select(face => face.Id).ToArray(), cancellationToken);
        var topSuggestions = await BuildTopSuggestionsAsync(items, cancellationToken);
        if (IsSuggestionConfidenceSort(sort))
        {
            items = items
                .OrderByDescending(face => topSuggestions.TryGetValue(face.Id, out var suggestion) ? suggestion.Confidence : -1f)
                .ThenByDescending(face => face.UpdatedAt)
                .ThenBy(face => face.Id)
                .ToList();
        }

        return Ok(new PaginatedResponse<FaceDto>(
            items.Select(face => MapToDto(
                face,
                computedCounts.TryGetValue(face.Id, out var counts) ? counts : null,
                topSuggestions.GetValueOrDefault(face.Id))).ToList(),
            totalCount,
            page,
            perPage));
    }

    private static List<int> ParseIntList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => int.TryParse(part, out var parsed) ? parsed : 0)
                .Where(static id => id > 0)
                .Distinct()
                .ToList();

    private static string? NormalizeCriterionModifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "EQUALS" or "NOT_EQUALS" or "GREATER_THAN" or "LESS_THAN" or "BETWEEN" or "NOT_BETWEEN" or "IS_NULL" or "NOT_NULL"
            ? normalized
            : null;
    }

    private static bool MatchesConfidenceCriterion(float confidence, string? modifier, float? value, float? value2)
    {
        if (modifier is null && !value.HasValue)
            return true;

        return modifier switch
        {
            "IS_NULL" => false,
            "NOT_NULL" => true,
            "NOT_EQUALS" => value.HasValue && Math.Abs(confidence - value.Value) > 0.0001f,
            "LESS_THAN" => value.HasValue && confidence < value.Value,
            "BETWEEN" => value.HasValue && value2.HasValue && confidence >= Math.Min(value.Value, value2.Value) && confidence <= Math.Max(value.Value, value2.Value),
            "NOT_BETWEEN" => value.HasValue && value2.HasValue && (confidence < Math.Min(value.Value, value2.Value) || confidence > Math.Max(value.Value, value2.Value)),
            "EQUALS" => value.HasValue && Math.Abs(confidence - value.Value) <= 0.0001f,
            _ => value.HasValue && confidence >= value.Value,
        };
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
    public async Task<ActionResult<PaginatedResponse<FaceAppearanceDto>>> GetAppearances(
        int id,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 24,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        perPage = Math.Clamp(perPage, 1, 250);

        var faceExists = await db.Faces.AsNoTracking().AnyAsync(face => face.Id == id, cancellationToken);
        if (!faceExists)
            return NotFound();

        var items = await LoadFaceAppearanceItemsAsync(id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(q))
        {
            items = items
                .Where(item =>
                    item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || item.HostType.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        items = ApplyAppearanceSort(items, sort, direction);
        var totalCount = items.Count;
        var pageItems = items
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToList();

        return Ok(new PaginatedResponse<FaceAppearanceDto>(pageItems, totalCount, page, perPage));
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

    [HttpGet("/api/scenes/{sceneId:int}/faces")]
    public async Task<ActionResult<IReadOnlyList<FaceHostFaceDto>>> GetSceneFaces(int sceneId, CancellationToken cancellationToken)
        => Ok(await LoadHostFacesAsync(FaceAppearanceHostType.Scene, sceneId, cancellationToken));

    [HttpGet("/api/images/{imageId:int}/faces")]
    public async Task<ActionResult<IReadOnlyList<FaceHostFaceDto>>> GetImageFaces(int imageId, CancellationToken cancellationToken)
        => Ok(await LoadHostFacesAsync(FaceAppearanceHostType.Image, imageId, cancellationToken));

    [HttpGet("/api/performers/{performerId:int}/faces")]
    public async Task<ActionResult<IReadOnlyList<FaceDto>>> GetPerformerFaces(int performerId, CancellationToken cancellationToken)
    {
        var faces = await db.Faces
            .AsNoTracking()
            .Include(face => face.Performer)
            .Where(face => face.PerformerId == performerId && face.MergedIntoFaceId == null)
            .OrderByDescending(face => face.AppearanceCount)
            .ThenBy(face => face.Label)
            .ThenBy(face => face.Id)
            .ToListAsync(cancellationToken);

        var computedCounts = await LoadComputedCountsAsync(faces.Select(face => face.Id).ToArray(), cancellationToken);
        return Ok(faces.Select(face => MapToDto(face, computedCounts.GetValueOrDefault(face.Id))).ToList());
    }

    [HttpGet("review/unlinked")]
    public async Task<ActionResult<IReadOnlyList<FaceDto>>> GetUnlinkedReviewFaces(
        [FromQuery] int take = 24,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var faces = await db.Faces
            .AsNoTracking()
            .Include(face => face.Performer)
            .Where(face => face.PerformerId == null && face.MergedIntoFaceId == null && !face.Ignored)
            .OrderByDescending(face => face.AppearanceCount)
            .ThenByDescending(face => face.FrameSampleCount)
            .ThenBy(face => face.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var computedCounts = await LoadComputedCountsAsync(faces.Select(face => face.Id).ToArray(), cancellationToken);
        var topSuggestions = await BuildTopSuggestionsAsync(faces, cancellationToken);
        return Ok(faces.Select(face => MapToDto(
            face,
            computedCounts.GetValueOrDefault(face.Id),
            topSuggestions.GetValueOrDefault(face.Id))).ToList());
    }

    [HttpGet("review/ai-run")]
    public async Task<ActionResult<IReadOnlyList<FaceDto>>> GetAiRunReviewFaces(
        [FromQuery] DateTime? startedAt,
        [FromQuery] DateTime? completedAt,
        [FromQuery] int take = 12,
        CancellationToken cancellationToken = default)
    {
        if (!startedAt.HasValue || !completedAt.HasValue)
            return Ok(Array.Empty<FaceDto>());

        take = Math.Clamp(take, 1, 100);
        var windowStart = startedAt.Value.ToUniversalTime().AddMinutes(-1);
        var windowEnd = completedAt.Value.ToUniversalTime().AddMinutes(1);
        var runs = await db.AiRuns
            .AsNoTracking()
            .Where(run => run.SourceKey == "ext:ai.core"
                && run.Status == AiRunStatus.Completed
                && run.StartedAt >= windowStart
                && (run.CompletedAt ?? run.StartedAt) <= windowEnd
                && (run.TargetType == AiRunTargetType.Scene || run.TargetType == AiRunTargetType.Image))
            .OrderByDescending(run => run.CompletedAt ?? run.StartedAt)
            .ToListAsync(cancellationToken);

        var targets = runs
            .Select(run => new { run.TargetType, run.TargetId })
            .Distinct()
            .ToArray();
        if (targets.Length != 1)
            return Ok(Array.Empty<FaceDto>());

        var target = targets[0];
        var hostType = target.TargetType == AiRunTargetType.Scene ? FaceAppearanceHostType.Scene : FaceAppearanceHostType.Image;
        return Ok(await LoadReviewFacesForHostAsync(hostType, target.TargetId, take, cancellationToken));
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

    [HttpPost("batch/link-top-suggestion")]
    [RequiresPermission(Permissions.FacesWrite)]
    public async Task<ActionResult<FaceBatchOperationResultDto>> BatchLinkTopSuggestion([FromBody] FaceBatchLinkTopSuggestionDto dto, CancellationToken cancellationToken)
    {
        var succeeded = new List<int>();
        var skipped = new List<FaceBatchSkippedDto>();
        var failed = new List<FaceBatchFailedDto>();
        var minConfidence = NormalizeConfidenceThreshold(dto.MinConfidence ?? 60f);
        var requestedFaceIds = dto.FaceIds.Distinct().ToArray();
        var facesById = requestedFaceIds.Length == 0
            ? new Dictionary<int, Face>()
            : await db.Faces
                .Where(face => requestedFaceIds.Contains(face.Id))
                .ToDictionaryAsync(face => face.Id, cancellationToken);
        var eligibleFaceIds = facesById.Values
            .Where(face => !face.PerformerId.HasValue)
            .Select(face => face.Id)
            .ToArray();
        var blockedByFaceId = await LoadBlockedSuggestionIdsAsync(eligibleFaceIds, cancellationToken);
        var suggestionsByFaceId = await BuildRankedSuggestionsByFaceAsync(
            eligibleFaceIds,
            blockedByFaceId,
            TopSuggestionCandidateCount,
            cancellationToken,
            includeReferenceMatches: true);

        foreach (var faceId in requestedFaceIds)
        {
            try
            {
                if (!facesById.TryGetValue(faceId, out var face))
                {
                    skipped.Add(new FaceBatchSkippedDto(faceId, "Face was not found."));
                    continue;
                }

                if (face.PerformerId.HasValue)
                {
                    skipped.Add(new FaceBatchSkippedDto(faceId, "Face is already linked."));
                    continue;
                }

                suggestionsByFaceId.TryGetValue(faceId, out var suggestions);
                var suggestion = suggestions?
                    .FirstOrDefault(item => ResolveLocalPerformerId(item).HasValue && NormalizeConfidenceThreshold(item.Confidence) >= minConfidence);
                var performerId = suggestion is null ? null : ResolveLocalPerformerId(suggestion);
                if (!performerId.HasValue)
                {
                    skipped.Add(new FaceBatchSkippedDto(faceId, "No local top suggestion met the confidence threshold."));
                    continue;
                }

                await facePerformerPropagationService.ApplyLinkChangeAsync(faceId, face.PerformerId, performerId, cancellationToken);
                face.PerformerId = performerId;
                succeeded.Add(faceId);
            }
            catch (Exception ex)
            {
                failed.Add(new FaceBatchFailedDto(faceId, ex.Message));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new FaceBatchOperationResultDto(succeeded, skipped, failed));
    }

    [HttpPost("batch/delete")]
    [RequiresPermission(Permissions.FacesDelete)]
    public async Task<ActionResult<FaceBatchOperationResultDto>> BatchDelete([FromBody] FaceBatchDeleteDto dto, CancellationToken cancellationToken)
    {
        var succeeded = new List<int>();
        var skipped = new List<FaceBatchSkippedDto>();
        var failed = new List<FaceBatchFailedDto>();

        foreach (var faceId in dto.FaceIds.Distinct())
        {
            try
            {
                var deleted = await DeleteFaceAsync(faceId, cancellationToken);
                if (deleted)
                    succeeded.Add(faceId);
                else
                    skipped.Add(new FaceBatchSkippedDto(faceId, "Face was not found."));
            }
            catch (Exception ex)
            {
                failed.Add(new FaceBatchFailedDto(faceId, ex.Message));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new FaceBatchOperationResultDto(succeeded, skipped, failed));
    }

    [HttpPost("{id:int}/create-performer")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<ActionResult<FaceDto>> CreatePerformerFromFace(int id, [FromBody] FaceCreatePerformerDto dto, CancellationToken cancellationToken)
    {
        var name = Clean(dto.Name);
        if (string.IsNullOrWhiteSpace(name))
            return ValidationProblem("A performer name is required.");

        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return NotFound();

        if (face.PerformerId.HasValue)
            return ValidationProblem("This face is already linked to a performer.");

        var performer = new Performer { Name = name };
        await TrySetLocalPerformerImageFromFaceAsync(face, performer, dto.SetPerformerImage, cancellationToken);

        db.Performers.Add(performer);
        await db.SaveChangesAsync(cancellationToken);

        await facePerformerPropagationService.ApplyLinkChangeAsync(id, face.PerformerId, performer.Id, cancellationToken);
        face.PerformerId = performer.Id;
        await db.SaveChangesAsync(cancellationToken);

        var updated = await db.Faces
            .AsNoTracking()
            .Include(item => item.Performer)
            .FirstAsync(item => item.Id == id, cancellationToken);

        return Ok(MapToDto(updated));
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
        await facePerformerPropagationService.ApplyLinkChangeAsync(id, face.PerformerId, dto.PerformerId, cancellationToken);
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
        if (!await DeleteFaceAsync(id, cancellationToken))
            return NotFound();
        await db.SaveChangesAsync(cancellationToken);

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

        Performer? performer = null;
        if (dto.PerformerId.HasValue)
        {
            performer = await db.Performers
                .Include(item => item.RemoteIds)
                .FirstOrDefaultAsync(item => item.Id == dto.PerformerId.Value, cancellationToken);
            if (performer is null)
                return ValidationProblem($"Performer {dto.PerformerId.Value} was not found.");
        }

        await facePerformerPropagationService.ApplyLinkChangeAsync(id, face.PerformerId, dto.PerformerId, cancellationToken);
        face.PerformerId = dto.PerformerId;
        if (performer is not null)
            await TrySetLocalPerformerImageFromFaceAsync(face, performer, dto.SetPerformerImage, cancellationToken);
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

        var performer = await db.Performers
            .Include(item => item.RemoteIds)
            .FirstOrDefaultAsync(item => item.Id == dto.PerformerId, cancellationToken);
        if (performer is null)
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
        {
            await facePerformerPropagationService.ApplyLinkChangeAsync(id, face.PerformerId, dto.PerformerId, cancellationToken);
            face.PerformerId = dto.PerformerId;
            await TrySetLocalPerformerImageFromFaceAsync(face, performer, dto.SetPerformerImage, cancellationToken);
        }

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
    public async Task<ActionResult<PaginatedResponse<FaceSimilarDto>>> GetSimilar(
        int id,
        [FromQuery] string? kindFamily,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 18,
        [FromQuery] int k = 80,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        perPage = Math.Clamp(perPage, 1, 250);
        k = Math.Clamp(k, 1, 250);
        var candidateCount = Math.Clamp(Math.Max(k, page * perPage * 4), 1, 250);

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
            return Ok(new PaginatedResponse<FaceSimilarDto>(Array.Empty<FaceSimilarDto>(), 0, page, perPage));

        var results = await embeddingService.KnnAsync(
            sourceEmbedding.Vector,
            candidateCount + 1,
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
            .Take(candidateCount)
            .ToArray();

        if (faceIds.Length == 0)
            return Ok(new PaginatedResponse<FaceSimilarDto>(Array.Empty<FaceSimilarDto>(), 0, page, perPage));

        var faces = await db.Faces
            .AsNoTracking()
            .Include(face => face.Performer)
            .Where(face => faceIds.Contains(face.Id))
            .ToDictionaryAsync(face => face.Id, cancellationToken);

        var computedCounts = await LoadComputedCountsAsync(faceIds, cancellationToken);

        var response = results
            .Where(result => result.Embedding.HostId != id)
            .GroupBy(result => result.Embedding.HostId)
            .Select(group => group.OrderBy(result => result.Distance).First())
            .OrderBy(result => result.Distance)
            .Take(candidateCount)
            .Where(result => faces.ContainsKey(result.Embedding.HostId))
            .Select(result => MapToSimilarDto(
                faces[result.Embedding.HostId],
                computedCounts.GetValueOrDefault(result.Embedding.HostId),
                result.Distance))
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            response = response
                .Where(face =>
                    (!string.IsNullOrWhiteSpace(face.Label) && face.Label.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(face.PerformerName) && face.PerformerName.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        response = ApplySimilarSort(response, sort, direction);
        var totalCount = response.Count;
        var pageItems = response
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToList();

        return Ok(new PaginatedResponse<FaceSimilarDto>(pageItems, totalCount, page, perPage));
    }

    private async Task<IReadOnlyList<FaceHostFaceDto>> LoadHostFacesAsync(FaceAppearanceHostType hostType, int hostId, CancellationToken cancellationToken)
    {
        var appearances = await db.FaceAppearances
            .AsNoTracking()
            .Include(appearance => appearance.Face)
                .ThenInclude(face => face!.Performer)
            .Where(appearance => appearance.HostType == hostType && appearance.HostId == hostId && appearance.Face != null && appearance.Face.MergedIntoFaceId == null)
            .OrderByDescending(appearance => appearance.TopConfidence ?? 0)
            .ThenBy(appearance => appearance.FaceId)
            .ToListAsync(cancellationToken);

        var computedCounts = await LoadComputedCountsAsync(appearances.Select(appearance => appearance.FaceId).Distinct().ToArray(), cancellationToken);
        return appearances
            .GroupBy(appearance => appearance.FaceId)
            .Select(group =>
            {
                var primaryAppearance = group
                    .OrderByDescending(appearance => appearance.TopConfidence ?? 0)
                    .ThenBy(appearance => appearance.Id)
                    .First();
                var face = primaryAppearance.Face!;
                var hasCounts = computedCounts.TryGetValue(face.Id, out var counts);
                return new FaceHostFaceDto(
                    face.Id,
                    face.Label,
                    face.PerformerId,
                    face.Performer?.Name,
                    face.CoverBlobId is null ? null : EntityImageUrls.Face(ControllerContext.HttpContext, face.Id, face.UpdatedAt),
                    hasCounts ? counts.AppearanceCount : face.AppearanceCount,
                    hasCounts ? counts.FrameSampleCount : face.FrameSampleCount,
                    hasCounts ? counts.SceneCount : face.SceneCount,
                    hasCounts ? counts.ImageCount : face.ImageCount,
                    MinOrNull(group.Select(appearance => appearance.FirstSeenAtSec)),
                    MaxOrNull(group.Select(appearance => appearance.LastSeenAtSec)),
                    MaxFloatOrNull(group.Select(appearance => appearance.TopConfidence)));
            })
            .OrderByDescending(face => face.TopConfidence ?? 0)
            .ThenBy(face => face.Id)
            .ToList();

        static double? MinOrNull(IEnumerable<double?> values)
        {
            var resolved = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
            return resolved.Length == 0 ? null : resolved.Min();
        }

        static double? MaxOrNull(IEnumerable<double?> values)
        {
            var resolved = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
            return resolved.Length == 0 ? null : resolved.Max();
        }

        static float? MaxFloatOrNull(IEnumerable<float?> values)
        {
            var resolved = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
            return resolved.Length == 0 ? null : resolved.Max();
        }
    }

    private static IOrderedQueryable<Face> ApplyFaceSort(IQueryable<Face> query, string? sort)
    {
        var normalized = (sort ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "created_desc" => query.OrderByDescending(face => face.CreatedAt).ThenBy(face => face.Id),
            "updated_desc" => query.OrderByDescending(face => face.UpdatedAt).ThenBy(face => face.Id),
            "appearance_desc" => query.OrderBy(face => face.MergedIntoFaceId != null).ThenByDescending(face => face.AppearanceCount).ThenByDescending(face => face.FrameSampleCount).ThenBy(face => face.Label).ThenBy(face => face.Id),
            "scene_count_desc" => query.OrderByDescending(face => face.SceneCount).ThenByDescending(face => face.AppearanceCount).ThenBy(face => face.Id),
            "image_count_desc" => query.OrderByDescending(face => face.ImageCount).ThenByDescending(face => face.AppearanceCount).ThenBy(face => face.Id),
            "suggestion_confidence" => query.OrderBy(face => face.PerformerId != null).ThenByDescending(face => face.AppearanceCount).ThenByDescending(face => face.UpdatedAt).ThenBy(face => face.Id),
            _ => query.OrderBy(face => face.MergedIntoFaceId != null).ThenByDescending(face => face.AppearanceCount).ThenByDescending(face => face.FrameSampleCount).ThenBy(face => face.Label).ThenBy(face => face.Id),
        };
    }

    private static bool IsSuggestionConfidenceSort(string? sort)
        => string.Equals(sort?.Trim(), "suggestion_confidence", StringComparison.OrdinalIgnoreCase);

    private async Task<List<FaceAppearanceDto>> LoadFaceAppearanceItemsAsync(int faceId, CancellationToken cancellationToken)
    {
        var appearances = await db.FaceAppearances
            .AsNoTracking()
            .Where(appearance => appearance.FaceId == faceId)
            .OrderBy(appearance => appearance.HostType)
            .ThenByDescending(appearance => appearance.LastSeenAtSec ?? appearance.FirstSeenAtSec ?? double.MinValue)
            .ThenByDescending(appearance => appearance.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (appearances.Count == 0)
            return await BuildFallbackAppearanceItemsAsync(faceId, cancellationToken);

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

        return appearances
            .GroupBy(appearance => new { appearance.HostType, appearance.HostId })
            .Select(group =>
            {
                var primaryAppearance = group
                    .OrderByDescending(appearance => appearance.TopConfidence ?? 0)
                    .ThenBy(appearance => appearance.Id)
                    .First();
                return new FaceAppearanceDto(
                    primaryAppearance.Id,
                    primaryAppearance.HostType == FaceAppearanceHostType.Scene ? "scene" : "image",
                    primaryAppearance.HostId,
                    ResolveAppearanceTitle(primaryAppearance, sceneTitles, imageTitles),
                    ResolveAppearanceThumbnailUrl(primaryAppearance.HostType, primaryAppearance.HostId),
                    group.Sum(appearance => appearance.SampleCount),
                    group.Sum(appearance => appearance.RetainedSpatialSampleCount),
                    group.Sum(appearance => appearance.SegmentCount),
                    MinOrNull(group.Select(appearance => appearance.FirstSeenAtSec)),
                    MaxOrNull(group.Select(appearance => appearance.LastSeenAtSec)),
                    MaxFloatOrNull(group.Select(appearance => appearance.TopConfidence)));
            })
            .ToList();
    }

    private static List<FaceAppearanceDto> ApplyAppearanceSort(IEnumerable<FaceAppearanceDto> items, string? sort, string? direction)
    {
        var normalized = (sort ?? string.Empty).Trim().ToLowerInvariant();
        var ascending = ResolveSortDirection(direction, normalized is "title" or "host_type");

        return normalized switch
        {
            "title" => OrderBy(items, item => item.Title, ascending).ThenBy(item => item.HostId).ToList(),
            "host_type" => OrderBy(items, item => item.HostType, ascending).ThenBy(item => item.Title).ToList(),
            "sample_count" => OrderBy(items, item => item.FrameSampleCount, ascending).ThenBy(item => item.Title).ToList(),
            "confidence" => OrderBy(items, item => item.TopConfidence ?? float.MinValue, ascending).ThenBy(item => item.Title).ToList(),
            "first_seen" => OrderBy(items, item => item.FirstSeenAtSec ?? double.MinValue, ascending).ThenBy(item => item.Title).ToList(),
            _ => OrderBy(items, item => item.LastSeenAtSec ?? item.FirstSeenAtSec ?? double.MinValue, ascending).ThenBy(item => item.Title).ToList(),
        };
    }

    private static List<FaceSimilarDto> ApplySimilarSort(IEnumerable<FaceSimilarDto> items, string? sort, string? direction)
    {
        var normalized = (sort ?? string.Empty).Trim().ToLowerInvariant();
        var ascending = ResolveSortDirection(direction, normalized is "distance" or "label");

        return normalized switch
        {
            "label" => OrderBy(items, item => item.Label ?? item.PerformerName ?? string.Empty, ascending).ThenBy(item => item.Id).ToList(),
            "updated_at" => OrderBy(items, item => item.UpdatedAt, ascending).ThenBy(item => item.Id).ToList(),
            "appearance_count" => OrderBy(items, item => item.AppearanceCount, ascending).ThenBy(item => item.Distance).ToList(),
            "scene_count" => OrderBy(items, item => item.SceneCount, ascending).ThenBy(item => item.Distance).ToList(),
            "image_count" => OrderBy(items, item => item.ImageCount, ascending).ThenBy(item => item.Distance).ToList(),
            _ => OrderBy(items, item => item.Distance, ascending).ThenBy(item => item.Id).ToList(),
        };
    }

    private static IOrderedEnumerable<TItem> OrderBy<TItem, TKey>(IEnumerable<TItem> items, Func<TItem, TKey> keySelector, bool ascending)
        where TKey : IComparable<TKey>
        => ascending ? items.OrderBy(keySelector) : items.OrderByDescending(keySelector);

    private static bool ResolveSortDirection(string? direction, bool defaultAscending)
        => string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) && defaultAscending);

    private async Task<IReadOnlyList<FaceDto>> LoadReviewFacesForHostAsync(FaceAppearanceHostType hostType, int hostId, int take, CancellationToken cancellationToken)
    {
        var faceIds = await db.FaceAppearances
            .AsNoTracking()
            .Where(appearance => appearance.HostType == hostType && appearance.HostId == hostId)
            .Select(appearance => appearance.FaceId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (faceIds.Length == 0)
            return [];

        var candidateTake = Math.Clamp(take * 4, take, 100);
        var faces = await db.Faces
            .AsNoTracking()
            .Include(face => face.Performer)
            .Where(face => faceIds.Contains(face.Id) && face.PerformerId == null && face.MergedIntoFaceId == null && !face.Ignored)
            .OrderByDescending(face => face.AppearanceCount)
            .ThenByDescending(face => face.FrameSampleCount)
            .ThenBy(face => face.Id)
            .Take(candidateTake)
            .ToListAsync(cancellationToken);
        if (faces.Count == 0)
            return [];

        var computedCounts = await LoadComputedCountsAsync(faces.Select(face => face.Id).ToArray(), cancellationToken);
        var topSuggestions = await BuildTopSuggestionsAsync(faces, cancellationToken);
        return faces
            .Where(face => topSuggestions.ContainsKey(face.Id))
            .OrderByDescending(face => topSuggestions[face.Id].Confidence)
            .ThenByDescending(face => face.AppearanceCount)
            .Take(take)
            .Select(face => MapToDto(face, computedCounts.GetValueOrDefault(face.Id), topSuggestions.GetValueOrDefault(face.Id)))
            .ToList();
    }

    private async Task<bool> DeleteFaceAsync(int id, CancellationToken cancellationToken)
    {
        var face = await db.Faces.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (face is null)
            return false;

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

        await facePerformerPropagationService.ApplyLinkChangeAsync(id, face.PerformerId, null, cancellationToken);

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

        return true;
    }

    private static int? ResolveLocalPerformerId(FaceSuggestionDto suggestion)
        => suggestion.LocalPerformerId ?? (suggestion.PerformerId > 0 ? suggestion.PerformerId : null);

    private static float NormalizeConfidenceThreshold(float confidence)
        => confidence <= 1f ? confidence * 100f : confidence;

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

        var rankedSuggestionsByFaceId = await BuildRankedSuggestionsByFaceAsync(
            eligibleFaceIds,
            blockedByFaceId,
            TopSuggestionCandidateCount,
            cancellationToken,
            includeReferenceMatches: true);
        var topSuggestions = new Dictionary<int, FaceTopSuggestionDto>(rankedSuggestionsByFaceId.Count);
        foreach (var (faceId, suggestions) in rankedSuggestionsByFaceId)
        {
            var top = suggestions.FirstOrDefault();
            if (top is not null)
            {
                topSuggestions[faceId] = MapTopSuggestion(top);
            }
        }
        return topSuggestions;
    }

    private async Task<Dictionary<int, IReadOnlyList<FaceSuggestionDto>>> BuildRankedSuggestionsByFaceAsync(
        IReadOnlyCollection<int> faceIds,
        IReadOnlyDictionary<int, HashSet<int>> blockedByFaceId,
        int maxResults,
        CancellationToken cancellationToken,
        bool includeReferenceMatches = true)
    {
        maxResults = Math.Clamp(maxResults, 1, 20);
        var distinctFaceIds = faceIds.Where(static faceId => faceId > 0).Distinct().ToArray();
        if (distinctFaceIds.Length == 0)
        {
            return [];
        }

        var activeSuggesters = (faceSuggesters ?? []).Where(suggester => suggester is not EmptyFaceSuggester).ToArray();
        if (activeSuggesters.Length == 0)
        {
            return [];
        }

        var suggestionOptions = new FaceSuggestionOptions(IncludeReferenceMatches: includeReferenceMatches);
        var suggestionsByFaceId = new Dictionary<int, List<FaceSuggestionDto>>();
        foreach (var suggester in activeSuggesters)
        {
            var batch = await suggester.SuggestForBatchAsync(distinctFaceIds, maxResults, suggestionOptions, cancellationToken);
            foreach (var (faceId, suggestions) in batch)
            {
                if (!suggestionsByFaceId.TryGetValue(faceId, out var faceSuggestions))
                {
                    faceSuggestions = [];
                    suggestionsByFaceId[faceId] = faceSuggestions;
                }

                faceSuggestions.AddRange(suggestions);
            }
        }

        return suggestionsByFaceId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<FaceSuggestionDto>)pair.Value
                .Where(item => !blockedByFaceId.TryGetValue(pair.Key, out var blockedPerformerIds) || !blockedPerformerIds.Contains(item.PerformerId))
                .GroupBy(item => item.PerformerId)
                .Select(group => group
                    .OrderByDescending(item => item.Confidence)
                    .ThenByDescending(item => item.Evidence.Count)
                    .ThenBy(item => item.PerformerName)
                    .First())
                .OrderByDescending(item => item.Confidence)
                .ThenBy(item => item.PerformerName)
                .Take(maxResults)
                .ToList());
    }

    private async Task<IReadOnlyList<FaceSuggestionDto>> BuildRankedSuggestionsAsync(
        int faceId,
        int maxResults,
        CancellationToken cancellationToken,
        bool includeReferenceMatches = true)
    {
        var blockedByFaceId = await LoadBlockedSuggestionIdsAsync(new[] { faceId }, cancellationToken);
        blockedByFaceId.TryGetValue(faceId, out var blockedIds);
        return await BuildRankedSuggestionsAsync(faceId, blockedIds, maxResults, cancellationToken, includeReferenceMatches);
    }

    private async Task<IReadOnlyList<FaceSuggestionDto>> BuildRankedSuggestionsAsync(
        int faceId,
        IReadOnlySet<int>? blockedPerformerIds,
        int maxResults,
        CancellationToken cancellationToken,
        bool includeReferenceMatches = true)
    {
        maxResults = Math.Clamp(maxResults, 1, 20);

        var activeSuggesters = (faceSuggesters ?? []).ToArray();
        if (activeSuggesters.Length == 0)
        {
            return [];
        }

        var suggestionOptions = new FaceSuggestionOptions(IncludeReferenceMatches: includeReferenceMatches);
        var suggestions = await Task.WhenAll(activeSuggesters.Select(suggester => suggester.SuggestForAsync(faceId, maxResults, suggestionOptions, cancellationToken)));
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
        suggestion.ExternalUrl,
        suggestion.LocalPerformerHasImage,
        suggestion.LocalPerformerIsLocalOnly);

    private async Task TrySetLocalPerformerImageFromFaceAsync(
        Face face,
        Performer performer,
        bool setPerformerImage,
        CancellationToken cancellationToken)
    {
        if (!setPerformerImage
            || string.IsNullOrWhiteSpace(face.CoverBlobId)
            || !string.IsNullOrWhiteSpace(performer.ImageBlobId)
            || performer.RemoteIds.Count > 0)
        {
            return;
        }

        var blob = await blobService.GetBlobAsync(face.CoverBlobId, cancellationToken);
        if (blob is null)
        {
            return;
        }

        await using var stream = blob.Value.Stream;
        performer.ImageBlobId = await blobService.StoreBlobAsync(stream, blob.Value.ContentType, cancellationToken);
    }

    private FaceDto MapToDto(Face face, FaceComputedCounts? computedCounts = null, FaceTopSuggestionDto? topSuggestion = null) => new(
        face.Id,
        face.Label,
        face.PerformerId,
        face.Performer?.Name,
        face.CoverBlobId is null ? null : EntityImageUrls.Face(ControllerContext.HttpContext, face.Id, face.UpdatedAt),
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

    private FaceSimilarDto MapToSimilarDto(Face face, FaceComputedCounts? computedCounts, float distance) => new(
        face.Id,
        face.Label,
        face.PerformerId,
        face.Performer?.Name,
        face.CoverBlobId is null ? null : EntityImageUrls.Face(ControllerContext.HttpContext, face.Id, face.UpdatedAt),
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
        distance);

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

    private async Task<List<FaceAppearanceDto>> BuildFallbackAppearanceItemsAsync(int faceId, CancellationToken cancellationToken)
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

        return items;
    }

    private static double? MinOrNull(IEnumerable<double?> values)
    {
        var resolved = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return resolved.Length == 0 ? null : resolved.Min();
    }

    private static double? MaxOrNull(IEnumerable<double?> values)
    {
        var resolved = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return resolved.Length == 0 ? null : resolved.Max();
    }

    private static float? MaxFloatOrNull(IEnumerable<float?> values)
    {
        var resolved = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return resolved.Length == 0 ? null : resolved.Max();
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






