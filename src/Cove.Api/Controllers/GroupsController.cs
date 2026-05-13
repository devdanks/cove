using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.Common;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.GroupsRead)]
public class GroupsController(IGroupRepository groupRepo, Data.CoveContext db, IUserEngagementService engagementService, CustomFieldService customFields, DynamicGroupResolver dynamicGroups) : ControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<PaginatedResponse<GroupDto>>> Find(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int perPage = 25,
        [FromQuery] string? sort = null, [FromQuery] string? direction = null,
        [FromQuery] int? seed = null,
        [FromQuery] string? name = null, [FromQuery] int? rating = null,
        [FromQuery] int? studioId = null, [FromQuery] string? tagIds = null,
        CancellationToken ct = default)
    {
        await dynamicGroups.EnsureBuiltInGroupsAsync(ct);
        var filter = new GroupFilter { Name = name, Rating = rating, StudioId = studioId, TagIds = QueryParsing.ParseIntList(tagIds)?.ToList() };
        var findFilter = new FindFilter
        {
            Q = q, Page = page, PerPage = perPage, Sort = sort,
            Direction = direction == "desc" ? SortDirection.Desc : SortDirection.Asc,
            Seed = seed,
        };

        var (items, totalCount) = await groupRepo.FindAsync(filter, findFilter, ct);
        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, items.Select(group => group.Id), ct);
        var dtos = items.Select(group => MapToDto(group, GetCustomFields(customFieldValues, group.Id))).ToList();
        return Ok(new PaginatedResponse<GroupDto>(dtos, totalCount, page, perPage));
    }

    [HttpPost("find")]
    public async Task<ActionResult<PaginatedResponse<GroupDto>>> FindPost([FromBody] FilteredQueryRequest<GroupFilter> req, CancellationToken ct)
    {
        await dynamicGroups.EnsureBuiltInGroupsAsync(ct);
        var findFilter = req.FindFilter ?? new FindFilter();
        var filter = req.ObjectFilter ?? new GroupFilter();
        var (items, totalCount) = await groupRepo.FindAsync(filter, findFilter, ct);
        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, items.Select(group => group.Id), ct);
        var dtos = items.Select(group => MapToDto(group, GetCustomFields(customFieldValues, group.Id))).ToList();
        return Ok(new PaginatedResponse<GroupDto>(dtos, totalCount, findFilter.Page, findFilter.PerPage));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GroupDto>> GetById(int id, CancellationToken ct)
    {
        await dynamicGroups.EnsureBuiltInGroupsAsync(ct);
        var group = await groupRepo.GetByIdWithRelationsAsync(id, ct);
        if (group == null) return NotFound();
        return Ok(MapToDto(group, await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, id, ct)));
    }

    [HttpPost]
    [RequiresPermission(Permissions.GroupsWrite)]
    public async Task<ActionResult<GroupDto>> Create([FromBody] GroupCreateDto dto, CancellationToken ct)
    {
        var nextSortOrder = dto.SortOrder ?? ((await db.Groups.MaxAsync(group => (int?)group.SortOrder, ct)) ?? -1) + 1;
        var group = new Group
        {
            Name = dto.Name, Aliases = dto.Aliases, Duration = dto.Duration,
            Date = ParseDate(dto.Date), StudioId = dto.StudioId,
            Director = dto.Director, Synopsis = dto.Synopsis,
            Kind = dto.Kind,
            QuerySourceKey = NormalizeOptionalText(dto.QuerySourceKey),
            QueryJson = NormalizeOptionalText(dto.QueryJson),
            CacheTtlSec = dto.CacheTtlSec ?? 60,
            ShowInSceneLists = dto.ShowInSceneLists ?? false,
            SortOrder = nextSortOrder,
            AllowedHostTypes = NormalizeAllowedHostTypes(dto.AllowedHostTypes),
        };
        if (dto.Urls?.Count > 0) group.Urls = dto.Urls.Select(u => new GroupUrl { Url = u }).ToList();
        if (dto.TagIds?.Count > 0) group.GroupTags = dto.TagIds.Select(id => new GroupTag { TagId = id }).ToList();

        group = await groupRepo.AddAsync(group, ct);
        if (dto.CustomFields != null)
            await customFields.SaveValuesAsync(CustomFieldEntityTypes.Group, group.Id, dto.CustomFields, ct);
        if (dto.Rating.HasValue)
            await engagementService.SetRatingAsync(AffinityHostType.Group, group.Id, dto.Rating, cancellationToken: ct);
        var result = await groupRepo.GetByIdWithRelationsAsync(group.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = group.Id }, MapToDto(result!, await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, group.Id, ct)));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<ActionResult<GroupDto>> Update(int id, [FromBody] GroupUpdateDto dto, CancellationToken ct)
    {
        var group = await groupRepo.GetByIdWithRelationsAsync(id, ct);
        if (group == null) return NotFound();

        if (dto.Name != null) group.Name = dto.Name;
        if (dto.Aliases != null) group.Aliases = dto.Aliases;
        if (dto.Duration.HasValue) group.Duration = dto.Duration;
        if (dto.Date != null) group.Date = ParseDate(dto.Date);
        if (dto.StudioId.HasValue) group.StudioId = dto.StudioId;
        if (dto.Director != null) group.Director = dto.Director;
        if (dto.Synopsis != null) group.Synopsis = dto.Synopsis;
        if (dto.Kind.HasValue) group.Kind = dto.Kind.Value;
        if (dto.QuerySourceKey != null) group.QuerySourceKey = NormalizeOptionalText(dto.QuerySourceKey);
        if (dto.QueryJson != null) group.QueryJson = NormalizeOptionalText(dto.QueryJson);
        if (dto.CacheTtlSec.HasValue) group.CacheTtlSec = Math.Max(0, dto.CacheTtlSec.Value);
        if (dto.ShowInSceneLists.HasValue) group.ShowInSceneLists = dto.ShowInSceneLists.Value;
        if (dto.SortOrder.HasValue) group.SortOrder = dto.SortOrder.Value;
        if (dto.AllowedHostTypes != null) group.AllowedHostTypes = NormalizeAllowedHostTypes(dto.AllowedHostTypes);

        if (dto.Urls != null)
        {
            group.Urls.Clear();
            group.Urls = dto.Urls.Select(u => new GroupUrl { Url = u, GroupId = id }).ToList();
        }
        if (dto.TagIds != null)
        {
            group.GroupTags.Clear();
            group.GroupTags = dto.TagIds.Select(tid => new GroupTag { TagId = tid, GroupId = id }).ToList();
        }
        await groupRepo.UpdateAsync(group, ct);
        if (dto.CustomFields != null)
            await customFields.SaveValuesAsync(CustomFieldEntityTypes.Group, id, dto.CustomFields, ct);
        if (dto.Rating.HasValue)
            await engagementService.SetRatingAsync(AffinityHostType.Group, id, dto.Rating, cancellationToken: ct);
        var updated = await groupRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(MapToDto(updated!, await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, id, ct)));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.GroupsDelete)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var g = await groupRepo.GetByIdAsync(id, ct);
        if (g == null) return NotFound();
        await customFields.DeleteValuesForEntityAsync(CustomFieldEntityTypes.Group, id, ct);
        await groupRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    // ===== Bulk Update =====

    [HttpPost("bulk")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkGroupUpdateDto dto, CancellationToken ct)
    {
        var groups = await db.Groups
            .Include(g => g.GroupTags)
            .Where(g => dto.Ids.Contains(g.Id))
            .ToListAsync(ct);
        var clearFields = dto.ClearFields?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (var g in groups)
        {
            if (clearFields.Contains("studioId")) g.StudioId = null;
            if (clearFields.Contains("date")) g.Date = null;
            if (clearFields.Contains("director")) g.Director = null;
            if (clearFields.Contains("synopsis")) g.Synopsis = null;
            if (dto.StudioId.HasValue) g.StudioId = dto.StudioId;
            if (dto.Date != null) g.Date = ParseDate(dto.Date);
            if (dto.Director != null) g.Director = dto.Director;
            if (dto.Synopsis != null) g.Synopsis = dto.Synopsis;

            if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Set)
            {
                g.GroupTags.Clear();
                g.GroupTags = dto.TagIds.Select(tid => new GroupTag { TagId = tid, GroupId = g.Id }).ToList();
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Add)
            {
                var existing = g.GroupTags.Select(gt => gt.TagId).ToHashSet();
                foreach (var tid in dto.TagIds.Where(t => !existing.Contains(t)))
                    g.GroupTags.Add(new GroupTag { TagId = tid, GroupId = g.Id });
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Remove)
            {
                g.GroupTags = g.GroupTags.Where(gt => !dto.TagIds.Contains(gt.TagId)).ToList();
            }
        }

        await db.SaveChangesAsync(ct);
        if (dto.Rating.HasValue)
        {
            foreach (var group in groups)
                await engagementService.SetRatingAsync(AffinityHostType.Group, group.Id, dto.Rating, cancellationToken: ct);
        }
        return Ok(new { updated = groups.Count });
    }

    [HttpPut("reorder")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<IActionResult> Reorder([FromBody] GroupReorderDto dto, CancellationToken ct)
    {
        if (dto.Ids.Count == 0) return Ok();

        var groups = await db.Groups
            .Where(group => dto.Ids.Contains(group.Id))
            .ToListAsync(ct);
        if (groups.Count != dto.Ids.Distinct().Count()) return NotFound();

        for (var i = 0; i < dto.Ids.Count; i++)
        {
            var group = groups.First(item => item.Id == dto.Ids[i]);
            group.SortOrder = Math.Max(0, dto.StartIndex) + i;
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpGet("dynamic-sources")]
    public ActionResult<IReadOnlyList<DynamicGroupSourceDto>> GetDynamicSources()
        => Ok(dynamicGroups.GetSources());

    [HttpPut("{id:int}/query")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<ActionResult<GroupDto>> UpdateQuery(int id, [FromBody] GroupQueryUpdateDto dto, CancellationToken ct)
    {
        var group = await groupRepo.GetByIdWithRelationsAsync(id, ct);
        if (group == null) return NotFound();

        group.Kind = GroupKind.Dynamic;
        group.QuerySourceKey = NormalizeOptionalText(dto.QuerySourceKey);
        group.QueryJson = NormalizeOptionalText(dto.QueryJson);
        if (dto.CacheTtlSec.HasValue)
            group.CacheTtlSec = Math.Max(0, dto.CacheTtlSec.Value);
        group.LastResolvedAt = null;
        group.CachedItemCount = null;

        await groupRepo.UpdateAsync(group, ct);
        var updated = await groupRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(MapToDto(updated!, await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, id, ct)));
    }

    [HttpPost("{id:int}/snapshot")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<ActionResult<GroupDto>> SnapshotDynamicGroup(int id, CancellationToken ct)
    {
        if (!await db.Groups.AnyAsync(group => group.Id == id, ct))
            return NotFound();

        await dynamicGroups.SnapshotAsync(id, ct);
        var updated = await groupRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(MapToDto(updated!, await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, id, ct)));
    }

    [HttpGet("{id:int}/subgroups")]
    public async Task<ActionResult<List<GroupDto>>> GetSubGroups(int id, CancellationToken ct)
    {
        var relations = await db.Set<GroupRelation>()
            .Where(r => r.ContainingGroupId == id)
            .OrderBy(r => r.OrderIndex)
            .Include(r => r.SubGroup!).ThenInclude(g => g.Urls)
            .Include(r => r.SubGroup!).ThenInclude(g => g.GroupTags).ThenInclude(gt => gt.Tag)
            .Include(r => r.SubGroup!).ThenInclude(g => g.GroupItems)
            .ToListAsync(ct);
        var groups = relations.Where(r => r.SubGroup != null).Select(r => r.SubGroup!).ToList();
        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, groups.Select(group => group.Id), ct);
        return Ok(groups.Select(group => MapToDto(group, GetCustomFields(customFieldValues, group.Id))).ToList());
    }

    [HttpGet("{id:int}/containinggroups")]
    public async Task<ActionResult<List<GroupDto>>> GetContainingGroups(int id, CancellationToken ct)
    {
        var relations = await db.Set<GroupRelation>()
            .Where(r => r.SubGroupId == id)
            .OrderBy(r => r.OrderIndex)
            .Include(r => r.ContainingGroup!).ThenInclude(g => g.Urls)
            .Include(r => r.ContainingGroup!).ThenInclude(g => g.GroupTags).ThenInclude(gt => gt.Tag)
            .Include(r => r.ContainingGroup!).ThenInclude(g => g.GroupItems)
            .ToListAsync(ct);
        var groups = relations.Where(r => r.ContainingGroup != null).Select(r => r.ContainingGroup!).ToList();
        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Group, groups.Select(group => group.Id), ct);
        return Ok(groups.Select(group => MapToDto(group, GetCustomFields(customFieldValues, group.Id))).ToList());
    }

    [HttpPost("{id:int}/subgroups")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, ActionArgumentName = "dto", PropertyName = "SubGroupId")]
    public async Task<IActionResult> AddSubGroup(int id, [FromBody] AddSubGroupDto dto, CancellationToken ct)
    {
        var group = await groupRepo.GetByIdAsync(id, ct);
        if (group == null) return NotFound();
        if (dto.SubGroupId == id) return BadRequest("A group cannot be a sub-group of itself");
        if (!await db.Groups.AnyAsync(g => g.Id == dto.SubGroupId, ct)) return NotFound("Sub-group not found");

        var existing = await db.Set<GroupRelation>()
            .Where(r => r.ContainingGroupId == id)
            .ToListAsync(ct);

        if (existing.Any(r => r.SubGroupId == dto.SubGroupId))
            return Conflict("Sub-group already exists");

        var maxOrder = existing.Count > 0 ? existing.Max(r => r.OrderIndex) + 1 : 0;
        db.Set<GroupRelation>().Add(new GroupRelation
        {
            ContainingGroupId = id,
            SubGroupId = dto.SubGroupId,
            OrderIndex = dto.OrderIndex ?? maxOrder,
            Description = dto.Description,
        });
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpDelete("{id:int}/subgroups/{subGroupId:int}")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, RouteValueName = "subGroupId")]
    public async Task<IActionResult> RemoveSubGroup(int id, int subGroupId, CancellationToken ct)
    {
        var relation = await db.Set<GroupRelation>()
            .FirstOrDefaultAsync(r => r.ContainingGroupId == id && r.SubGroupId == subGroupId, ct);
        if (relation == null) return NotFound();
        db.Set<GroupRelation>().Remove(relation);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:int}/subgroups/reorder")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, ActionArgumentName = "dto", PropertyName = "SubGroupIds")]
    public async Task<IActionResult> ReorderSubGroups(int id, [FromBody] ReorderSubGroupsDto dto, CancellationToken ct)
    {
        var relations = await db.Set<GroupRelation>()
            .Where(r => r.ContainingGroupId == id)
            .ToListAsync(ct);

        for (var i = 0; i < dto.SubGroupIds.Count; i++)
        {
            var rel = relations.FirstOrDefault(r => r.SubGroupId == dto.SubGroupIds[i]);
            if (rel != null) rel.OrderIndex = i;
        }
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    private GroupDto MapToDto(Group g, Dictionary<string, object>? customFieldValues = null) => new(
        g.Id, g.Name, g.Aliases, g.Duration, g.Date?.ToString("yyyy-MM-dd"),
        g.StudioId, g.Studio?.Name, g.Director, g.Synopsis,
        g.Urls.Select(u => u.Url).ToList(),
        g.GroupTags.Where(gt => gt.Tag != null).Select(gt => TagDtoMapping.MapTagDto(gt.Tag!)).ToList(),
        g.Kind == GroupKind.Dynamic ? g.CachedItemCount ?? 0 : g.GroupItems.Where(item => item.SceneId.HasValue).Select(item => item.SceneId!.Value).Distinct().Count(),
        g.Kind == GroupKind.Dynamic ? g.CachedItemCount ?? 0 : g.GroupItems.Count,
        g.GroupItems.Any(item => item.Kind == GroupItemKind.SceneRange),
        g.SubGroupRelations?.Count ?? 0,
        g.ContainingGroupRelations?.Count ?? 0,
        customFieldValues,
        g.CreatedAt.ToString("o"), g.UpdatedAt.ToString("o"),
        g.FrontImageBlobId != null ? EntityImageUrls.GroupFront(ControllerContext.HttpContext, g.Id, g.UpdatedAt) : null,
        g.BackImageBlobId != null ? EntityImageUrls.GroupBack(ControllerContext.HttpContext, g.Id, g.UpdatedAt) : null,
        g.Kind,
        g.QuerySourceKey,
        g.QueryJson,
        g.LastResolvedAt?.ToString("o"),
        g.CachedItemCount,
        g.CacheTtlSec,
        g.ShowInSceneLists,
        g.AllowedHostTypes,
        g.SortOrder
    );

    private static Dictionary<string, object>? GetCustomFields(IReadOnlyDictionary<int, Dictionary<string, object>> lookup, int id)
        => lookup.TryGetValue(id, out var values) && values.Count > 0 ? values : null;

    private static DateOnly? ParseDate(string? date) => DateOnly.TryParse(date, out var d) ? d : null;

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeAllowedHostTypes(List<string>? values)
    {
        var normalized = values?
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized is { Count: > 0 } ? normalized : ["scene"];
    }
}
