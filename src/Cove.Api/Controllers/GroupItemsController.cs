using System.Text.Json;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:int}")]
[RequiresPermission(Permissions.GroupsRead)]
[RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsRead, RouteValueName = "groupId")]
public class GroupItemsController(CoveContext db, SegmentSpanResolver spanResolver, DynamicGroupResolver? dynamicGroups = null) : ControllerBase
{
    [HttpGet("items")]
    public async Task<ActionResult<IReadOnlyList<GroupItemDto>>> List(int groupId, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(item => item.Id == groupId, ct);
        if (group is null)
            return NotFound();

        if (group.Kind == GroupKind.Dynamic && dynamicGroups is not null)
            return Ok(await dynamicGroups.ResolveDtosAsync(groupId, forceRefresh: false, ct));

        var items = await db.GroupItems.AsNoTracking()
            .Include(item => item.Scene).ThenInclude(scene => scene!.Files)
            .Include(item => item.Image)
            .Include(item => item.ChildGroup)
            .Where(item => item.GroupId == groupId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        return Ok(items.Select(MapItem).ToList());
    }

    [HttpGet("items/page")]
    public async Task<ActionResult<PaginatedResponse<GroupItemDto>>> ListPage(
        int groupId,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 40,
        [FromQuery] string? sort = null,
        [FromQuery] string? direction = null,
        CancellationToken ct = default)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(item => item.Id == groupId, ct);
        if (group is null)
            return NotFound();

        var findFilter = new FindFilter
        {
            Page = page,
            PerPage = perPage,
            Sort = sort,
            Direction = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Desc : SortDirection.Asc,
        };

        if (group.Kind != GroupKind.Dynamic)
        {
            var staticQuery = db.GroupItems.AsNoTracking()
                .Include(item => item.Scene).ThenInclude(scene => scene!.Files)
                .Include(item => item.Image)
                .Include(item => item.ChildGroup)
                .Where(item => item.GroupId == groupId);

            if (!string.IsNullOrWhiteSpace(findFilter.Q))
            {
                var q = findFilter.Q.Trim();
                staticQuery = staticQuery.Where(item =>
                    (item.Title != null && EF.Functions.ILike(item.Title, $"%{q}%"))
                    || (item.Scene != null && item.Scene.Title != null && EF.Functions.ILike(item.Scene.Title, $"%{q}%"))
                    || (item.Image != null && item.Image.Title != null && EF.Functions.ILike(item.Image.Title, $"%{q}%"))
                    || (item.ChildGroup != null && EF.Functions.ILike(item.ChildGroup.Name, $"%{q}%")));
            }

            var totalCount = await staticQuery.CountAsync(ct);
            var desc = findFilter.Direction == SortDirection.Desc;
            staticQuery = (findFilter.Sort ?? "order") switch
            {
                "title" => desc
                    ? staticQuery.OrderByDescending(item => item.Title ?? item.Scene!.Title ?? item.Image!.Title ?? item.ChildGroup!.Name).ThenByDescending(item => item.Id)
                    : staticQuery.OrderBy(item => item.Title ?? item.Scene!.Title ?? item.Image!.Title ?? item.ChildGroup!.Name).ThenBy(item => item.Id),
                "kind" => desc
                    ? staticQuery.OrderByDescending(item => item.Kind).ThenByDescending(item => item.OrderIndex)
                    : staticQuery.OrderBy(item => item.Kind).ThenBy(item => item.OrderIndex),
                "created_at" => desc
                    ? staticQuery.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
                    : staticQuery.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
                _ => desc
                    ? staticQuery.OrderByDescending(item => item.OrderIndex).ThenByDescending(item => item.Id)
                    : staticQuery.OrderBy(item => item.OrderIndex).ThenBy(item => item.Id),
            };

            var safePage = Math.Max(1, findFilter.Page);
            var safePerPage = Math.Clamp(findFilter.PerPage, 1, 250);
            var items = await staticQuery
                .Skip((safePage - 1) * safePerPage)
                .Take(safePerPage)
                .ToListAsync(ct);

            return Ok(new PaginatedResponse<GroupItemDto>(items.Select(MapItem).ToList(), totalCount, safePage, safePerPage));
        }

        if (dynamicGroups is null)
            return Ok(new PaginatedResponse<GroupItemDto>([], 0, Math.Max(1, page), Math.Clamp(perPage, 1, 250)));

        return Ok(await dynamicGroups.ResolvePageDtosAsync(groupId, findFilter, forceRefresh: false, ct));
    }

    [HttpPost("items")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, RouteValueName = "groupId")]
    public async Task<ActionResult<GroupItemDto>> Create(int groupId, [FromBody] GroupItemCreateDto dto, CancellationToken ct)
    {
        if (!await GroupExistsAsync(groupId, ct))
            return NotFound();

        var host = await ResolveCreateHostAsync(groupId, dto, ct);
        if (host.Error is not null)
            return BadRequest(host.Error);

        var validationError = ValidateItemRange(dto.Kind, dto.StartSec, dto.EndSec);
        if (validationError is not null)
            return BadRequest(validationError);

        var siblings = await db.GroupItems
            .Where(item => item.GroupId == groupId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);
        var insertIndex = Math.Clamp(dto.OrderIndex, 0, siblings.Count);
        foreach (var sibling in siblings.Where(item => item.OrderIndex >= insertIndex))
            sibling.OrderIndex++;

        var item = new GroupItem
        {
            GroupId = groupId,
            OrderIndex = insertIndex,
            Kind = dto.Kind,
            HostType = host.HostType,
            HostId = host.HostId,
            SceneId = host.SceneId,
            ImageId = host.ImageId,
            ChildGroupId = host.ChildGroupId,
            StartSec = dto.Kind == GroupItemKind.Scene ? null : dto.StartSec,
            EndSec = dto.Kind == GroupItemKind.Scene ? null : dto.EndSec,
            Title = NormalizeOptionalText(dto.Title),
            Notes = NormalizeOptionalText(dto.Notes),
            SourceSpanKey = NormalizeOptionalText(dto.SourceSpanKey),
            SourceProfileId = dto.SourceProfileId,
            SourceQueryJson = NormalizeOptionalText(dto.SourceQueryJson),
        };

        db.GroupItems.Add(item);
        await db.SaveChangesAsync(ct);
        await LoadItemReferencesAsync(item, ct);

        return CreatedAtAction(nameof(List), new { groupId }, MapItem(item));
    }

    [HttpPut("items/{id:int}")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, RouteValueName = "groupId")]
    public async Task<ActionResult<GroupItemDto>> Update(int groupId, int id, [FromBody] GroupItemUpdateDto dto, CancellationToken ct)
    {
        var item = await db.GroupItems
            .Include(entry => entry.Scene)
            .FirstOrDefaultAsync(entry => entry.GroupId == groupId && entry.Id == id, ct);
        if (item is null)
            return NotFound();

        var validationError = ValidateItemRange(dto.Kind, dto.StartSec, dto.EndSec);
        if (validationError is not null)
            return BadRequest(validationError);

        var siblings = await db.GroupItems
            .Where(entry => entry.GroupId == groupId && entry.Id != id)
            .OrderBy(entry => entry.OrderIndex)
            .ThenBy(entry => entry.Id)
            .ToListAsync(ct);
        ApplyOrder(siblings, item, dto.OrderIndex);

        item.Kind = dto.Kind;
        item.StartSec = dto.Kind == GroupItemKind.Scene ? null : dto.StartSec;
        item.EndSec = dto.Kind == GroupItemKind.Scene ? null : dto.EndSec;
        item.Title = NormalizeOptionalText(dto.Title);
        item.Notes = NormalizeOptionalText(dto.Notes);

        await db.SaveChangesAsync(ct);
        return Ok(MapItem(item));
    }

    [HttpDelete("items/{id:int}")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, RouteValueName = "groupId")]
    public async Task<IActionResult> Delete(int groupId, int id, CancellationToken ct)
    {
        var item = await db.GroupItems.FirstOrDefaultAsync(entry => entry.GroupId == groupId && entry.Id == id, ct);
        if (item is null)
            return NotFound();

        db.GroupItems.Remove(item);
        await ReindexItemsAsync(groupId, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("items/reorder")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, RouteValueName = "groupId")]
    public async Task<IActionResult> Reorder(int groupId, [FromBody] GroupItemsReorderDto dto, CancellationToken ct)
    {
        var items = await db.GroupItems
            .Where(item => item.GroupId == groupId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);
        if (dto.Ids.Count == 0)
            return BadRequest("Reorder payload must contain at least one group item.");

        var duplicateIds = dto.Ids.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicateIds.Count > 0)
            return BadRequest("Reorder payload must not contain duplicate group items.");

        var expectedIds = items.Select(item => item.Id).ToHashSet();
        var actualIds = dto.Ids.OrderBy(id => id).ToList();
        if (actualIds.Any(id => !expectedIds.Contains(id)))
            return BadRequest("Reorder payload contains a group item that is not in this group.");

        var orderedItems = items.ToList();
        if (dto.Ids.Count != items.Count)
        {
            var movingIds = dto.Ids.ToHashSet();
            orderedItems = orderedItems.Where(item => !movingIds.Contains(item.Id)).ToList();
            var movingItems = dto.Ids.Select(id => items.First(item => item.Id == id)).ToList();
            var insertIndex = Math.Clamp(dto.StartIndex, 0, orderedItems.Count);
            orderedItems.InsertRange(insertIndex, movingItems);
        }
        else
        {
            orderedItems = dto.Ids.Select(id => items.First(item => item.Id == id)).ToList();
        }

        for (var index = 0; index < orderedItems.Count; index++)
            orderedItems[index].OrderIndex = index;

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("items/from-spans")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, RouteValueName = "groupId")]
    public async Task<ActionResult<IReadOnlyList<GroupItemDto>>> CreateFromSpans(int groupId, [FromBody] GroupItemsFromSpansDto dto, CancellationToken ct)
    {
        if (!await GroupExistsAsync(groupId, ct))
            return NotFound();
        if (dto.Spans.Count == 0)
            return Ok(Array.Empty<GroupItemDto>());

        var items = await db.GroupItems
            .Where(item => item.GroupId == groupId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);
        var nextOrderIndex = items.Count;
        var createdItems = new List<GroupItem>();

        foreach (var spanInput in dto.Spans)
        {
            if (spanInput.DerivedQuery is { } derivedQuery)
            {
                if (!spanInput.SceneId.HasValue)
                    return BadRequest("SceneId is required when snapshotting a derived query span.");

                var derivedSpans = await spanResolver.QuerySceneAsync(spanInput.SceneId.Value, new SegmentSpanQueryRequestDto(
                    spanInput.ProfileId,
                    derivedQuery.Operator,
                    derivedQuery.Operands,
                    derivedQuery.MergeGapSec,
                    derivedQuery.MinDurationSec), ct);

                var matchingSpans = !string.IsNullOrWhiteSpace(spanInput.SpanKey)
                    ? derivedSpans.Where(span => string.Equals(span.SpanKey, spanInput.SpanKey, StringComparison.Ordinal)).ToList()
                    : derivedSpans.ToList();

                if (matchingSpans.Count == 0)
                    return BadRequest($"Derived span '{spanInput.SpanKey ?? "<query>"}' was not found.");

                var sourceQueryJson = JsonSerializer.Serialize(derivedQuery);
                foreach (var span in matchingSpans)
                {
                    createdItems.Add(new GroupItem
                    {
                        GroupId = groupId,
                        OrderIndex = nextOrderIndex++,
                        Kind = GroupItemKind.SceneRange,
                        HostType = "scene",
                        HostId = spanInput.SceneId.Value,
                        SceneId = spanInput.SceneId.Value,
                        StartSec = span.StartSec,
                        EndSec = span.EndSec,
                        Title = NormalizeOptionalText(spanInput.Title) ?? span.TagName ?? span.Kind,
                        SourceSpanKey = span.SpanKey,
                        SourceProfileId = spanInput.ProfileId,
                        SourceQueryJson = sourceQueryJson,
                        SnapshotAt = DateTime.UtcNow,
                    });
                }

                continue;
            }

            GroupItem item;
            if (!string.IsNullOrWhiteSpace(spanInput.SpanKey))
            {
                if (!spanInput.SceneId.HasValue)
                    return BadRequest("SceneId is required when snapshotting a resolved span.");

                var detail = await spanResolver.GetSpanDetailAsync(spanInput.SceneId.Value, spanInput.SpanKey, spanInput.ProfileId, ct);
                if (detail is null)
                    return BadRequest($"Resolved span '{spanInput.SpanKey}' was not found.");

                item = new GroupItem
                {
                    GroupId = groupId,
                    OrderIndex = nextOrderIndex++,
                    Kind = GroupItemKind.SceneRange,
                    HostType = "scene",
                    HostId = detail.SceneId,
                    SceneId = detail.SceneId,
                    StartSec = detail.Span.StartSec,
                    EndSec = detail.Span.EndSec,
                    Title = NormalizeOptionalText(spanInput.Title) ?? detail.Span.TagName ?? detail.Span.Kind ?? detail.SceneTitle,
                    SourceSpanKey = detail.Span.SpanKey,
                    SourceProfileId = detail.ProfileId,
                    SourceQueryJson = null,
                    SnapshotAt = DateTime.UtcNow,
                };
            }
            else
            {
                if (!spanInput.SceneId.HasValue)
                    return BadRequest("SceneId is required when creating a group item from manual span input.");
                if (!await SceneExistsAsync(spanInput.SceneId.Value, ct))
                    return BadRequest("Scene was not found.");

                var kind = spanInput.StartSec.HasValue || spanInput.EndSec.HasValue ? GroupItemKind.SceneRange : GroupItemKind.Scene;
                var validationError = ValidateItemRange(kind, spanInput.StartSec, spanInput.EndSec);
                if (validationError is not null)
                    return BadRequest(validationError);

                item = new GroupItem
                {
                    GroupId = groupId,
                    OrderIndex = nextOrderIndex++,
                    Kind = kind,
                    HostType = "scene",
                    HostId = spanInput.SceneId.Value,
                    SceneId = spanInput.SceneId.Value,
                    StartSec = kind == GroupItemKind.Scene ? null : spanInput.StartSec,
                    EndSec = kind == GroupItemKind.Scene ? null : spanInput.EndSec,
                    Title = NormalizeOptionalText(spanInput.Title),
                    SourceProfileId = spanInput.ProfileId,
                    SourceQueryJson = null,
                    SnapshotAt = DateTime.UtcNow,
                };
            }

            createdItems.Add(item);
        }

        db.GroupItems.AddRange(createdItems);
        await db.SaveChangesAsync(ct);

        foreach (var item in createdItems)
            await LoadItemReferencesAsync(item, ct);

        return Ok(createdItems.Select(MapItem).ToList());
    }

    [HttpGet("playback-manifest")]
    [RequiresPermission(Permissions.StreamRead)]
    public async Task<ActionResult<GroupPlaybackManifestDto>> GetPlaybackManifest(int groupId, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(item => item.Id == groupId, ct);
        if (group is null)
            return NotFound();

        if (group.Kind == GroupKind.Dynamic && dynamicGroups is not null)
        {
            var resolved = await dynamicGroups.ResolveAsync(groupId, forceRefresh: false, ct);
            var playable = resolved
                .Where(item => item.SceneId.HasValue && (item.Kind == GroupItemKind.Scene || item.Kind == GroupItemKind.SceneRange))
                .ToList();
            var sceneIds = playable.Select(item => item.SceneId!.Value).Distinct().ToArray();
            var scenes = await db.Scenes.AsNoTracking()
                .Where(scene => sceneIds.Contains(scene.Id))
                .ToDictionaryAsync(scene => scene.Id, ct);

            var dynamicManifest = playable.Select((item, index) =>
            {
                var sceneId = item.SceneId!.Value;
                scenes.TryGetValue(sceneId, out var scene);
                var startSec = item.Kind == GroupItemKind.SceneRange ? item.StartSec ?? 0 : 0;
                var endSec = item.Kind == GroupItemKind.SceneRange ? item.EndSec : null;
                double? durationSec = endSec.HasValue
                    ? Math.Max(0, endSec.Value - startSec)
                    : scene?.MaxDuration > 0
                        ? scene.MaxDuration
                        : null;

                return new GroupPlaybackManifestItemDto(
                    -(index + 1),
                    sceneId,
                    scene?.Title,
                    $"/api/stream/scene/{sceneId}",
                    startSec,
                    endSec,
                    durationSec,
                    $"/api/stream/scene/{sceneId}/screenshot",
                    item.Title ?? scene?.Title);
            }).ToList();

            return Ok(new GroupPlaybackManifestDto(dynamicManifest));
        }

        var items = await db.GroupItems.AsNoTracking()
            .Include(item => item.Scene)
            .Where(item => item.GroupId == groupId)
            .Where(item => (item.Kind == GroupItemKind.Scene || item.Kind == GroupItemKind.SceneRange) && item.SceneId != null)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        var manifest = items.Select(item =>
        {
            var startSec = item.Kind == GroupItemKind.SceneRange ? item.StartSec ?? 0 : 0;
            var endSec = item.Kind == GroupItemKind.SceneRange ? item.EndSec : null;
            double? durationSec = endSec.HasValue
                ? Math.Max(0, endSec.Value - startSec)
                : item.Scene?.MaxDuration > 0
                    ? item.Scene.MaxDuration
                    : null;
            return new GroupPlaybackManifestItemDto(
                item.Id,
                item.SceneId!.Value,
                item.Scene?.Title,
                $"/api/stream/scene/{item.SceneId.Value}",
                startSec,
                endSec,
                durationSec,
                $"/api/stream/scene/{item.SceneId.Value}/screenshot",
                item.Title ?? item.Scene?.Title);
        }).ToList();

        return Ok(new GroupPlaybackManifestDto(manifest));
    }

    private Task<bool> GroupExistsAsync(int groupId, CancellationToken ct)
        => db.Groups.AsNoTracking().AnyAsync(group => group.Id == groupId, ct);

    private Task<bool> SceneExistsAsync(int sceneId, CancellationToken ct)
        => db.Scenes.AsNoTracking().AnyAsync(scene => scene.Id == sceneId, ct);

    private Task<bool> ImageExistsAsync(int imageId, CancellationToken ct)
        => db.Images.AsNoTracking().AnyAsync(image => image.Id == imageId, ct);

    private async Task LoadItemReferencesAsync(GroupItem item, CancellationToken ct)
    {
        if (item.SceneId.HasValue)
        {
            await db.Entry(item).Reference(entry => entry.Scene).LoadAsync(ct);
            if (item.Scene is not null)
                await db.Entry(item.Scene).Collection(scene => scene.Files).LoadAsync(ct);
        }
        if (item.ImageId.HasValue)
            await db.Entry(item).Reference(entry => entry.Image).LoadAsync(ct);
        if (item.ChildGroupId.HasValue)
            await db.Entry(item).Reference(entry => entry.ChildGroup).LoadAsync(ct);
    }

    private async Task<GroupItemHostResolution> ResolveCreateHostAsync(int groupId, GroupItemCreateDto dto, CancellationToken ct)
    {
        var hostType = NormalizeHostType(dto.HostType, dto.Kind);
        var hostId = dto.HostId ?? dto.SceneId;
        if (!hostId.HasValue)
            return GroupItemHostResolution.Fail("Group item host id is required.");

        if (hostType == "scene")
        {
            if (!await SceneExistsAsync(hostId.Value, ct))
                return GroupItemHostResolution.Fail("Scene was not found.");
            return new GroupItemHostResolution(hostType, hostId.Value, hostId.Value, null, null, null);
        }

        if (hostType == "image")
        {
            if (!await ImageExistsAsync(hostId.Value, ct))
                return GroupItemHostResolution.Fail("Image was not found.");
            return new GroupItemHostResolution(hostType, hostId.Value, null, hostId.Value, null, null);
        }

        if (hostType == "group")
        {
            if (hostId.Value == groupId)
                return GroupItemHostResolution.Fail("A group item cannot point to its containing group.");
            if (!await GroupExistsAsync(hostId.Value, ct))
                return GroupItemHostResolution.Fail("Child group was not found.");
            return new GroupItemHostResolution(hostType, hostId.Value, null, null, hostId.Value, null);
        }

        return GroupItemHostResolution.Fail($"Group items do not support host type '{hostType}'.");
    }

    private async Task ReindexItemsAsync(int groupId, CancellationToken ct)
    {
        var items = await db.GroupItems
            .Where(item => item.GroupId == groupId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        for (var index = 0; index < items.Count; index++)
            items[index].OrderIndex = index;
    }

    private static void ApplyOrder(List<GroupItem> siblings, GroupItem item, int desiredIndex)
    {
        var ordered = siblings.OrderBy(entry => entry.OrderIndex).ThenBy(entry => entry.Id).ToList();
        var insertIndex = Math.Clamp(desiredIndex, 0, ordered.Count);
        ordered.Insert(insertIndex, item);
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].OrderIndex = index;
    }

    private static string? ValidateItemRange(GroupItemKind kind, double? startSec, double? endSec)
    {
        if (kind != GroupItemKind.SceneRange)
            return null;

        if (!startSec.HasValue || !endSec.HasValue)
            return "Scene range items require both StartSec and EndSec.";
        if (endSec.Value < startSec.Value)
            return "Group item end must be greater than or equal to the start.";
        return null;
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SceneTitle(Scene? scene)
        => !string.IsNullOrWhiteSpace(scene?.Title)
            ? scene.Title
            : scene?.Files.OrderBy(file => file.Id).FirstOrDefault()?.Basename;

    private static string NormalizeHostType(string? hostType, GroupItemKind kind)
    {
        if (!string.IsNullOrWhiteSpace(hostType))
            return hostType.Trim().ToLowerInvariant();

        return kind switch
        {
            GroupItemKind.Image => "image",
            GroupItemKind.Group => "group",
            GroupItemKind.Audio => "audio",
            GroupItemKind.Text => "text",
            _ => "scene",
        };
    }

    private static GroupItemDto MapItem(GroupItem item) => new(
        item.Id,
        item.GroupId,
        item.OrderIndex,
        item.Kind,
        item.SceneId,
        SceneTitle(item.Scene),
        item.HostType,
        item.HostId,
        item.ImageId,
        item.Image?.Title,
        item.ChildGroupId,
        item.ChildGroup?.Name,
        item.StartSec,
        item.EndSec,
        item.Title,
        item.Notes,
        item.SourceSpanKey,
        item.SourceProfileId,
        item.SourceQueryJson,
        item.SnapshotAt?.ToString("o"),
        item.CreatedAt.ToString("o"),
        item.UpdatedAt.ToString("o"));

    private sealed record GroupItemHostResolution(
        string HostType,
        int HostId,
        int? SceneId,
        int? ImageId,
        int? ChildGroupId,
        string? Error)
    {
        public static GroupItemHostResolution Fail(string error) => new(string.Empty, 0, null, null, null, error);
    }
}