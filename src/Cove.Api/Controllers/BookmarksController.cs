using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/me/bookmarks")]
public class BookmarksController(CoveContext db, ICurrentPrincipalAccessor principalAccessor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookmarkDto>>> List(CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is not int userId)
            return Forbid();

        var rows = await db.UserBookmarks.AsNoTracking()
            .Where(bookmark => bookmark.UserId == userId)
            .OrderByDescending(bookmark => bookmark.CreatedAt)
            .Select(bookmark => new BookmarkDto(bookmark.HostType, bookmark.HostId, bookmark.CreatedAt.ToString("o")))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<IReadOnlyList<BookmarkStateDto>>> Batch([FromBody] BookmarkBatchRequestDto dto, CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is not int userId)
            return Forbid();
        if (!HasPermission(dto.HostType))
            return Forbid();

        var ids = dto.HostIds.Distinct().ToList();
        var rows = await db.UserBookmarks.AsNoTracking()
            .Where(bookmark => bookmark.UserId == userId && bookmark.HostType == dto.HostType && ids.Contains(bookmark.HostId))
            .ToDictionaryAsync(bookmark => bookmark.HostId, bookmark => bookmark.CreatedAt, ct);

        return Ok(ids.Select(id => new BookmarkStateDto(
            dto.HostType,
            id,
            rows.ContainsKey(id),
            rows.TryGetValue(id, out var createdAt) ? createdAt.ToString("o") : null)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<BookmarkStateDto>> Toggle([FromBody] BookmarkToggleDto dto, CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is not int userId)
            return Forbid();
        if (!HasPermission(dto.HostType))
            return Forbid();
        if (!await HostExistsAsync(dto.HostType, dto.HostId, ct))
            return NotFound();

        var existing = await db.UserBookmarks.FirstOrDefaultAsync(
            bookmark => bookmark.UserId == userId && bookmark.HostType == dto.HostType && bookmark.HostId == dto.HostId,
            ct);

        if (dto.Saved)
        {
            if (existing is null)
            {
                existing = new UserBookmark
                {
                    UserId = userId,
                    HostType = dto.HostType,
                    HostId = dto.HostId,
                    CreatedAt = DateTime.UtcNow,
                };
                db.UserBookmarks.Add(existing);
            }
        }
        else if (existing is not null)
        {
            db.UserBookmarks.Remove(existing);
        }

        await db.SaveChangesAsync(ct);
        return Ok(new BookmarkStateDto(dto.HostType, dto.HostId, dto.Saved, dto.Saved ? existing?.CreatedAt.ToString("o") : null));
    }

    private async Task<bool> HostExistsAsync(AffinityHostType hostType, int hostId, CancellationToken ct)
        => hostType switch
        {
            AffinityHostType.Scene => await db.Scenes.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            AffinityHostType.Image => await db.Images.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            AffinityHostType.Performer => await db.Performers.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            AffinityHostType.Face => await db.Faces.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            AffinityHostType.Tag => await db.Tags.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            AffinityHostType.Studio => await db.Studios.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            AffinityHostType.Gallery => await db.Galleries.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            AffinityHostType.Group => await db.Groups.AsNoTracking().AnyAsync(item => item.Id == hostId, ct),
            _ => false,
        };

    private bool HasPermission(AffinityHostType hostType)
    {
        var permission = hostType switch
        {
            AffinityHostType.Scene => Permissions.ScenesRead,
            AffinityHostType.Image => Permissions.ImagesRead,
            AffinityHostType.Performer => Permissions.PerformersRead,
            AffinityHostType.Face => Permissions.FacesRead,
            AffinityHostType.Tag => Permissions.TagsRead,
            AffinityHostType.Studio => Permissions.StudiosRead,
            AffinityHostType.Gallery => Permissions.GalleriesRead,
            AffinityHostType.Group => Permissions.GroupsRead,
            _ => null,
        };
        return permission is not null && principalAccessor.Current?.Has(permission) == true;
    }
}
