using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Cove.Core.Auth;
using Cove.Core.Common;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.ImagesRead)]
public class ImagesController(IImageRepository imageRepo, Data.CoveContext db, ITagProvenanceService? tagProvenanceService = null, ICurrentPrincipalAccessor? principalAccessor = null) : ControllerBase
{
    private bool CanReadFiles => principalAccessor?.Current?.Has(Permissions.FilesRead) == true;
    private static string GetVisibleBasename(string path, string basename) => string.IsNullOrWhiteSpace(basename) ? System.IO.Path.GetFileName(path) : basename;

    [HttpGet]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<PaginatedResponse<ImageDto>>> Find(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int perPage = 25,
        [FromQuery] string? sort = null, [FromQuery] string? direction = null,
        [FromQuery] int? seed = null,
        [FromQuery] string? title = null, [FromQuery] int? rating = null,
        [FromQuery] bool? organized = null, [FromQuery] int? studioId = null,
        [FromQuery] string? tagIds = null, [FromQuery] string? performerIds = null,
        [FromQuery] int? galleryId = null,
        CancellationToken ct = default)
    {
        var filter = new ImageFilter
        {
            Title = title, Rating = rating, Organized = organized, StudioId = studioId,
            TagIds = QueryParsing.ParseIntList(tagIds)?.ToList(), PerformerIds = QueryParsing.ParseIntList(performerIds)?.ToList(),
            GalleryId = galleryId
        };
        var findFilter = new FindFilter
        {
            Q = q, Page = page, PerPage = perPage, Sort = sort,
            Direction = direction == "desc" ? SortDirection.Desc : SortDirection.Asc,
            Seed = seed,
        };

        var (items, totalCount) = await imageRepo.FindAsync(filter, findFilter, ct);
        var dtos = await MapListToDtos(items, ct);
        return Ok(new PaginatedResponse<ImageDto>(dtos, totalCount, page, perPage));
    }

    [HttpPost("find")]
    public async Task<ActionResult<PaginatedResponse<ImageDto>>> FindPost([FromBody] FilteredQueryRequest<ImageFilter> req, CancellationToken ct)
    {
        var findFilter = req.FindFilter ?? new FindFilter();
        var filter = req.ObjectFilter ?? new ImageFilter();
        var (items, totalCount) = await imageRepo.FindAsync(filter, findFilter, ct);
        var dtos = await MapListToDtos(items, ct);
        return Ok(new PaginatedResponse<ImageDto>(dtos, totalCount, findFilter.Page, findFilter.PerPage));
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<ImageDto>> GetById(int id, CancellationToken ct)
    {
        var image = await imageRepo.GetByIdWithRelationsAsync(id, ct);
        if (image == null) return NotFound();
        return Ok(await MapToDtoWithProvenanceAsync(image, ct));
    }

    [HttpPost]
    [RequiresPermission(Permissions.ImagesWrite)]
    public async Task<ActionResult<ImageDto>> Create([FromBody] ImageCreateDto dto, CancellationToken ct)
    {
        var image = new Image
        {
            Title = dto.Title,
            Code = dto.Code,
            Details = dto.Details,
            Photographer = dto.Photographer,
            Rating = dto.Rating,
            Organized = dto.Organized,
            StudioId = dto.StudioId,
            Date = ParseDate(dto.Date)
        };

        if (dto.Urls?.Count > 0)
            image.Urls = dto.Urls.Select(u => new ImageUrl { Url = u }).ToList();
        if (dto.TagIds?.Count > 0)
            image.ImageTags = dto.TagIds.Select(tagId => new ImageTag { TagId = tagId }).ToList();
        if (dto.PerformerIds?.Count > 0)
            image.ImagePerformers = dto.PerformerIds.Select(performerId => new ImagePerformer { PerformerId = performerId }).ToList();
        if (dto.GalleryIds?.Count > 0)
            image.ImageGalleries = dto.GalleryIds.Select(gid => new ImageGallery { GalleryId = gid }).ToList();

        image = await imageRepo.AddAsync(image, ct);
        if (dto.TagIds?.Count > 0 && tagProvenanceService != null)
        {
            await tagProvenanceService.SyncTagSetAsync(AffinityHostType.Image, image.Id, [], dto.TagIds, cancellationToken: ct);
            await db.SaveChangesAsync(ct);
        }
        var result = await imageRepo.GetByIdWithRelationsAsync(image.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = image.Id }, await MapToDtoWithProvenanceAsync(result!, ct));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.ImagesWrite)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.ImagesWrite)]
    public async Task<ActionResult<ImageDto>> Update(int id, [FromBody] ImageUpdateDto dto, CancellationToken ct)
    {
        var image = await imageRepo.GetByIdWithRelationsAsync(id, ct);
        if (image == null) return NotFound();
        var previousTagIds = dto.TagIds != null ? image.ImageTags.Select(imageTag => imageTag.TagId).ToArray() : [];

        if (dto.Title != null) image.Title = dto.Title;
        if (dto.Code != null) image.Code = dto.Code;
        if (dto.Details != null) image.Details = dto.Details;
        if (dto.Photographer != null) image.Photographer = dto.Photographer;
        if (dto.Rating.HasValue) image.Rating = dto.Rating;
        if (dto.Organized.HasValue) image.Organized = dto.Organized.Value;
        if (dto.StudioId.HasValue) image.StudioId = dto.StudioId;
        if (dto.Date != null) image.Date = ParseDate(dto.Date);

        if (dto.Urls != null)
        {
            image.Urls.Clear();
            image.Urls = dto.Urls.Select(u => new ImageUrl { Url = u, ImageId = id }).ToList();
        }
        if (dto.TagIds != null)
        {
            image.ImageTags.Clear();
            image.ImageTags = dto.TagIds.Select(tid => new ImageTag { TagId = tid, ImageId = id }).ToList();
        }
        if (dto.PerformerIds != null)
        {
            image.ImagePerformers.Clear();
            image.ImagePerformers = dto.PerformerIds.Select(pid => new ImagePerformer { PerformerId = pid, ImageId = id }).ToList();
        }
        if (dto.GalleryIds != null)
        {
            image.ImageGalleries.Clear();
            image.ImageGalleries = dto.GalleryIds.Select(gid => new ImageGallery { GalleryId = gid, ImageId = id }).ToList();
        }
        if (dto.CustomFields != null) image.CustomFields = dto.CustomFields;

        if (dto.TagIds != null && tagProvenanceService != null)
        {
            await tagProvenanceService.SyncTagSetAsync(
                AffinityHostType.Image,
                id,
                previousTagIds,
                image.ImageTags.Select(imageTag => imageTag.TagId).ToArray(),
                cancellationToken: ct);
        }

        await imageRepo.UpdateAsync(image, ct);
        var updated = await imageRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(await MapToDtoWithProvenanceAsync(updated!, ct));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.ImagesDelete)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.ImagesDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var img = await imageRepo.GetByIdAsync(id, ct);
        if (img == null) return NotFound();
        if (tagProvenanceService != null)
            await tagProvenanceService.RemoveForHostAsync(AffinityHostType.Image, id, ct);
        await imageRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    private async Task<ImageDto> MapToDtoWithProvenanceAsync(Image image, CancellationToken cancellationToken = default)
    {
        var tagIds = image.ImageTags
            .Where(imageTag => imageTag.Tag != null)
            .Select(imageTag => imageTag.Tag!.Id)
            .Distinct()
            .ToArray();
        var provenanceLookup = tagProvenanceService == null
            ? null
            : await tagProvenanceService.GetLookupAsync(AffinityHostType.Image, image.Id, tagIds, cancellationToken);

        return MapToDto(image, null, provenanceLookup);
    }

    private ImageDto MapToDto(Image i, int? galleryCount = null, IReadOnlyDictionary<int, List<TagProvenanceDto>>? provenanceLookup = null) => new(
        i.Id, i.Title, i.Code, i.Details, i.Photographer,
        i.Rating, i.Organized, i.OCounter, i.StudioId, i.Studio?.Name,
        i.Date?.ToString("yyyy-MM-dd"),
        i.Urls.Select(u => u.Url).ToList(),
        i.ImageTags.Where(it => it.Tag != null).Select(it => new TagDto(it.Tag!.Id, it.Tag.Name, it.Tag.Description, it.Tag.Favorite, it.Tag.IgnoreAutoTag, [], Provenance: GetTagProvenance(provenanceLookup, it.Tag!.Id))).ToList(),
        i.ImagePerformers.Where(ip => ip.Performer != null).Select(ip => new PerformerSummaryDto(ip.Performer!.Id, ip.Performer.Name, ip.Performer.Disambiguation, ip.Performer.Gender?.ToString(), ip.Performer.Birthdate?.ToString("yyyy-MM-dd"), ip.Performer.Favorite, ip.Performer.ImageBlobId != null ? EntityImageUrls.Performer(ip.Performer.Id, ip.Performer.UpdatedAt) : null)).ToList(),
        galleryCount ?? i.GalleryCount,
        i.ImageGalleries?.Select(ig => ig.GalleryId).ToList() ?? [],
        i.ImageGalleries?.Where(ig => ig.Gallery != null).Select(ig => new GallerySummaryDto(ig.GalleryId, ig.Gallery!.Title, ig.Gallery.Date?.ToString("yyyy-MM-dd"))).ToList() ?? [],
        i.Files?.Select(f => new ImageFileDto(
            f.Id,
            CanReadFiles ? f.Path : string.Empty,
            GetVisibleBasename(f.Path, f.Basename),
            f.Format ?? "",
            f.Width,
            f.Height,
            f.Size)).ToList() ?? [],
        i.CustomFields,
        i.CreatedAt.ToString("o"), i.UpdatedAt.ToString("o")
    );

    private Task<List<ImageDto>> MapListToDtos(IReadOnlyList<Image> items, CancellationToken ct)
    {
        return Task.FromResult(items.Count == 0 ? [] : items.Select(i => MapListToDto(i, i.GalleryCount)).ToList());
    }

    private ImageDto MapListToDto(Image i, int galleryCount) => new(
        i.Id, i.Title, i.Code, i.Details, i.Photographer,
        i.Rating, i.Organized, i.OCounter, i.StudioId, i.Studio?.Name,
        i.Date?.ToString("yyyy-MM-dd"),
        i.Urls.Select(u => u.Url).ToList(),
        i.ImageTags.Where(it => it.Tag != null).Select(it => new TagDto(it.Tag!.Id, it.Tag.Name, it.Tag.Description, it.Tag.Favorite, it.Tag.IgnoreAutoTag, [])).ToList(),
        i.ImagePerformers.Where(ip => ip.Performer != null).Select(ip => new PerformerSummaryDto(ip.Performer!.Id, ip.Performer.Name, ip.Performer.Disambiguation, ip.Performer.Gender?.ToString(), ip.Performer.Birthdate?.ToString("yyyy-MM-dd"), ip.Performer.Favorite, ip.Performer.ImageBlobId != null ? EntityImageUrls.Performer(ip.Performer.Id, ip.Performer.UpdatedAt) : null)).ToList(),
        galleryCount,
        i.ImageGalleries?.Select(ig => ig.GalleryId).ToList() ?? [],
        i.ImageGalleries?.Where(ig => ig.Gallery != null).Select(ig => new GallerySummaryDto(ig.GalleryId, ig.Gallery!.Title, ig.Gallery.Date?.ToString("yyyy-MM-dd"))).ToList() ?? [],
        i.Files?.Select(f => new ImageFileDto(
            f.Id,
            CanReadFiles ? f.Path : string.Empty,
            GetVisibleBasename(f.Path, f.Basename),
            f.Format ?? "",
            f.Width,
            f.Height,
            f.Size)).ToList() ?? [],
        null,
        i.CreatedAt.ToString("o"), i.UpdatedAt.ToString("o")
    );

    // ===== Activity Tracking =====

    [HttpPost("{id:int}/o")]
    [RequiresPermission(Permissions.ImagesWrite)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.ImagesWrite)]
    public async Task<ActionResult<int>> IncrementO(int id, CancellationToken ct)
    {
        var image = await imageRepo.GetByIdAsync(id, ct);
        if (image == null) return NotFound();
        image.OCounter++;
        await imageRepo.UpdateAsync(image, ct);
        return Ok(image.OCounter);
    }

    [HttpDelete("{id:int}/o")]
    [RequiresPermission(Permissions.ImagesWrite)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.ImagesWrite)]
    public async Task<ActionResult<int>> DecrementO(int id, CancellationToken ct)
    {
        var image = await imageRepo.GetByIdAsync(id, ct);
        if (image == null) return NotFound();
        image.OCounter = Math.Max(0, image.OCounter - 1);
        await imageRepo.UpdateAsync(image, ct);
        return Ok(image.OCounter);
    }

    [HttpPost("{id:int}/o/reset")]
    [RequiresPermission(Permissions.ImagesWrite)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.ImagesWrite)]
    public async Task<ActionResult<int>> ResetO(int id, CancellationToken ct)
    {
        var image = await imageRepo.GetByIdAsync(id, ct);
        if (image == null) return NotFound();
        image.OCounter = 0;
        await imageRepo.UpdateAsync(image, ct);
        return Ok(image.OCounter);
    }

    // ===== Bulk Operations =====

    [HttpPost("bulk")]
    [RequiresPermission(Permissions.ImagesWrite)]
    [RequiresEntityAccess(EntityKinds.Image, Permissions.ImagesWrite, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkImageUpdateDto dto, CancellationToken ct)
    {
        var images = await db.Images
            .Include(i => i.ImageTags)
            .Include(i => i.ImagePerformers)
            .Include(i => i.ImageGalleries)
            .AsSplitQuery()
            .Where(i => dto.Ids.Contains(i.Id))
            .ToListAsync(ct);

        foreach (var image in images)
        {
            var previousTagIds = dto.TagIds != null ? image.ImageTags.Select(imageTag => imageTag.TagId).ToArray() : [];

            if (dto.Rating.HasValue) image.Rating = dto.Rating;
            if (dto.Organized.HasValue) image.Organized = dto.Organized.Value;
            if (dto.StudioId.HasValue) image.StudioId = dto.StudioId;

            if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Set)
            {
                image.ImageTags.Clear();
                image.ImageTags = dto.TagIds.Select(tid => new ImageTag { TagId = tid, ImageId = image.Id }).ToList();
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Add)
            {
                var existing = image.ImageTags.Select(it => it.TagId).ToHashSet();
                foreach (var tid in dto.TagIds.Where(t => !existing.Contains(t)))
                    image.ImageTags.Add(new ImageTag { TagId = tid, ImageId = image.Id });
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Remove)
            {
                image.ImageTags = image.ImageTags.Where(it => !dto.TagIds.Contains(it.TagId)).ToList();
            }

            if (dto.TagIds != null && tagProvenanceService != null)
            {
                await tagProvenanceService.SyncTagSetAsync(
                    AffinityHostType.Image,
                    image.Id,
                    previousTagIds,
                    image.ImageTags.Select(imageTag => imageTag.TagId).ToArray(),
                    cancellationToken: ct);
            }

            if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Set)
            {
                image.ImagePerformers.Clear();
                image.ImagePerformers = dto.PerformerIds.Select(pid => new ImagePerformer { PerformerId = pid, ImageId = image.Id }).ToList();
            }
            else if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Add)
            {
                var existing = image.ImagePerformers.Select(ip => ip.PerformerId).ToHashSet();
                foreach (var pid in dto.PerformerIds.Where(p => !existing.Contains(p)))
                    image.ImagePerformers.Add(new ImagePerformer { PerformerId = pid, ImageId = image.Id });
            }
            else if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Remove)
            {
                image.ImagePerformers = image.ImagePerformers.Where(ip => !dto.PerformerIds.Contains(ip.PerformerId)).ToList();
            }

            if (dto.GalleryIds != null && dto.GalleryMode == BulkUpdateMode.Set)
            {
                image.ImageGalleries.Clear();
                image.ImageGalleries = dto.GalleryIds.Select(gid => new ImageGallery { GalleryId = gid, ImageId = image.Id }).ToList();
            }
            else if (dto.GalleryIds != null && dto.GalleryMode == BulkUpdateMode.Add)
            {
                var existing = image.ImageGalleries.Select(ig => ig.GalleryId).ToHashSet();
                foreach (var gid in dto.GalleryIds.Where(g => !existing.Contains(g)))
                    image.ImageGalleries.Add(new ImageGallery { GalleryId = gid, ImageId = image.Id });
            }
            else if (dto.GalleryIds != null && dto.GalleryMode == BulkUpdateMode.Remove)
            {
                image.ImageGalleries = image.ImageGalleries.Where(ig => !dto.GalleryIds.Contains(ig.GalleryId)).ToList();
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { updated = images.Count });
    }

    private static DateOnly? ParseDate(string? date) => DateOnly.TryParse(date, out var d) ? d : null;

    private static List<TagProvenanceDto> GetTagProvenance(IReadOnlyDictionary<int, List<TagProvenanceDto>>? provenanceLookup, int tagId)
        => provenanceLookup != null && provenanceLookup.TryGetValue(tagId, out var provenance) ? provenance : [];
}
