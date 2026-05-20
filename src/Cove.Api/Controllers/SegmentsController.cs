using System.Text.Json;
using System.Linq.Expressions;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.SegmentsRead)]
public class SegmentsController(CoveContext db, SegmentSpanResolver spanResolver, IServiceScopeFactory scopeFactory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<SegmentRecordDto>>> List(
        [FromQuery] string? q,
        [FromQuery] string? ids,
        [FromQuery] int? sceneId,
        [FromQuery] string? sceneIds,
        [FromQuery] string? sceneTitle,
        [FromQuery] int? tagId,
        [FromQuery] string? tagIds,
        [FromQuery] string? kind,
        [FromQuery] string? sourceKey,
        [FromQuery] bool? tagged,
        [FromQuery] float? minConfidence,
        [FromQuery] double? minDurationSec,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] string? excludeSceneIds = null,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 48,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        perPage = Math.Clamp(perPage, 1, 250);
        var sortKey = NormalizeSort(sort);
        var descending = !string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);

        var query =
            from segment in db.Segments.AsNoTracking()
            join scene in db.Scenes.AsNoTracking() on segment.HostId equals scene.Id
            join tag in db.Tags.AsNoTracking() on segment.TagId equals tag.Id into tagJoin
            from tag in tagJoin.DefaultIfEmpty()
            where segment.HostType == SegmentHostType.Scene
            select new SegmentLibraryRow
            {
                Segment = segment,
                SceneTitle = scene.Title,
                TagName = tag != null ? tag.Name : null,
            };

        var parsedIds = ParseIdList(ids);
        if (parsedIds.Count > 0)
            query = query.Where(item => parsedIds.Contains(item.Segment.Id));

        var parsedSceneIds = ParseIdList(sceneIds);
        var parsedExcludeSceneIds = ParseIdList(excludeSceneIds);
        if (sceneId.HasValue)
            query = query.Where(item => item.Segment.HostId == sceneId.Value);
        else if (parsedSceneIds.Count > 0)
            query = query.Where(item => parsedSceneIds.Contains(item.Segment.HostId));

        if (parsedExcludeSceneIds.Count > 0)
            query = query.Where(item => !parsedExcludeSceneIds.Contains(item.Segment.HostId));

        if (!string.IsNullOrWhiteSpace(sceneTitle))
        {
            var normalizedSceneTitle = sceneTitle.Trim();
            query = query.Where(item => item.SceneTitle != null && item.SceneTitle.Contains(normalizedSceneTitle));
        }

        var parsedTagIds = ParseIdList(tagIds);
        if (tagId.HasValue)
            query = query.Where(item => item.Segment.TagId == tagId.Value);
        else if (parsedTagIds.Count > 0)
            query = query.Where(item => item.Segment.TagId.HasValue && parsedTagIds.Contains(item.Segment.TagId.Value));

        if (!string.IsNullOrWhiteSpace(kind))
        {
            var normalizedKind = kind.Trim();
            query = query.Where(item => item.Segment.Kind != null && item.Segment.Kind.Contains(normalizedKind));
        }

        if (!string.IsNullOrWhiteSpace(sourceKey))
        {
            var normalizedSourceKey = sourceKey.Trim();
            query = query.Where(item => item.Segment.SourceKey.Contains(normalizedSourceKey));
        }

        if (tagged.HasValue)
            query = tagged.Value
                ? query.Where(item => item.Segment.TagId != null)
                : query.Where(item => item.Segment.TagId == null);

        if (minConfidence.HasValue)
            query = query.Where(item => item.Segment.Confidence.HasValue && item.Segment.Confidence.Value >= minConfidence.Value);

        if (minDurationSec.HasValue)
            query = query.Where(item => ((item.Segment.EndSec ?? item.Segment.StartSec) - item.Segment.StartSec) >= minDurationSec.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(item =>
                (item.Segment.Title != null && item.Segment.Title.Contains(term)) ||
                (item.Segment.Kind != null && item.Segment.Kind.Contains(term)) ||
                (item.TagName != null && item.TagName.Contains(term)) ||
                (item.SceneTitle != null && item.SceneTitle.Contains(term)) ||
                item.Segment.SourceKey.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orderedQuery = ApplyOrdering(query, sortKey, descending);
        var items = await orderedQuery
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<SegmentRecordDto>(items.Select(MapToDto).ToList(), totalCount, page, perPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SegmentRecordDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var item = await (
            from segment in db.Segments.AsNoTracking()
            join scene in db.Scenes.AsNoTracking() on segment.HostId equals scene.Id
            join tag in db.Tags.AsNoTracking() on segment.TagId equals tag.Id into tagJoin
            from tag in tagJoin.DefaultIfEmpty()
            where segment.HostType == SegmentHostType.Scene && segment.Id == id
            select new SegmentLibraryRow
            {
                Segment = segment,
                SceneTitle = scene.Title,
                TagName = tag != null ? tag.Name : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? NotFound() : Ok(MapToDto(item));
    }

    [HttpPost("bulk/remove-tag")]
    [RequiresPermission(Permissions.SegmentsWrite)]
    public async Task<ActionResult<object>> RemoveTagFromSegments([FromBody] SegmentTagBulkRemoveRequest request, CancellationToken cancellationToken)
    {
        if (request.TagId <= 0)
            return BadRequest("A valid tag id is required.");

        var ids = request.Ids?.Where(id => id > 0).Distinct().ToArray() ?? [];
        if (ids.Length == 0)
            return BadRequest("At least one segment id is required.");

        var segments = await db.Segments
            .Where(segment => ids.Contains(segment.Id) && segment.TagId == request.TagId)
            .ToListAsync(cancellationToken);

        var sceneIds = segments
            .Where(segment => segment.HostType == SegmentHostType.Scene)
            .Select(segment => segment.HostId)
            .Distinct()
            .ToArray();

        var now = DateTime.UtcNow;
        foreach (var segment in segments)
        {
            segment.TagId = null;
            segment.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var sceneId in sceneIds)
            spanResolver.EvictScene(sceneId);

        return Ok(new { count = segments.Count });
    }

    [HttpGet("source-keys/distinct")]
    public async Task<ActionResult<IReadOnlyList<SegmentDistinctValueDto>>> DistinctSourceKeys(CancellationToken cancellationToken)
    {
        var values = await db.Segments.AsNoTracking()
            .Where(segment => segment.HostType == SegmentHostType.Scene && !string.IsNullOrWhiteSpace(segment.SourceKey))
            .GroupBy(segment => segment.SourceKey)
            .Select(group => new
            {
                Value = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Value)
            .Take(200)
            .ToListAsync(cancellationToken);

        var items = values
            .Select(item => new SegmentDistinctValueDto(item.Value!, item.Count))
            .ToList();

        return Ok(items);
    }

    [HttpGet("kinds/distinct")]
    public async Task<ActionResult<IReadOnlyList<SegmentDistinctValueDto>>> DistinctKinds(CancellationToken cancellationToken)
    {
        var values = await db.Segments.AsNoTracking()
            .Where(segment => segment.HostType == SegmentHostType.Scene && segment.Kind != null && segment.Kind != string.Empty)
            .GroupBy(segment => segment.Kind!)
            .Select(group => new
            {
                Value = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Value)
            .Take(200)
            .ToListAsync(cancellationToken);

        var items = values
            .Select(item => new SegmentDistinctValueDto(item.Value, item.Count))
            .ToList();

        return Ok(items);
    }

    private static SegmentRecordDto MapToDto(SegmentLibraryRow item) => new(
        item.Segment.Id,
        item.Segment.HostType,
        item.Segment.HostId,
        item.SceneTitle,
        item.Segment.StartSec,
        item.Segment.EndSec,
        item.Segment.TagId,
        item.TagName,
        item.Segment.Kind,
        item.Segment.RefId,
        item.Segment.Payload != null ? item.Segment.Payload.RootElement.Clone() : (JsonElement?)null,
        item.Segment.SourceKey,
        item.Segment.SourceRunId,
        item.Segment.Confidence,
        item.Segment.Title,
        item.Segment.ColorHint,
        item.Segment.CreatedAt.ToString("o"),
        item.Segment.UpdatedAt.ToString("o"));

    private static string NormalizeSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return "updated_at";

        return sort.Trim().ToLowerInvariant();
    }

    private static List<int> ParseIdList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : (int?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToList();
    }

    private static IOrderedQueryable<SegmentLibraryRow> ApplyOrdering(IQueryable<SegmentLibraryRow> query, string sort, bool descending)
    {
        return sort switch
        {
            "created_at" => OrderBy(query, item => item.Segment.CreatedAt, descending),
            "start_sec" => OrderBy(query, item => item.Segment.StartSec, descending),
            "end_sec" => OrderBy(query, item => item.Segment.EndSec ?? item.Segment.StartSec, descending),
            "duration" => OrderBy(query, item => (item.Segment.EndSec ?? item.Segment.StartSec) - item.Segment.StartSec, descending),
            "confidence" => OrderBy(query, item => item.Segment.Confidence ?? -1f, descending),
            "title" => OrderBy(query, item => item.Segment.Title ?? item.Segment.Kind ?? item.TagName ?? string.Empty, descending),
            "scene_title" => OrderBy(query, item => item.SceneTitle ?? string.Empty, descending),
            "kind" => OrderBy(query, item => item.Segment.Kind ?? string.Empty, descending),
            "source_key" => OrderBy(query, item => item.Segment.SourceKey, descending),
            "tag_name" => OrderBy(query, item => item.TagName ?? string.Empty, descending),
            _ => OrderBy(query, item => item.Segment.UpdatedAt, descending),
        };
    }

    private static IOrderedQueryable<SegmentLibraryRow> OrderBy<T>(
        IQueryable<SegmentLibraryRow> query,
        Expression<Func<SegmentLibraryRow, T>> keySelector,
        bool descending)
    {
        return descending
            ? query.OrderByDescending(keySelector).ThenByDescending(item => item.Segment.Id)
            : query.OrderBy(keySelector).ThenBy(item => item.Segment.Id);
    }

    private sealed class SegmentLibraryRow
    {
        public required Segment Segment { get; init; }
        public string? SceneTitle { get; init; }
        public string? TagName { get; init; }
    }

    public sealed record SegmentTagBulkRemoveRequest(int TagId, IReadOnlyList<int>? Ids);

    // ===== Span Search =====

    [HttpPost("spans/search")]
    public async Task<ActionResult<SegmentSpanSearchResponseDto>> SearchSpans(
        [FromBody] SegmentSpanSearchRequestDto request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.Page ?? 1);
        var perPage = Math.Clamp(request.PerPage ?? 24, 1, 100);
        var sort = (request.Sort ?? "updated_at").Trim().ToLowerInvariant();
        var descending = !string.Equals(request.Direction, "asc", StringComparison.OrdinalIgnoreCase);

        // 1. Gather scene IDs matching the scope filters.
        List<(int Id, string? Title, DateTimeOffset UpdatedAt)> sceneList;
        if (request.SceneIds is { Length: > 0 })
        {
            var idSet = request.SceneIds.ToHashSet();
            var excludeSet = request.ExcludeSceneIds?.ToHashSet() ?? [];
            sceneList = await db.Scenes.AsNoTracking()
                .Where(s => idSet.Contains(s.Id) && !excludeSet.Contains(s.Id))
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.Title, s.UpdatedAt })
                .ToListAsync(ct)
                .ContinueWith(t => t.Result.Select(s => (s.Id, (string?)s.Title, (DateTimeOffset)s.UpdatedAt)).ToList(), TaskContinuationOptions.ExecuteSynchronously);
        }
        else
        {
            var excludeSet = request.ExcludeSceneIds?.ToHashSet() ?? [];
            var sceneQuery = db.Scenes.AsNoTracking()
                .Where(s => !excludeSet.Contains(s.Id));

            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                var term = request.Q.Trim();
                sceneQuery = sceneQuery.Where(s =>
                    (s.Title != null && s.Title.Contains(term)) ||
                    (s.Code != null && s.Code.Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(request.SceneTitle))
            {
                var titleTerm = request.SceneTitle.Trim();
                sceneQuery = sceneQuery.Where(s => s.Title != null && s.Title.Contains(titleTerm));
            }

            sceneQuery = (sort, descending) switch
            {
                ("title", false) => sceneQuery.OrderBy(s => s.Title),
                ("title", true) => sceneQuery.OrderByDescending(s => s.Title),
                ("created_at", false) => sceneQuery.OrderBy(s => s.CreatedAt),
                ("created_at", true) => sceneQuery.OrderByDescending(s => s.CreatedAt),
                (_, false) => sceneQuery.OrderBy(s => s.UpdatedAt),
                _ => sceneQuery.OrderByDescending(s => s.UpdatedAt),
            };

            sceneList = await sceneQuery
                .Select(s => new { s.Id, s.Title, s.UpdatedAt })
                .ToListAsync(ct)
                .ContinueWith(t => t.Result.Select(s => (s.Id, (string?)s.Title, (DateTimeOffset)s.UpdatedAt)).ToList(), TaskContinuationOptions.ExecuteSynchronously);
        }

        // 2. Resolve the active profile ID once.
        var profileId = await spanResolver.ResolveProfileIdAsync(request.Profile, ct);

        // 3. For each scene, resolve spans in parallel using fresh scopes so each task
        // gets its own DbContext/connection. The request-scoped resolver cannot be shared
        // across Task.WhenAll without tripping EF/Npgsql concurrent-command failures.
        var allItems = new List<SegmentSpanSearchResultItemDto>(sceneList.Count * 2);
        const int batchSize = 16;
        var derivedQueryRequest = request.DerivedQuery is { } dq
            ? new SegmentSpanQueryRequestDto(
                profileId,
                dq.Operator,
                dq.Operands,
                dq.MergeGapSec,
                dq.MinDurationSec)
            : null;

        for (var i = 0; i < sceneList.Count; i += batchSize)
        {
            var batch = sceneList.Skip(i).Take(batchSize).ToList();
            var batchResults = await Task.WhenAll(batch.Select(async scene =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scopedResolver = scope.ServiceProvider.GetRequiredService<SegmentSpanResolver>();

                IReadOnlyList<ResolvedSpan> spans;
                if (derivedQueryRequest is not null)
                {
                    spans = await scopedResolver.QuerySceneAsync(scene.Id, derivedQueryRequest, ct);
                }
                else
                {
                    var resolved = await scopedResolver.ResolveSceneAsync(scene.Id, profileId, ct);
                    spans = resolved.Spans;
                }

                return (scene, spans);
            }));

            foreach (var (scene, spans) in batchResults)
            {
                foreach (var span in spans)
                    allItems.Add(new SegmentSpanSearchResultItemDto(span, scene.Id, scene.Title, scene.UpdatedAt.ToString("o"), profileId));
            }
        }

        var totalCount = allItems.Count;
        var offset = (page - 1) * perPage;
        var pageItems = allItems.Skip(offset).Take(perPage).ToList();

        return Ok(new SegmentSpanSearchResponseDto(pageItems, totalCount, page, perPage));
    }
}