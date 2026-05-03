using System.Text.Json;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:int}")]
[RequiresPermission(Permissions.GroupsRead)]
[RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsRead, RouteValueName = "groupId")]
public class GroupItemsController(CoveContext db, SegmentSpanResolver spanResolver) : ControllerBase
{
    [HttpGet("items")]
    public async Task<ActionResult<IReadOnlyList<GroupItemDto>>> List(int groupId, CancellationToken ct)
    {
        if (!await GroupExistsAsync(groupId, ct))
            return NotFound();

        var items = await db.GroupItems.AsNoTracking()
            .Include(item => item.Scene)
            .Where(item => item.GroupId == groupId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        return Ok(items.Select(MapItem).ToList());
    }

    [HttpPost("items")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite, RouteValueName = "groupId")]
    public async Task<ActionResult<GroupItemDto>> Create(int groupId, [FromBody] GroupItemCreateDto dto, CancellationToken ct)
    {
        if (!await GroupExistsAsync(groupId, ct))
            return NotFound();
        if (!await SceneExistsAsync(dto.SceneId, ct))
            return BadRequest("Scene was not found.");

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
            SceneId = dto.SceneId,
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
        await LoadSceneAsync(item, ct);

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
        if (items.Count != dto.Ids.Count)
            return BadRequest("Reorder payload must contain every group item exactly once.");

        var expectedIds = items.Select(item => item.Id).OrderBy(id => id).ToList();
        var actualIds = dto.Ids.OrderBy(id => id).ToList();
        if (!expectedIds.SequenceEqual(actualIds))
            return BadRequest("Reorder payload must contain every group item exactly once.");

        for (var index = 0; index < dto.Ids.Count; index++)
        {
            var item = items.First(entry => entry.Id == dto.Ids[index]);
            item.OrderIndex = index;
        }

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
            await LoadSceneAsync(item, ct);

        return Ok(createdItems.Select(MapItem).ToList());
    }

    [HttpGet("playback-manifest")]
    [RequiresPermission(Permissions.StreamRead)]
    public async Task<ActionResult<GroupPlaybackManifestDto>> GetPlaybackManifest(int groupId, CancellationToken ct)
    {
        if (!await GroupExistsAsync(groupId, ct))
            return NotFound();

        var items = await db.GroupItems.AsNoTracking()
            .Include(item => item.Scene)
            .Where(item => item.GroupId == groupId)
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
                item.SceneId,
                item.Scene?.Title,
                $"/api/stream/scene/{item.SceneId}",
                startSec,
                endSec,
                durationSec,
                $"/api/stream/scene/{item.SceneId}/screenshot",
                item.Title ?? item.Scene?.Title);
        }).ToList();

        return Ok(new GroupPlaybackManifestDto(manifest));
    }

    private Task<bool> GroupExistsAsync(int groupId, CancellationToken ct)
        => db.Groups.AsNoTracking().AnyAsync(group => group.Id == groupId, ct);

    private Task<bool> SceneExistsAsync(int sceneId, CancellationToken ct)
        => db.Scenes.AsNoTracking().AnyAsync(scene => scene.Id == sceneId, ct);

    private async Task LoadSceneAsync(GroupItem item, CancellationToken ct)
    {
        await db.Entry(item).Reference(entry => entry.Scene).LoadAsync(ct);
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
        if (kind == GroupItemKind.Scene)
            return null;

        if (!startSec.HasValue || !endSec.HasValue)
            return "Scene range items require both StartSec and EndSec.";
        if (endSec.Value < startSec.Value)
            return "Group item end must be greater than or equal to the start.";
        return null;
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static GroupItemDto MapItem(GroupItem item) => new(
        item.Id,
        item.GroupId,
        item.OrderIndex,
        item.Kind,
        item.SceneId,
        item.Scene?.Title,
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
}