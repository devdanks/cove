using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;
using IAuthorizationService = Cove.Core.Auth.IAuthorizationService;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.TagsRead)]
public class TagsController(ITagRepository tagRepo, Data.CoveContext db, IEntityIdentifierService entityIdentifiers, CustomFieldService customFields) : ControllerBase
{
    private sealed record TagUsageCounts(
        int SceneCount,
        int SegmentCount,
        int ImageCount,
        int GalleryCount,
        int GroupCount,
        int PerformerCount,
        int StudioCount)
    {
        public int TotalUsageCount => SceneCount + SegmentCount + ImageCount + GalleryCount + GroupCount + PerformerCount + StudioCount;
    }

    private sealed record GraphRelation(int ParentId, int ChildId);

    [HttpGet]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<PaginatedResponse<TagListDto>>> Find(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int perPage = 25,
        [FromQuery] string? sort = null, [FromQuery] string? direction = null,
        [FromQuery] int? seed = null,
        [FromQuery] string? name = null, [FromQuery] bool? favorite = null,
        CancellationToken ct = default)
    {
        var filter = new TagFilter { Name = name, Favorite = favorite };
        var findFilter = new FindFilter
        {
            Q = q, Page = page, PerPage = perPage, Sort = sort,
            Direction = direction == "desc" ? SortDirection.Desc : SortDirection.Asc,
            Seed = seed,
        };

        var (items, totalCount) = await tagRepo.FindAsync(filter, findFilter, ct);
    var segmentCountsByTagId = await LoadSceneSegmentCountsAsync(items.Select(tag => tag.Id), ct);
    var dtos = MapTagListDtos(items, segmentCountsByTagId);
        return Ok(new PaginatedResponse<TagListDto>(dtos, totalCount, page, perPage));
    }

    [HttpPost("find")]
    public async Task<ActionResult<PaginatedResponse<TagListDto>>> FindPost([FromBody] FilteredQueryRequest<TagFilter> req, CancellationToken ct)
    {
        var findFilter = req.FindFilter ?? new FindFilter();
        var filter = req.ObjectFilter ?? new TagFilter();
        var (items, totalCount) = await tagRepo.FindAsync(filter, findFilter, ct);
        var segmentCountsByTagId = await LoadSceneSegmentCountsAsync(items.Select(tag => tag.Id), ct);
        var dtos = MapTagListDtos(items, segmentCountsByTagId);
        return Ok(new PaginatedResponse<TagListDto>(dtos, totalCount, findFilter.Page, findFilter.PerPage));
    }

    [HttpPost("graph")]
    public async Task<ActionResult<TagGraphResponseDto>> Graph([FromBody] FilteredQueryRequest<TagFilter> req, CancellationToken ct)
    {
        const int graphNodeLimit = 5000;

        var requestFindFilter = req.FindFilter ?? new FindFilter();
        var graphFindFilter = new FindFilter
        {
            Q = requestFindFilter.Q,
            Sort = requestFindFilter.Sort,
            Direction = requestFindFilter.Direction,
            Seed = requestFindFilter.Seed,
            Page = 1,
            PerPage = Math.Clamp(requestFindFilter.PerPage > 0 ? requestFindFilter.PerPage : graphNodeLimit, 1, graphNodeLimit),
        };

        var filter = req.ObjectFilter ?? new TagFilter();
        var (items, totalCount) = await tagRepo.FindAsync(filter, graphFindFilter, ct);
        if (items.Count == 0)
            return Ok(new TagGraphResponseDto([], [], totalCount));

        var ids = items.Select(tag => tag.Id).ToList();
        var parentIdsByTagId = ids.ToDictionary(id => id, _ => new List<int>());
        var childIdsByTagId = ids.ToDictionary(id => id, _ => new List<int>());
        var relations = await db.Set<TagParent>()
            .AsNoTracking()
            .Where(relation => ids.Contains(relation.ParentId) && ids.Contains(relation.ChildId))
            .Select(relation => new GraphRelation(relation.ParentId, relation.ChildId))
            .ToListAsync(ct);

        foreach (var relation in relations)
        {
            childIdsByTagId[relation.ParentId].Add(relation.ChildId);
            parentIdsByTagId[relation.ChildId].Add(relation.ParentId);
        }

        var segmentCountsByTagId = await LoadSceneSegmentCountsAsync(ids, ct);
        var graphItems = items
            .Select(tag =>
            {
                var usageCounts = new TagUsageCounts(
                    tag.SceneCount,
                    segmentCountsByTagId.GetValueOrDefault(tag.Id),
                    tag.ImageCount,
                    tag.GalleryCount,
                    tag.GroupCount,
                    tag.PerformerCount,
                    tag.StudioCount);

                return new TagGraphNodeDto(
                    tag.Id,
                    tag.Name,
                    tag.Favorite,
                    tag.Description,
                    tag.ImageBlobId != null ? EntityImageUrls.Tag(ControllerContext.HttpContext, tag.Id, tag.UpdatedAt) : null,
                    parentIdsByTagId[tag.Id],
                    childIdsByTagId[tag.Id],
                    usageCounts.TotalUsageCount,
                    usageCounts.SceneCount,
                    usageCounts.SegmentCount,
                    usageCounts.ImageCount,
                    usageCounts.GalleryCount,
                    usageCounts.GroupCount,
                    usageCounts.PerformerCount,
                    usageCounts.StudioCount);
            })
            .ToList();

        var graphLinks = relations
            .Select(relation => new TagGraphLinkDto(relation.ParentId, relation.ChildId))
            .ToList();

        return Ok(new TagGraphResponseDto(graphItems, graphLinks, totalCount));
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<TagDetailDto>> GetById(int id, CancellationToken ct)
    {
        var tag = await db.Tags
            .AsNoTracking()
            .Include(t => t.Aliases)
            .Include(t => t.TagGroup)
            .Include(t => t.ParentRelations).ThenInclude(tp => tp.Parent).ThenInclude(parent => parent!.TagGroup)
            .Include(t => t.ChildRelations).ThenInclude(tp => tp.Child).ThenInclude(child => child!.TagGroup)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag == null) return NotFound();

        return Ok(await MapToDetailDtoAsync(tag, ct));
    }

    [HttpGet("{id:int}/segments")]
    public async Task<ActionResult<IReadOnlyList<TagSegmentWallDto>>> GetSegments(int id, [FromQuery] int count = 100, CancellationToken ct = default)
    {
        var exists = await db.Tags.AsNoTracking().AnyAsync(tag => tag.Id == id, ct);
        if (!exists)
            return NotFound();

        count = Math.Clamp(count, 1, 250);

        var segments = await (
            from segment in db.Segments.AsNoTracking()
            join scene in db.Scenes.AsNoTracking() on segment.HostId equals scene.Id
            where segment.HostType == SegmentHostType.Scene && segment.TagId == id
            orderby segment.UpdatedAt descending, segment.Id descending
            select new TagSegmentWallDto(
                segment.Id,
                segment.Title,
                segment.StartSec,
                segment.EndSec,
                segment.Kind ?? "segment",
                segment.SourceKey,
                segment.Confidence,
                scene.Id,
                scene.Title ?? $"Scene #{scene.Id}")
        )
            .Take(count)
            .ToListAsync(ct);

        return Ok(segments);
    }

    [HttpPost]
    [RequiresPermission(Permissions.TagsWrite)]
    public async Task<ActionResult<TagDetailDto>> Create([FromBody] TagCreateDto dto, CancellationToken ct)
    {
        var existing = await tagRepo.GetByNameAsync(dto.Name, ct);
        if (existing != null) return Conflict(new { message = $"Tag '{dto.Name}' already exists" });

        var validation = await ValidateTagMetadataAsync(dto.Color, dto.TagGroupId, ct);
        if (validation != null) return validation;

        var tag = new Tag
        {
            Name = dto.Name, SortName = dto.SortName, Description = dto.Description,
            Color = NormalizeOptionalText(dto.Color),
            TagGroupId = NormalizeOptionalId(dto.TagGroupId),
            Favorite = dto.Favorite,
            IgnoreAutoTag = dto.IgnoreAutoTag,
            MinOccurrenceSec = NormalizeOptionalPositive(dto.MinOccurrenceSec),
            MinOccurrencePercent = NormalizeOptionalPercent(dto.MinOccurrencePercent),
            ShowAsSegment = dto.ShowAsSegment,
            SegmentColorOverride = NormalizeOptionalText(dto.SegmentColorOverride),
            SegmentLaneOverride = dto.SegmentLaneOverride,
        };
        if (dto.Aliases?.Count > 0) tag.Aliases = dto.Aliases.Select(a => new TagAlias { Alias = a }).ToList();
        if (dto.ParentIds?.Count > 0) tag.ParentRelations = dto.ParentIds.Select(pid => new TagParent { ParentId = pid }).ToList();
        if (dto.ChildIds?.Count > 0) tag.ChildRelations = dto.ChildIds.Select(cid => new TagParent { ChildId = cid }).ToList();

        tag = await tagRepo.AddAsync(tag, ct);
        if (dto.CustomFields != null)
            await customFields.SaveValuesAsync(CustomFieldEntityTypes.Tag, tag.Id, dto.CustomFields, ct);
        if (dto.Aliases?.Count > 0)
            await entityIdentifiers.SyncAsync(EntityKinds.Tag, tag.Id, IdentifierSchemes.Alias, dto.Aliases, null, ct);
        var result = await tagRepo.GetByIdWithRelationsAsync(tag.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = tag.Id }, await MapToDetailDtoAsync(result!, ct));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.TagsWrite)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsWrite)]
    public async Task<ActionResult<TagDetailDto>> Update(int id, [FromBody] TagUpdateDto dto, CancellationToken ct)
    {
        var tag = tagRepo != null
            ? await tagRepo.GetByIdWithRelationsAsync(id, ct)
            : await db.Tags
                .Include(t => t.Aliases)
                .Include(t => t.TagGroup)
                .Include(t => t.ParentRelations)
                .Include(t => t.ChildRelations)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag == null) return NotFound();

            var validation = await ValidateTagMetadataAsync(dto.Color, dto.TagGroupId, ct);
            if (validation != null) return validation;

        if (dto.Name != null) tag.Name = dto.Name;
        if (dto.SortName != null) tag.SortName = dto.SortName;
        if (dto.Description != null) tag.Description = dto.Description;
        tag.Color = NormalizeOptionalText(dto.Color);
        tag.TagGroupId = NormalizeOptionalId(dto.TagGroupId);
        if (dto.Favorite.HasValue) tag.Favorite = dto.Favorite.Value;
        if (dto.IgnoreAutoTag.HasValue) tag.IgnoreAutoTag = dto.IgnoreAutoTag.Value;
        tag.MinOccurrenceSec = NormalizeOptionalPositive(dto.MinOccurrenceSec);
        tag.MinOccurrencePercent = NormalizeOptionalPercent(dto.MinOccurrencePercent);
        tag.ShowAsSegment = dto.ShowAsSegment;
        tag.SegmentColorOverride = NormalizeOptionalText(dto.SegmentColorOverride);
        tag.SegmentLaneOverride = dto.SegmentLaneOverride;

        if (dto.Aliases != null)
        {
            tag.Aliases.Clear();
            tag.Aliases = dto.Aliases.Select(a => new TagAlias { Alias = a, TagId = id }).ToList();
        }
        if (dto.ParentIds != null)
        {
            tag.ParentRelations.Clear();
            tag.ParentRelations = dto.ParentIds.Select(pid => new TagParent { ParentId = pid, ChildId = id }).ToList();
        }
        if (dto.ChildIds != null)
        {
            tag.ChildRelations.Clear();
            tag.ChildRelations = dto.ChildIds.Select(cid => new TagParent { ParentId = id, ChildId = cid }).ToList();
        }
        if (tagRepo != null)
        {
            await tagRepo.UpdateAsync(tag, ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }
        if (dto.CustomFields != null)
            await customFields.SaveValuesAsync(CustomFieldEntityTypes.Tag, id, dto.CustomFields, ct);
        if (dto.Aliases != null)
            await entityIdentifiers.SyncAsync(EntityKinds.Tag, id, IdentifierSchemes.Alias, dto.Aliases, null, ct);
        var updated = tagRepo != null
            ? await tagRepo.GetByIdWithRelationsAsync(id, ct)
            : await db.Tags
                .AsNoTracking()
                .Include(t => t.Aliases)
                .Include(t => t.TagGroup)
                .Include(t => t.ParentRelations).ThenInclude(tp => tp.Parent).ThenInclude(parent => parent!.TagGroup)
                .Include(t => t.ChildRelations).ThenInclude(tp => tp.Child).ThenInclude(child => child!.TagGroup)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
        return Ok(await MapToDetailDtoAsync(updated!, ct));
    }

    [HttpGet("{id:int}/metadata-server/search")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<IReadOnlyList<MetadataServerTagMatchDto>>> SearchMetadataServer(int id, [FromServices] MetadataServerService metadataServerService, [FromQuery] string? term, [FromQuery] string? endpoint, CancellationToken ct)
    {
        var tag = await tagRepo.GetByIdWithRelationsAsync(id, ct);
        if (tag == null)
            return NotFound();

        await db.Entry(tag).Collection(t => t.RemoteIds).LoadAsync(ct);

        if (string.IsNullOrWhiteSpace(term))
        {
            var existingRemoteId = tag.RemoteIds.FirstOrDefault(remoteId => string.IsNullOrWhiteSpace(endpoint) || string.Equals(remoteId.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
            if (existingRemoteId != null)
            {
                var existing = await metadataServerService.GetTagMatchAsync(existingRemoteId.Endpoint, existingRemoteId.RemoteId, ct);
                if (existing != null)
                    return Ok(new[] { existing });
            }

            term = tag.Name;
        }

        return Ok(await metadataServerService.SearchTagsAsync(term, endpoint, ct));
    }

    [HttpPost("metadata-server/find-by-ids")]
    public async Task<ActionResult<IReadOnlyList<MetadataServerTagMatchDto>>> FindMetadataServerTagsByIds([FromServices] MetadataServerService metadataServerService, [FromBody] MetadataServerFindByIdsRequestDto dto, CancellationToken ct)
    {
        if (dto.Ids.Count == 0)
            return Ok(Array.Empty<MetadataServerTagMatchDto>());

        var results = new List<MetadataServerTagMatchDto>();
        foreach (var tagId in dto.Ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var match = await metadataServerService.GetTagMatchAsync(dto.Endpoint, tagId, ct);
            if (match != null)
                results.Add(match);
        }

        return Ok(results);
    }

    [HttpPost("{id:int}/metadata-server/import")]
    [RequiresPermission(Permissions.TagsWrite)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsWrite)]
    public async Task<ActionResult<TagDetailDto>> ImportFromMetadataServer(int id, [FromServices] MetadataServerService metadataServerService, [FromBody] MetadataServerTagImportRequestDto dto, CancellationToken ct)
    {
        var tag = await tagRepo.GetByIdWithRelationsAsync(id, ct);
        if (tag == null)
            return NotFound();

        await db.Entry(tag).Collection(t => t.RemoteIds).LoadAsync(ct);

        var imported = await metadataServerService.MergeTagAsync(tag, dto.Endpoint, dto.TagId, ct);
        if (!imported)
            return NotFound();

        await tagRepo.UpdateAsync(tag, ct);
        var updated = await tagRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(await MapToDetailDtoAsync(updated!, ct));
    }

    [HttpPost("{id:int}/metadata-server/submit-draft")]
    [RequiresPermission(Permissions.TagsWrite)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsWrite)]
    public async Task<IActionResult> SubmitTagDraft(int id, [FromServices] MetadataServerService metadataServerService, [FromBody] MetadataServerEndpointDto dto, CancellationToken ct)
    {
        var tag = await tagRepo.GetByIdWithRelationsAsync(id, ct);
        if (tag == null)
            return NotFound();

        var draftId = await metadataServerService.SubmitTagDraftAsync(tag, dto.Endpoint, ct);
        return Ok(new { draftId });
    }

    [HttpPost("metadata-server/batch-tag")]
    [RequiresPermission(Permissions.TagsWrite)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsWrite, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<ActionResult<object>> BatchTagFromMetadataServer([FromBody] MetadataServerTagBatchTagRequestDto dto, [FromServices] IJobService jobService, [FromServices] IServiceScopeFactory scopeFactory, [FromServices] IAuthorizationService authorizationService, [FromServices] ICurrentPrincipalAccessor principalAccessor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Endpoint))
            return BadRequest(new { message = "Endpoint is required" });

        var ids = await ResolveSelectedTagIdsAsync(dto, ct);
        if (ids.Count == 0)
            return BadRequest(new { message = "No tags selected for batch tagging" });

        var principal = principalAccessor.Current;
        if (principal == null)
            return Forbid();

        foreach (var id in ids)
        {
            var result = await authorizationService.AuthorizeAsync(
                principal,
                Permissions.TagsWrite,
                new EntityRef(EntityKinds.Tag, id.ToString()),
                ct);

            if (!result.Allowed)
                return Forbid();
        }

        var jobId = jobService.Enqueue(
            "metadata-server:tags",
            $"Tagging {ids.Count} tags from {dto.Endpoint}",
            async (progress, jobCt) =>
            {
                using var scope = scopeFactory.CreateScope();
                var metadataServerService = scope.ServiceProvider.GetRequiredService<MetadataServerService>();
                await metadataServerService.BatchTagTagsAsync(dto.Endpoint, ids, dto.RefreshAlreadyTagged, dto.ExcludeFields, progress, jobCt);
            });

        return Ok(new { jobId, itemCount = ids.Count });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.TagsDelete)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var tag = await tagRepo.GetByIdAsync(id, ct);
        if (tag == null) return NotFound();
        await customFields.DeleteValuesForEntityAsync(CustomFieldEntityTypes.Tag, id, ct);
        await tagRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    private async Task<List<int>> ResolveSelectedTagIdsAsync(MetadataServerTagBatchTagRequestDto dto, CancellationToken ct)
    {
        if (dto.Ids?.Count > 0)
            return dto.Ids.Distinct().ToList();

        if (!dto.SelectAll && dto.Filter == null)
            return [];

        const int pageSize = 500;
        var ids = new List<int>();
        var page = 1;

        while (true)
        {
            var (items, totalCount) = await tagRepo.FindAsync(dto.Filter, new FindFilter
            {
                Page = page,
                PerPage = pageSize,
                Sort = "id",
                Direction = SortDirection.Asc,
            }, ct);

            if (items.Count == 0)
                break;

            ids.AddRange(items.Select(item => item.Id));
            if (ids.Count >= totalCount)
                break;

            page++;
        }

        return ids.Distinct().ToList();
    }

    private async Task<TagDetailDto> MapToDetailDtoAsync(Tag t, CancellationToken ct)
    {
        var segmentCount = await db.Segments
            .AsNoTracking()
            .CountAsync(segment => segment.HostType == SegmentHostType.Scene && segment.TagId == t.Id, ct);

        return new TagDetailDto(
            t.Id,
            t.Name,
            t.SortName,
            t.Description,
            t.Favorite,
            t.IgnoreAutoTag,
            t.Aliases.Select(a => a.Alias).ToList(),
            t.ParentRelations.Where(pr => pr.Parent != null).Select(pr => MapTagDto(pr.Parent!)).ToList(),
            t.ChildRelations.Where(cr => cr.Child != null).Select(cr => MapTagDto(cr.Child!)).ToList(),
            t.SceneCount,
            t.PerformerCount,
            t.ImageCount,
            t.GalleryCount,
            t.StudioCount,
            t.GroupCount,
            segmentCount,
            await customFields.GetValuesAsync(CustomFieldEntityTypes.Tag, t.Id, ct),
            t.CreatedAt.ToString("o"),
            t.UpdatedAt.ToString("o"),
            t.ShowAsSegment,
            t.SegmentColorOverride,
                t.SegmentLaneOverride,
                t.Color,
                t.TagGroupId,
                t.TagGroup?.Name,
                t.TagGroup?.Color,
                t.MinOccurrenceSec,
                t.MinOccurrencePercent);
    }

    private List<TagListDto> MapTagListDtos(IReadOnlyList<Tag> items, IReadOnlyDictionary<int, int> segmentCountsByTagId)
    {
        if (items.Count == 0) return [];

        return items.Select(t =>
        {
            return new TagListDto(
                t.Id,
                t.Name,
                t.Description,
                t.Favorite,
                t.IgnoreAutoTag,
                t.Aliases.Select(a => a.Alias).ToList(),
                t.SceneCount,
                segmentCountsByTagId.GetValueOrDefault(t.Id),
                t.ImageCount,
                t.GalleryCount,
                t.GroupCount,
                t.PerformerCount,
                t.StudioCount,
                t.ImageBlobId != null ? EntityImageUrls.Tag(ControllerContext.HttpContext, t.Id, t.UpdatedAt) : null,
                t.ShowAsSegment,
                t.SegmentColorOverride,
                t.SegmentLaneOverride,
                t.Color,
                t.TagGroupId,
                t.TagGroup?.Name,
                t.TagGroup?.Color,
                t.MinOccurrenceSec,
                t.MinOccurrencePercent);
        }).ToList();
    }

    private static TagDto MapTagDto(Tag tag, List<TagProvenanceDto>? provenance = null)
        => new(
            tag.Id,
            tag.Name,
            tag.Description,
            tag.Favorite,
            tag.IgnoreAutoTag,
            tag.Aliases.Select(alias => alias.Alias).ToList(),
            tag.ShowAsSegment,
            tag.SegmentColorOverride,
            tag.SegmentLaneOverride,
            provenance,
            tag.Color,
            tag.TagGroupId,
            tag.TagGroup?.Name,
            tag.TagGroup?.Color,
            tag.MinOccurrenceSec,
            tag.MinOccurrencePercent);

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? NormalizeOptionalId(int? value)
        => value is > 0 ? value : null;

    private static double? NormalizeOptionalPositive(double? value)
        => value is > 0 ? value : null;

    private static double? NormalizeOptionalPercent(double? value)
        => value is > 0 ? Math.Min(value.Value, 100d) : null;

    private async Task<ActionResult<TagDetailDto>?> ValidateTagMetadataAsync(string? color, int? tagGroupId, CancellationToken ct)
    {
        var normalizedColor = NormalizeOptionalText(color);
        if (normalizedColor != null && !IsHexColor(normalizedColor))
            return BadRequest(new { message = "Color must be #RRGGBB or #RRGGBBAA." });

        var normalizedGroupId = NormalizeOptionalId(tagGroupId);
        if (normalizedGroupId.HasValue && !await db.TagGroups.AsNoTracking().AnyAsync(group => group.Id == normalizedGroupId.Value, ct))
            return BadRequest(new { message = "Tag group does not exist." });

        return null;
    }

    private static bool IsHexColor(string value)
    {
        if (value.Length is not (7 or 9) || value[0] != '#')
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }

        return true;
    }

    private async Task<Dictionary<int, int>> LoadSceneSegmentCountsAsync(IEnumerable<int> tagIds, CancellationToken ct)
    {
        var ids = tagIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Segments
            .AsNoTracking()
            .Where(segment => segment.HostType == SegmentHostType.Scene && segment.TagId.HasValue && ids.Contains(segment.TagId.Value))
            .GroupBy(segment => segment.TagId!.Value)
            .Select(group => new { TagId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.TagId, item => item.Count, ct);
    }

    // ===== Bulk Operations =====

    [HttpPost("bulk")]
    [RequiresPermission(Permissions.TagsWrite)]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkTagUpdateDto dto, CancellationToken ct)
    {
        var tags = await db.Tags
            .Include(t => t.ParentRelations)
            .Include(t => t.ChildRelations)
            .AsSplitQuery()
            .Where(t => dto.Ids.Contains(t.Id))
            .ToListAsync(ct);

        foreach (var tag in tags)
        {
            if (dto.Description != null) tag.Description = dto.Description;
            if (dto.Color != null) tag.Color = string.IsNullOrWhiteSpace(dto.Color) ? null : dto.Color.Trim();
            if (dto.TagGroupId.HasValue) tag.TagGroupId = dto.TagGroupId;
            if (dto.MinOccurrenceSec.HasValue) tag.MinOccurrenceSec = dto.MinOccurrenceSec;
            if (dto.MinOccurrencePercent.HasValue) tag.MinOccurrencePercent = dto.MinOccurrencePercent;
            if (dto.Favorite.HasValue) tag.Favorite = dto.Favorite.Value;
            if (dto.IgnoreAutoTag.HasValue) tag.IgnoreAutoTag = dto.IgnoreAutoTag.Value;

            var parentIds = dto.ParentIds?
                .Where(parentId => parentId != tag.Id)
                .Distinct()
                .ToList();
            if (parentIds != null && dto.ParentMode == BulkUpdateMode.Set)
            {
                tag.ParentRelations.Clear();
                tag.ParentRelations = parentIds
                    .Select(parentId => new TagParent { ParentId = parentId, ChildId = tag.Id })
                    .ToList();
            }
            else if (parentIds != null && dto.ParentMode == BulkUpdateMode.Add)
            {
                var existingParentIds = tag.ParentRelations.Select(relation => relation.ParentId).ToHashSet();
                foreach (var parentId in parentIds.Where(parentId => !existingParentIds.Contains(parentId)))
                    tag.ParentRelations.Add(new TagParent { ParentId = parentId, ChildId = tag.Id });
            }
            else if (parentIds != null && dto.ParentMode == BulkUpdateMode.Remove)
            {
                tag.ParentRelations = tag.ParentRelations
                    .Where(relation => !parentIds.Contains(relation.ParentId))
                    .ToList();
            }

            var childIds = dto.ChildIds?
                .Where(childId => childId != tag.Id)
                .Distinct()
                .ToList();
            if (childIds != null && dto.ChildMode == BulkUpdateMode.Set)
            {
                tag.ChildRelations.Clear();
                tag.ChildRelations = childIds
                    .Select(childId => new TagParent { ParentId = tag.Id, ChildId = childId })
                    .ToList();
            }
            else if (childIds != null && dto.ChildMode == BulkUpdateMode.Add)
            {
                var existingChildIds = tag.ChildRelations.Select(relation => relation.ChildId).ToHashSet();
                foreach (var childId in childIds.Where(childId => !existingChildIds.Contains(childId)))
                    tag.ChildRelations.Add(new TagParent { ParentId = tag.Id, ChildId = childId });
            }
            else if (childIds != null && dto.ChildMode == BulkUpdateMode.Remove)
            {
                tag.ChildRelations = tag.ChildRelations
                    .Where(relation => !childIds.Contains(relation.ChildId))
                    .ToList();
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { updated = tags.Count });
    }

    [HttpDelete("bulk")]
    [RequiresPermission(Permissions.TagsDelete)]
    public async Task<IActionResult> BulkDelete([FromBody] BatchDeleteDto dto, CancellationToken ct)
    {
        var tags = await db.Tags.Where(t => dto.Ids.Contains(t.Id)).ToListAsync(ct);
        if (tags.Count == 0)
            return Ok(new { deleted = 0 });

        db.Tags.RemoveRange(tags);
        foreach (var tag in tags)
            await customFields.DeleteValuesForEntityAsync(CustomFieldEntityTypes.Tag, tag.Id, ct);
        await db.SaveChangesAsync(ct);
        return Ok(new { deleted = tags.Count });
    }

    // ===== Merge =====

    [HttpPost("merge")]
    [RequiresPermission(Permissions.TagsWrite)]
    public async Task<ActionResult<TagDetailDto>> MergeTags([FromBody] TagMergeDto dto, CancellationToken ct)
    {
        var target = await tagRepo.GetByIdWithRelationsAsync(dto.TargetId, ct);
        if (target == null) return NotFound("Target tag not found");

        var sources = await db.Tags
            .Include(t => t.Aliases)
            .Include(t => t.SceneTags)
            .Include(t => t.PerformerTags)
            .Include(t => t.ImageTags)
            .Include(t => t.GalleryTags)
            .AsSplitQuery()
            .Where(t => dto.SourceIds.Contains(t.Id))
            .ToListAsync(ct);

        foreach (var source in sources)
        {
            // Move scene associations
            foreach (var st in source.SceneTags)
                if (!target.SceneTags.Any(t => t.SceneId == st.SceneId))
                    db.Set<SceneTag>().Add(new SceneTag { SceneId = st.SceneId, TagId = target.Id });
            // Move performer associations
            foreach (var pt in source.PerformerTags)
                if (!target.PerformerTags.Any(t => t.PerformerId == pt.PerformerId))
                    db.Set<PerformerTag>().Add(new PerformerTag { PerformerId = pt.PerformerId, TagId = target.Id });
            // Move image associations
            foreach (var it in source.ImageTags)
                if (!target.ImageTags.Any(t => t.ImageId == it.ImageId))
                    db.Set<ImageTag>().Add(new ImageTag { ImageId = it.ImageId, TagId = target.Id });
            // Add source name as alias
            if (!target.Aliases.Any(a => a.Alias == source.Name))
                target.Aliases.Add(new TagAlias { Alias = source.Name, TagId = target.Id });
            // Delete source
            await customFields.DeleteValuesForEntityAsync(CustomFieldEntityTypes.Tag, source.Id, ct);
            db.Tags.Remove(source);
        }

        await db.SaveChangesAsync(ct);
        var result = await tagRepo.GetByIdWithRelationsAsync(target.Id, ct);
        return Ok(await MapToDetailDtoAsync(result!, ct));
    }

    // ===== Marker Wall =====

    [HttpGet("marker-strings")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<List<string>>> GetMarkerStrings([FromQuery] string? q, [FromQuery] string? sort, CancellationToken ct)
    {
        var query = db.Segments
            .AsNoTracking()
            .Where(segment => segment.HostType == SegmentHostType.Scene && segment.Title != null && segment.Title != string.Empty)
            .Select(segment => segment.Title!)
            .Distinct();
        if (!string.IsNullOrEmpty(q))
        {
            if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            {
                query = query.Where(title => EF.Functions.ILike(title, $"%{q}%"));
            }
            else
            {
                var normalizedQuery = q.ToUpperInvariant();
                query = query.Where(title => title.ToUpper().Contains(normalizedQuery));
            }
        }

        var result = sort == "count"
            ? await db.Segments
                .AsNoTracking()
                .Where(segment => segment.HostType == SegmentHostType.Scene && segment.Title != null && segment.Title != string.Empty)
                .GroupBy(segment => segment.Title!)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .Take(100)
                .ToListAsync(ct)
            : await query.OrderBy(t => t).Take(100).ToListAsync(ct);

        return Ok(result);
    }
}
