using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/taggroups")]
[RequiresPermission(Permissions.TagGroupsRead)]
public sealed class TagGroupsController(CoveContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TagGroupDto>>> List(CancellationToken ct)
    {
        var groups = await db.TagGroups
            .AsNoTracking()
            .Select(group => new
            {
                Group = group,
                TagCount = group.Tags.Count,
            })
            .OrderBy(item => item.Group.SortOrder)
            .ThenBy(item => item.Group.Name)
            .ToListAsync(ct);

        return Ok(groups.Select(item => Map(item.Group, item.TagCount)).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TagGroupDto>> Get(int id, CancellationToken ct)
    {
        var group = await db.TagGroups
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { Group = item, TagCount = item.Tags.Count })
            .FirstOrDefaultAsync(ct);

        return group == null ? NotFound() : Ok(Map(group.Group, group.TagCount));
    }

    [HttpPost]
    [RequiresPermission(Permissions.TagGroupsWrite)]
    public async Task<ActionResult<TagGroupDto>> Create([FromBody] TagGroupCreateDto dto, CancellationToken ct)
    {
        var name = NormalizeName(dto.Name);
        if (name == null)
            return BadRequest(new { message = "Name is required." });

        if (!IsValidColor(dto.Color))
            return BadRequest(new { message = "Color must be #RRGGBB or #RRGGBBAA." });

        var exists = await db.TagGroups.AnyAsync(group => group.Name == name, ct);
        if (exists)
            return Conflict(new { message = $"Tag group '{name}' already exists." });

        var group = new TagGroup
        {
            Name = name,
            Description = NormalizeOptionalText(dto.Description),
            Color = NormalizeOptionalText(dto.Color),
            SortOrder = dto.SortOrder ?? await NextSortOrderAsync(ct),
        };

        db.TagGroups.Add(group);
        await db.SaveChangesAsync(ct);

        var result = Map(group, 0);
        return CreatedAtAction(nameof(Get), new { id = group.Id }, result);
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.TagGroupsWrite)]
    public async Task<ActionResult<TagGroupDto>> Update(int id, [FromBody] TagGroupUpdateDto dto, CancellationToken ct)
    {
        var group = await db.TagGroups.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (group == null)
            return NotFound();

        if (!IsValidColor(dto.Color))
            return BadRequest(new { message = "Color must be #RRGGBB or #RRGGBBAA." });

        var name = NormalizeName(dto.Name);
        if (name != null && !string.Equals(name, group.Name, StringComparison.Ordinal))
        {
            var exists = await db.TagGroups.AnyAsync(item => item.Id != id && item.Name == name, ct);
            if (exists)
                return Conflict(new { message = $"Tag group '{name}' already exists." });

            group.Name = name;
        }

        if (dto.Description != null) group.Description = NormalizeOptionalText(dto.Description);
        if (dto.Color != null) group.Color = NormalizeOptionalText(dto.Color);
        if (dto.SortOrder.HasValue) group.SortOrder = dto.SortOrder.Value;

        await db.SaveChangesAsync(ct);

        var tagCount = await db.Tags.CountAsync(tag => tag.TagGroupId == group.Id, ct);
        return Ok(Map(group, tagCount));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.TagGroupsDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var group = await db.TagGroups.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (group == null)
            return NotFound();

        db.TagGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static TagGroupDto Map(TagGroup group, int tagCount)
        => new(group.Id, group.Name, group.Description, group.Color, group.SortOrder, tagCount, group.CreatedAt.ToString("o"), group.UpdatedAt.ToString("o"));

    private async Task<int> NextSortOrderAsync(CancellationToken ct)
    {
        var max = await db.TagGroups.Select(group => (int?)group.SortOrder).MaxAsync(ct);
        return max.HasValue ? max.Value + 10 : 10;
    }

    private static string? NormalizeName(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidColor(string? value)
    {
        var normalized = NormalizeOptionalText(value);
        if (normalized == null)
            return true;

        if (normalized.Length is not (7 or 9) || normalized[0] != '#')
            return false;

        for (var i = 1; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }

        return true;
    }
}