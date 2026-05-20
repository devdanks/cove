using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api")]
public class EntityImageController(CoveContext db, IBlobService blobService, IThumbnailService thumbnailService, IStreamService streamService) : ControllerBase
{
    // ── Scenes ──────────────────────────────────────────────────

    [HttpPost("scenes/{id:int}/image")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> UploadSceneImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Scenes.FirstOrDefaultAsync(scene => scene.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.ImageBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageBlobId });
    }

    [HttpGet("scenes/{id:int}/image")]
    [RequiresPermission(Permissions.ScenesRead)]
    public async Task<IActionResult> GetSceneImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Scenes.FirstOrDefaultAsync(scene => scene.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageBlobId == null)
        {
            var screenshot = await streamService.GetSceneScreenshot(id, null, ct);
            if (screenshot == null) return NotFound();

            Response.Headers.CacheControl = !string.IsNullOrWhiteSpace(v)
                ? "public, max-age=31536000, immutable"
                : screenshot.Value.useLongCache
                    ? "public, max-age=86400"
                    : "no-store, no-cache, max-age=0, must-revalidate";
            return File(screenshot.Value.stream, screenshot.Value.contentType);
        }

        return await ServeBlobAsync(entity.ImageBlobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("scenes/{id:int}/image")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> DeleteSceneImage(int id, CancellationToken ct)
    {
        var entity = await db.Scenes.FirstOrDefaultAsync(scene => scene.Id == id, ct);
        if (entity?.ImageBlobId == null) return NotFound();

        await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);
        entity.ImageBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Performers ──────────────────────────────────────────────

    [HttpPost("performers/{id:int}/image")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<IActionResult> UploadPerformerImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Performers.FirstOrDefaultAsync(performer => performer.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageOverrideBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageOverrideBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.ImageOverrideBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageOverrideBlobId });
    }

    [HttpGet("performers/{id:int}/image")]
    [RequiresPermission(Permissions.PerformersRead)]
    public async Task<IActionResult> GetPerformerImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Performers.FirstOrDefaultAsync(performer => performer.Id == id, ct);
        var blobId = entity?.ImageOverrideBlobId ?? entity?.ImageBlobId;
        if (blobId == null) return NotFound();

        return await ServeBlobAsync(blobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("performers/{id:int}/image")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<IActionResult> DeletePerformerImage(int id, CancellationToken ct)
    {
        var entity = await db.Performers.FirstOrDefaultAsync(performer => performer.Id == id, ct);
        if (entity == null) return NotFound();
        if (entity.ImageOverrideBlobId == null) return NoContent();

        await blobService.DeleteBlobAsync(entity.ImageOverrideBlobId, ct);
        entity.ImageOverrideBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("performers/{id:int}/image/source")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<IActionResult> SetPerformerImageFromSource(int id, [FromBody] EntityImageCoverSourceDto dto, CancellationToken ct)
    {
        var entity = await db.Performers.FirstOrDefaultAsync(performer => performer.Id == id, ct);
        if (entity == null) return NotFound();

        var source = await StoreCoverSourceBlobAsync(dto, ct);
        if (source.Error != null) return BadRequest(source.Error);

        await ReplaceBlobAsync(entity.ImageOverrideBlobId, source.BlobId!, blobId => entity.ImageOverrideBlobId = blobId, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageOverrideBlobId });
    }

    // ── Audios ─────────────────────────────────────────────────

    [HttpPost("audios/{id:int}/image")]
    [RequiresPermission(Permissions.AudiosWrite)]
    [RequiresEntityAccess(EntityKinds.Audio, Permissions.AudiosWrite)]
    public async Task<IActionResult> UploadAudioImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Audios.FirstOrDefaultAsync(audio => audio.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.ImageBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageBlobId });
    }

    [HttpGet("audios/{id:int}/image")]
    [RequiresPermission(Permissions.AudiosRead)]
    public async Task<IActionResult> GetAudioImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Audios.FirstOrDefaultAsync(audio => audio.Id == id, ct);
        if (entity?.ImageBlobId == null) return NotFound();

        return await ServeBlobAsync(entity.ImageBlobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("audios/{id:int}/image")]
    [RequiresPermission(Permissions.AudiosWrite)]
    [RequiresEntityAccess(EntityKinds.Audio, Permissions.AudiosWrite)]
    public async Task<IActionResult> DeleteAudioImage(int id, CancellationToken ct)
    {
        var entity = await db.Audios.FirstOrDefaultAsync(audio => audio.Id == id, ct);
        if (entity?.ImageBlobId == null) return NotFound();

        await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);
        entity.ImageBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Texts ──────────────────────────────────────────────────

    [HttpPost("texts/{id:int}/image")]
    [RequiresPermission(Permissions.TextsWrite)]
    [RequiresEntityAccess(EntityKinds.Text, Permissions.TextsWrite)]
    public async Task<IActionResult> UploadTextImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.TextDocuments.FirstOrDefaultAsync(text => text.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.ImageBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageBlobId });
    }

    [HttpGet("texts/{id:int}/image")]
    [RequiresPermission(Permissions.TextsRead)]
    public async Task<IActionResult> GetTextImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.TextDocuments.FirstOrDefaultAsync(text => text.Id == id, ct);
        if (entity?.ImageBlobId == null) return NotFound();

        return await ServeBlobAsync(entity.ImageBlobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("texts/{id:int}/image")]
    [RequiresPermission(Permissions.TextsWrite)]
    [RequiresEntityAccess(EntityKinds.Text, Permissions.TextsWrite)]
    public async Task<IActionResult> DeleteTextImage(int id, CancellationToken ct)
    {
        var entity = await db.TextDocuments.FirstOrDefaultAsync(text => text.Id == id, ct);
        if (entity?.ImageBlobId == null) return NotFound();

        await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);
        entity.ImageBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Faces ──────────────────────────────────────────────────

    [HttpPost("faces/{id:int}/image")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<IActionResult> UploadFaceImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Faces.FirstOrDefaultAsync(face => face.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.CoverBlobId != null)
            await blobService.DeleteBlobAsync(entity.CoverBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.CoverBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.CoverBlobId });
    }

    [HttpGet("faces/{id:int}/image")]
    [RequiresPermission(Permissions.FacesRead)]
    public async Task<IActionResult> GetFaceImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Faces.FirstOrDefaultAsync(face => face.Id == id, ct);
        if (entity?.CoverBlobId == null) return NotFound();

        return await ServeBlobAsync(entity.CoverBlobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("faces/{id:int}/image")]
    [RequiresPermission(Permissions.FacesWrite)]
    [RequiresEntityAccess(EntityKinds.Face, Permissions.FacesWrite)]
    public async Task<IActionResult> DeleteFaceImage(int id, CancellationToken ct)
    {
        var entity = await db.Faces.FirstOrDefaultAsync(face => face.Id == id, ct);
        if (entity?.CoverBlobId == null) return NotFound();

        await blobService.DeleteBlobAsync(entity.CoverBlobId, ct);
        entity.CoverBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Studios ─────────────────────────────────────────────────

    [HttpPost("studios/{id:int}/image")]
    [RequiresPermission(Permissions.StudiosWrite)]
    [RequiresEntityAccess(EntityKinds.Studio, Permissions.StudiosWrite)]
    public async Task<IActionResult> UploadStudioImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Studios.FirstOrDefaultAsync(studio => studio.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageOverrideBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageOverrideBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.ImageOverrideBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageOverrideBlobId });
    }

    [HttpGet("studios/{id:int}/image")]
    [RequiresPermission(Permissions.StudiosRead)]
    public async Task<IActionResult> GetStudioImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Studios.FirstOrDefaultAsync(studio => studio.Id == id, ct);
        var blobId = entity?.ImageOverrideBlobId ?? entity?.ImageBlobId;
        if (blobId == null) return NotFound();

        return await ServeBlobAsync(blobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("studios/{id:int}/image")]
    [RequiresPermission(Permissions.StudiosWrite)]
    [RequiresEntityAccess(EntityKinds.Studio, Permissions.StudiosWrite)]
    public async Task<IActionResult> DeleteStudioImage(int id, CancellationToken ct)
    {
        var entity = await db.Studios.FirstOrDefaultAsync(studio => studio.Id == id, ct);
        if (entity == null) return NotFound();
        if (entity.ImageOverrideBlobId == null) return NoContent();

        await blobService.DeleteBlobAsync(entity.ImageOverrideBlobId, ct);
        entity.ImageOverrideBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("studios/{id:int}/image/source")]
    [RequiresPermission(Permissions.StudiosWrite)]
    [RequiresEntityAccess(EntityKinds.Studio, Permissions.StudiosWrite)]
    public async Task<IActionResult> SetStudioImageFromSource(int id, [FromBody] EntityImageCoverSourceDto dto, CancellationToken ct)
    {
        var entity = await db.Studios.FirstOrDefaultAsync(studio => studio.Id == id, ct);
        if (entity == null) return NotFound();

        var source = await StoreCoverSourceBlobAsync(dto, ct);
        if (source.Error != null) return BadRequest(source.Error);

        await ReplaceBlobAsync(entity.ImageOverrideBlobId, source.BlobId!, blobId => entity.ImageOverrideBlobId = blobId, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageOverrideBlobId });
    }

    // ── Tags ────────────────────────────────────────────────────

    [HttpPost("tags/{id:int}/image")]
    [RequiresPermission(Permissions.TagsWrite)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsWrite)]
    public async Task<IActionResult> UploadTagImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Tags.FirstOrDefaultAsync(tag => tag.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageOverrideBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageOverrideBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.ImageOverrideBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageOverrideBlobId });
    }

    [HttpGet("tags/{id:int}/image")]
    [RequiresPermission(Permissions.TagsRead)]
    public async Task<IActionResult> GetTagImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Tags.FirstOrDefaultAsync(tag => tag.Id == id, ct);
        var blobId = entity?.ImageOverrideBlobId ?? entity?.ImageBlobId;
        if (blobId == null) return NotFound();

        return await ServeBlobAsync(blobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("tags/{id:int}/image")]
    [RequiresPermission(Permissions.TagsWrite)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsWrite)]
    public async Task<IActionResult> DeleteTagImage(int id, CancellationToken ct)
    {
        var entity = await db.Tags.FirstOrDefaultAsync(tag => tag.Id == id, ct);
        if (entity == null) return NotFound();
        if (entity.ImageOverrideBlobId == null) return NoContent();

        await blobService.DeleteBlobAsync(entity.ImageOverrideBlobId, ct);
        entity.ImageOverrideBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("tags/{id:int}/image/source")]
    [RequiresPermission(Permissions.TagsWrite)]
    [RequiresEntityAccess(EntityKinds.Tag, Permissions.TagsWrite)]
    public async Task<IActionResult> SetTagImageFromSource(int id, [FromBody] EntityImageCoverSourceDto dto, CancellationToken ct)
    {
        var entity = await db.Tags.FirstOrDefaultAsync(tag => tag.Id == id, ct);
        if (entity == null) return NotFound();

        var source = await StoreCoverSourceBlobAsync(dto, ct);
        if (source.Error != null) return BadRequest(source.Error);

        await ReplaceBlobAsync(entity.ImageOverrideBlobId, source.BlobId!, blobId => entity.ImageOverrideBlobId = blobId, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageOverrideBlobId });
    }

    // ── Groups (front) ──────────────────────────────────────────

    [HttpPost("groups/{id:int}/image/front")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<IActionResult> UploadGroupFrontImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Groups.FirstOrDefaultAsync(group => group.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.FrontImageBlobId != null)
            await blobService.DeleteBlobAsync(entity.FrontImageBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.FrontImageBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.FrontImageBlobId });
    }

    [HttpGet("groups/{id:int}/image/front")]
    [RequiresPermission(Permissions.GroupsRead)]
    public async Task<IActionResult> GetGroupFrontImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Groups.AsNoTracking().FirstOrDefaultAsync(group => group.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.FrontImageBlobId != null)
            return await ServeBlobAsync(entity.FrontImageBlobId, max, !string.IsNullOrWhiteSpace(v), ct);

        var fallback = await db.GroupItems.AsNoTracking()
            .Where(item => item.GroupId == id && (item.ImageId.HasValue || item.SceneId.HasValue))
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .Select(item => new { item.ImageId, item.SceneId, item.StartSec })
            .FirstOrDefaultAsync(ct);

        if (fallback?.ImageId is int imageId)
            return Redirect(WithQuery($"/api/stream/image/{imageId}/thumbnail", max, v));

        if (fallback?.SceneId is int sceneId)
            return Redirect(WithQuery($"/api/stream/scene/{sceneId}/screenshot", null, v, fallback.StartSec));

        return NotFound();
    }

    [HttpDelete("groups/{id:int}/image/front")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<IActionResult> DeleteGroupFrontImage(int id, CancellationToken ct)
    {
        var entity = await db.Groups.FirstOrDefaultAsync(group => group.Id == id, ct);
        if (entity == null) return NotFound();
        if (entity.FrontImageBlobId == null) return NoContent();

        await blobService.DeleteBlobAsync(entity.FrontImageBlobId, ct);
        entity.FrontImageBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("groups/{id:int}/image/front/source")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<IActionResult> SetGroupFrontImageFromSource(int id, [FromBody] EntityImageCoverSourceDto dto, CancellationToken ct)
    {
        var entity = await db.Groups.FirstOrDefaultAsync(group => group.Id == id, ct);
        if (entity == null) return NotFound();

        var source = await StoreCoverSourceBlobAsync(dto, ct);
        if (source.Error != null) return BadRequest(source.Error);

        await ReplaceBlobAsync(entity.FrontImageBlobId, source.BlobId!, blobId => entity.FrontImageBlobId = blobId, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.FrontImageBlobId });
    }

    // ── Groups (back) ───────────────────────────────────────────

    [HttpPost("groups/{id:int}/image/back")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<IActionResult> UploadGroupBackImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Groups.FirstOrDefaultAsync(group => group.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.BackImageBlobId != null)
            await blobService.DeleteBlobAsync(entity.BackImageBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.BackImageBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.BackImageBlobId });
    }

    [HttpGet("groups/{id:int}/image/back")]
    [RequiresPermission(Permissions.GroupsRead)]
    public async Task<IActionResult> GetGroupBackImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Groups.FirstOrDefaultAsync(group => group.Id == id, ct);
        if (entity?.BackImageBlobId == null) return NotFound();

        return await ServeBlobAsync(entity.BackImageBlobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("groups/{id:int}/image/back")]
    [RequiresPermission(Permissions.GroupsWrite)]
    [RequiresEntityAccess(EntityKinds.Group, Permissions.GroupsWrite)]
    public async Task<IActionResult> DeleteGroupBackImage(int id, CancellationToken ct)
    {
        var entity = await db.Groups.FirstOrDefaultAsync(group => group.Id == id, ct);
        if (entity?.BackImageBlobId == null) return NotFound();

        await blobService.DeleteBlobAsync(entity.BackImageBlobId, ct);
        entity.BackImageBlobId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Galleries ───────────────────────────────────────────────

    [HttpPost("galleries/{id:int}/image")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<IActionResult> UploadGalleryImage(int id, IFormFile file, CancellationToken ct)
    {
        if (!IsImage(file)) return BadRequest("File must be an image.");

        var entity = await db.Galleries.FirstOrDefaultAsync(gallery => gallery.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);

        await using var stream = file.OpenReadStream();
        entity.ImageBlobId = await blobService.StoreBlobAsync(stream, file.ContentType, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageBlobId });
    }

    [HttpGet("galleries/{id:int}/image")]
    [RequiresPermission(Permissions.GalleriesRead)]
    public async Task<IActionResult> GetGalleryImage(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var entity = await db.Galleries.FirstOrDefaultAsync(gallery => gallery.Id == id, ct);
        if (entity?.ImageBlobId == null) return NotFound();

        return await ServeBlobAsync(entity.ImageBlobId, max, !string.IsNullOrWhiteSpace(v), ct);
    }

    [HttpDelete("galleries/{id:int}/image")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<IActionResult> DeleteGalleryImage(int id, CancellationToken ct)
    {
        var entity = await db.Galleries.FirstOrDefaultAsync(gallery => gallery.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.ImageBlobId != null)
            await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);
        entity.ImageBlobId = null;
        entity.CoverImageId = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("galleries/{id:int}/image/source")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<IActionResult> SetGalleryImageFromSource(int id, [FromBody] EntityImageCoverSourceDto dto, CancellationToken ct)
    {
        var entity = await db.Galleries.FirstOrDefaultAsync(gallery => gallery.Id == id, ct);
        if (entity == null) return NotFound();

        if (dto.ImageId.HasValue && !dto.SceneId.HasValue)
        {
            var belongs = await db.Set<ImageGallery>()
                .AnyAsync(ig => ig.GalleryId == id && ig.ImageId == dto.ImageId.Value, ct);
            if (!belongs) return BadRequest("Image does not belong to this gallery");

            if (entity.ImageBlobId != null)
                await blobService.DeleteBlobAsync(entity.ImageBlobId, ct);

            entity.ImageBlobId = null;
            entity.CoverImageId = dto.ImageId.Value;
            await db.SaveChangesAsync(ct);
            return Ok();
        }

        var source = await StoreCoverSourceBlobAsync(dto, ct);
        if (source.Error != null) return BadRequest(source.Error);

        entity.CoverImageId = null;
        await ReplaceBlobAsync(entity.ImageBlobId, source.BlobId!, blobId => entity.ImageBlobId = blobId, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new { blobId = entity.ImageBlobId });
    }

    // ── Gallery Cover (Set from gallery images) ─────────────────

    [HttpPut("galleries/{id:int}/cover")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<IActionResult> SetGalleryCover(int id, [FromBody] GallerySetCoverDto dto, CancellationToken ct)
    {
        var gallery = await db.Galleries.FirstOrDefaultAsync(entity => entity.Id == id, ct);
        if (gallery == null) return NotFound();

        var belongs = await db.Set<ImageGallery>()
            .AnyAsync(ig => ig.GalleryId == id && ig.ImageId == dto.ImageId, ct);
        if (!belongs) return BadRequest("Image does not belong to this gallery");

        if (gallery.ImageBlobId != null)
            await blobService.DeleteBlobAsync(gallery.ImageBlobId, ct);

        gallery.ImageBlobId = null;
        gallery.CoverImageId = dto.ImageId;
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpDelete("galleries/{id:int}/cover")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<IActionResult> ResetGalleryCover(int id, CancellationToken ct)
    {
        var gallery = await db.Galleries.FirstOrDefaultAsync(entity => entity.Id == id, ct);
        if (gallery == null) return NotFound();

        if (gallery.ImageBlobId != null)
            await blobService.DeleteBlobAsync(gallery.ImageBlobId, ct);

        gallery.ImageBlobId = null;
        gallery.CoverImageId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static bool IsImage(IFormFile file) =>
        file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private async Task<(string? BlobId, string? Error)> StoreCoverSourceBlobAsync(EntityImageCoverSourceDto dto, CancellationToken ct)
    {
        if (dto.ImageId.HasValue == dto.SceneId.HasValue)
            return (null, "Choose exactly one source image or source scene.");

        if (dto.ImageId.HasValue)
        {
            var image = await thumbnailService.GetImageStreamAsync(dto.ImageId.Value, ct);
            if (image == null) return (null, "Source image file is unavailable.");

            await using var stream = image.Value.stream;
            return (await blobService.StoreBlobAsync(stream, image.Value.contentType, ct), null);
        }

        var screenshot = await streamService.GetSceneScreenshot(dto.SceneId!.Value, null, ct);
        if (screenshot == null) return (null, "Source scene screenshot is unavailable.");

        await using var screenshotStream = screenshot.Value.stream;
        return (await blobService.StoreBlobAsync(screenshotStream, screenshot.Value.contentType, ct), null);
    }

    private async Task ReplaceBlobAsync(string? currentBlobId, string newBlobId, Action<string> assign, CancellationToken ct)
    {
        assign(newBlobId);

        if (!string.IsNullOrWhiteSpace(currentBlobId) && !string.Equals(currentBlobId, newBlobId, StringComparison.Ordinal))
            await blobService.DeleteBlobAsync(currentBlobId, ct);
    }

    private async Task<IActionResult> ServeBlobAsync(string blobId, int? maxDimension, bool immutable, CancellationToken ct)
    {
        (Stream stream, string contentType, bool supportsRangeRequests)? result;

        if (maxDimension.HasValue && maxDimension.Value > 0)
        {
            result = await thumbnailService.GetBlobImageThumbnailStreamAsync(blobId, maxDimension.Value, ct);
        }
        else
        {
            var blob = await blobService.GetBlobAsync(blobId, ct);
            result = blob == null ? null : (blob.Value.Stream, blob.Value.ContentType, blob.Value.Stream.CanSeek);
        }

        if (result == null) return NotFound();

        var cacheControl = immutable
            ? "public, max-age=31536000, immutable"
            : "public, max-age=3600";

        Response.Headers.CacheControl = cacheControl;
        return File(result.Value.stream, result.Value.contentType, enableRangeProcessing: result.Value.supportsRangeRequests);
    }

    private static string WithQuery(string path, int? max, string? version, double? seconds = null)
    {
        var query = new List<string>();
        if (max.HasValue && max.Value > 0) query.Add($"max={max.Value}");
        if (!string.IsNullOrWhiteSpace(version)) query.Add($"v={Uri.EscapeDataString(version)}");
        if (seconds.HasValue) query.Add($"seconds={seconds.Value.ToString(CultureInfo.InvariantCulture)}");
        return query.Count == 0 ? path : $"{path}?{string.Join("&", query)}";
    }
}
