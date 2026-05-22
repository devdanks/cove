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
[RequiresPermission(Permissions.GalleriesRead)]
public class GalleriesController(IGalleryRepository galleryRepo, Data.CoveContext db, IUserEngagementService engagementService, ITagProvenanceService? tagProvenanceService = null, CustomFieldService? customFields = null, IFieldProvenanceService? fieldProvenanceService = null) : ControllerBase
{
    private readonly CustomFieldService _customFields = customFields ?? new CustomFieldService(db);
    private sealed record GalleryRelationshipCounts(IReadOnlyDictionary<int, int> ImageCounts, IReadOnlyDictionary<int, int> SceneCounts);

    [HttpGet]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<PaginatedResponse<GalleryDto>>> Find(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int perPage = 25,
        [FromQuery] string? sort = null, [FromQuery] string? direction = null,
        [FromQuery] int? seed = null,
        [FromQuery] string? title = null, [FromQuery] int? rating = null,
        [FromQuery] bool? organized = null, [FromQuery] int? studioId = null, [FromQuery] int? imageId = null,
        [FromQuery] string? tagIds = null, [FromQuery] string? performerIds = null,
        CancellationToken ct = default)
    {
        var filter = new GalleryFilter
        {
            Title = title, Rating = rating, Organized = organized, StudioId = studioId,
            ImageId = imageId,
            TagIds = QueryParsing.ParseIntList(tagIds)?.ToList(), PerformerIds = QueryParsing.ParseIntList(performerIds)?.ToList()
        };
        var findFilter = new FindFilter
        {
            Q = q, Page = page, PerPage = perPage, Sort = sort,
            Direction = direction == "desc" ? SortDirection.Desc : SortDirection.Asc,
            Seed = seed,
        };

        var (items, totalCount) = await galleryRepo.FindAsync(filter, findFilter, ct);
        var dtos = await MapListToDtos(items, ct);
        return Ok(new PaginatedResponse<GalleryDto>(dtos, totalCount, page, perPage));
    }

    [HttpPost("find")]
    public async Task<ActionResult<PaginatedResponse<GalleryDto>>> FindPost([FromBody] FilteredQueryRequest<GalleryFilter> req, CancellationToken ct)
    {
        var findFilter = req.FindFilter ?? new FindFilter();
        var filter = req.ObjectFilter ?? new GalleryFilter();
        var (items, totalCount) = await galleryRepo.FindAsync(filter, findFilter, ct);
        var dtos = await MapListToDtos(items, ct);
        return Ok(new PaginatedResponse<GalleryDto>(dtos, totalCount, findFilter.Page, findFilter.PerPage));
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<GalleryDto>> GetById(int id, CancellationToken ct)
    {
        var gallery = await galleryRepo.GetByIdWithRelationsAsync(id, ct);
        if (gallery == null) return NotFound();

        return Ok(await MapToDtoWithProvenanceAsync(gallery, ct));
    }

    [HttpGet("{id:int}/cover")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<IActionResult> GetCover(int id, [FromQuery] int? max, [FromQuery] string? v, CancellationToken ct)
    {
        var gallery = await db.Galleries.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (gallery == null) return NotFound();

        if (gallery.ImageBlobId != null)
            return Redirect(WithQuery($"/api/galleries/{id}/image", max, v));

        if (gallery.CoverImageId.HasValue)
            return Redirect(WithQuery($"/api/stream/image/{gallery.CoverImageId.Value}/thumbnail", max, v));

        var firstImageId = await db.Set<ImageGallery>()
            .Where(ig => ig.GalleryId == id)
            .OrderBy(ig => ig.ImageId)
            .Select(ig => (int?)ig.ImageId)
            .FirstOrDefaultAsync(ct);

        if (firstImageId.HasValue)
            return Redirect(WithQuery($"/api/stream/image/{firstImageId.Value}/thumbnail", max, v));

        var firstSceneId = await db.Set<SceneGallery>()
            .Where(sg => sg.GalleryId == id)
            .OrderBy(sg => sg.SceneId)
            .Select(sg => (int?)sg.SceneId)
            .FirstOrDefaultAsync(ct);

        return firstSceneId.HasValue
            ? Redirect(WithQuery($"/api/stream/scene/{firstSceneId.Value}/screenshot", null, v))
            : NotFound();
    }

    [HttpPost]
    [RequiresPermission(Permissions.GalleriesWrite)]
    public async Task<ActionResult<GalleryDto>> Create([FromBody] GalleryCreateDto dto, CancellationToken ct)
    {
        var gallery = new Gallery
        {
            Title = dto.Title, Code = dto.Code, Date = ParseDate(dto.Date),
            Details = dto.Details, Photographer = dto.Photographer,
            Organized = dto.Organized, StudioId = dto.StudioId
        };
        if (dto.Urls?.Count > 0) gallery.Urls = dto.Urls.Select(u => new GalleryUrl { Url = u }).ToList();
        if (dto.TagIds?.Count > 0) gallery.GalleryTags = dto.TagIds.Select(id => new GalleryTag { TagId = id }).ToList();
        if (dto.PerformerIds?.Count > 0) gallery.GalleryPerformers = dto.PerformerIds.Select(id => new GalleryPerformer { PerformerId = id }).ToList();
        if (dto.SceneIds?.Count > 0) gallery.SceneGalleries = dto.SceneIds.Select(id => new SceneGallery { SceneId = id }).ToList();

        gallery = await galleryRepo.AddAsync(gallery, ct);
        if (dto.CustomFields != null)
            await _customFields.SaveValuesAsync(CustomFieldEntityTypes.Gallery, gallery.Id, dto.CustomFields, ct);
        if (dto.Rating.HasValue)
            await engagementService.SetRatingAsync(AffinityHostType.Gallery, gallery.Id, dto.Rating, cancellationToken: ct);
        if (dto.TagIds?.Count > 0 && tagProvenanceService != null)
        {
            await tagProvenanceService.SyncTagSetAsync(AffinityHostType.Gallery, gallery.Id, [], dto.TagIds, cancellationToken: ct);
            await db.SaveChangesAsync(ct);
        }
        var result = await galleryRepo.GetByIdWithRelationsAsync(gallery.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = gallery.Id }, await MapToDtoWithProvenanceAsync(result!, ct));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<ActionResult<GalleryDto>> Update(int id, [FromBody] GalleryUpdateDto dto, CancellationToken ct)
    {
        var gallery = await galleryRepo.GetByIdWithRelationsAsync(id, ct);
        if (gallery == null) return NotFound();
        var previousTagIds = dto.TagIds != null ? gallery.GalleryTags.Select(galleryTag => galleryTag.TagId).ToArray() : [];

        if (dto.Title != null) gallery.Title = dto.Title;
        if (dto.Code != null) gallery.Code = dto.Code;
        if (dto.Date != null) gallery.Date = ParseDate(dto.Date);
        if (dto.Details != null) gallery.Details = dto.Details;
        if (dto.Photographer != null) gallery.Photographer = dto.Photographer;
        if (dto.Organized.HasValue) gallery.Organized = dto.Organized.Value;
        if (dto.StudioId.HasValue) gallery.StudioId = dto.StudioId;

        if (dto.Urls != null)
        {
            gallery.Urls.Clear();
            gallery.Urls = dto.Urls.Select(u => new GalleryUrl { Url = u, GalleryId = id }).ToList();
        }
        if (dto.TagIds != null)
        {
            gallery.GalleryTags.Clear();
            gallery.GalleryTags = dto.TagIds.Select(tid => new GalleryTag { TagId = tid, GalleryId = id }).ToList();
        }
        if (dto.PerformerIds != null)
        {
            gallery.GalleryPerformers.Clear();
            gallery.GalleryPerformers = dto.PerformerIds.Select(pid => new GalleryPerformer { PerformerId = pid, GalleryId = id }).ToList();
        }
        if (dto.SceneIds != null)
        {
            gallery.SceneGalleries.Clear();
            gallery.SceneGalleries = dto.SceneIds.Select(sid => new SceneGallery { SceneId = sid, GalleryId = id }).ToList();
        }
        if (dto.TagIds != null && tagProvenanceService != null)
        {
            await tagProvenanceService.SyncTagSetAsync(
                AffinityHostType.Gallery,
                id,
                previousTagIds,
                gallery.GalleryTags.Select(galleryTag => galleryTag.TagId).ToArray(),
                cancellationToken: ct);
        }

        await galleryRepo.UpdateAsync(gallery, ct);
        if (dto.CustomFields != null)
            await _customFields.SaveValuesAsync(CustomFieldEntityTypes.Gallery, id, dto.CustomFields, ct);
        if (dto.Rating.HasValue)
            await engagementService.SetRatingAsync(AffinityHostType.Gallery, id, dto.Rating, cancellationToken: ct);
        var updated = await galleryRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(await MapToDtoWithProvenanceAsync(updated!, ct));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.GalleriesDelete)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var g = await galleryRepo.GetByIdAsync(id, ct);
        if (g == null) return NotFound();
        if (tagProvenanceService != null)
            await tagProvenanceService.RemoveForHostAsync(AffinityHostType.Gallery, id, ct);
        await _customFields.DeleteValuesForEntityAsync(CustomFieldEntityTypes.Gallery, id, ct);
        await galleryRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    private async Task<GalleryDto> MapToDtoWithProvenanceAsync(Gallery gallery, CancellationToken cancellationToken = default)
    {
        var tagIds = gallery.GalleryTags
            .Where(galleryTag => galleryTag.Tag != null)
            .Select(galleryTag => galleryTag.Tag!.Id)
            .Distinct()
            .ToArray();
        var provenanceLookup = tagProvenanceService == null
            ? null
            : await tagProvenanceService.GetLookupAsync(AffinityHostType.Gallery, gallery.Id, tagIds, cancellationToken);

        var customFieldValues = await _customFields.GetValuesAsync(CustomFieldEntityTypes.Gallery, gallery.Id, cancellationToken);
        var relationshipCounts = await GetRelationshipCountsAsync([gallery.Id], cancellationToken);
        var fieldProvenance = fieldProvenanceService == null
            ? null
            : (await fieldProvenanceService.GetForHostAsync(AffinityHostType.Gallery, gallery.Id, cancellationToken)).ToList();
        return MapToDto(
            gallery,
            customFieldValues,
            GetRelationshipCount(relationshipCounts.ImageCounts, gallery.Id),
            GetRelationshipCount(relationshipCounts.SceneCounts, gallery.Id),
            provenanceLookup,
            fieldProvenance);
    }

    private GalleryDto MapToDto(Gallery g, Dictionary<string, object>? customFieldValues = null, int? imageCount = null, int? sceneCount = null, IReadOnlyDictionary<int, List<TagProvenanceDto>>? provenanceLookup = null, List<FieldProvenanceDto>? fieldProvenance = null) => new(
        g.Id, g.Title, g.Code, g.Date?.ToString("yyyy-MM-dd"), g.Details, g.Photographer,
        g.Organized, g.StudioId, g.Studio?.Name,
        g.Urls.Select(u => u.Url).ToList(),
        g.GalleryTags.Where(gt => gt.Tag != null).Select(gt => TagDtoMapping.MapTagDto(gt.Tag!, GetTagProvenance(provenanceLookup, gt.Tag!.Id))).ToList(),
        g.GalleryPerformers.Where(gp => gp.Performer != null).Select(gp => new PerformerSummaryDto(gp.Performer!.Id, gp.Performer.Name, gp.Performer.Disambiguation, gp.Performer.Gender?.ToString(), gp.Performer.Birthdate?.ToString("yyyy-MM-dd"), gp.Performer.Favorite, EntityImageUrls.PerformerOrNull(ControllerContext.HttpContext, gp.Performer!))).ToList(),
        imageCount ?? g.ImageCount,
        sceneCount ?? g.SceneCount,
        g.SceneGalleries?.Select(sg => sg.SceneId).ToList() ?? [],
        g.Folder?.Path,
        g.Files?.Select(f => new GalleryFileInfoDto(f.Id, f.Path, f.Size, f.ModTime.ToString("o"),
            f.Fingerprints?.Select(fp => new FingerprintDto(fp.Type, fp.Value)).ToList() ?? [])).ToList() ?? [],
        customFieldValues,
        g.CreatedAt.ToString("o"), g.UpdatedAt.ToString("o"),
        ResolveCoverPath(g, imageCount, sceneCount),
        g.CoverImageId,
        fieldProvenance
    );

    /// <summary>Resolve cover image URL through the unified gallery cover endpoint.</summary>
    private string? ResolveCoverPath(Gallery g, int? imageCount = null, int? sceneCount = null)
    {
        var resolvedImageCount = imageCount ?? g.ImageCount;
        var resolvedSceneCount = sceneCount ?? g.SceneCount;
        if (g.ImageBlobId != null || g.CoverImageId != null || resolvedImageCount > 0 || resolvedSceneCount > 0) return EntityImageUrls.GalleryCover(ControllerContext.HttpContext, g.Id, g.UpdatedAt);
        return null;
    }

    private static string WithQuery(string path, int? max, string? version)
    {
        var query = new List<string>();
        if (max.HasValue && max.Value > 0) query.Add($"max={max.Value}");
        if (!string.IsNullOrWhiteSpace(version)) query.Add($"v={Uri.EscapeDataString(version)}");
        return query.Count == 0 ? path : $"{path}?{string.Join("&", query)}";
    }

    private async Task<List<GalleryDto>> MapListToDtos(IReadOnlyList<Gallery> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];
        var ids = items.Select(item => item.Id).ToArray();
        var customFieldValues = await _customFields.GetValuesAsync(CustomFieldEntityTypes.Gallery, ids, ct);
        var relationshipCounts = await GetRelationshipCountsAsync(ids, ct);
        return items.Select(g => MapToDto(
            g,
            GetCustomFields(customFieldValues, g.Id),
            GetRelationshipCount(relationshipCounts.ImageCounts, g.Id),
            GetRelationshipCount(relationshipCounts.SceneCounts, g.Id))).ToList();
    }

    private async Task<GalleryRelationshipCounts> GetRelationshipCountsAsync(IReadOnlyCollection<int> galleryIds, CancellationToken ct)
    {
        if (galleryIds.Count == 0)
            return new GalleryRelationshipCounts(new Dictionary<int, int>(), new Dictionary<int, int>());

        var imageCounts = await db.Set<ImageGallery>()
            .AsNoTracking()
            .Where(imageGallery => galleryIds.Contains(imageGallery.GalleryId))
            .GroupBy(imageGallery => imageGallery.GalleryId)
            .Select(group => new { GalleryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.GalleryId, item => item.Count, ct);

        var sceneCounts = await db.Set<SceneGallery>()
            .AsNoTracking()
            .Where(sceneGallery => galleryIds.Contains(sceneGallery.GalleryId))
            .GroupBy(sceneGallery => sceneGallery.GalleryId)
            .Select(group => new { GalleryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.GalleryId, item => item.Count, ct);

        return new GalleryRelationshipCounts(imageCounts, sceneCounts);
    }

    private static int GetRelationshipCount(IReadOnlyDictionary<int, int> counts, int galleryId)
        => counts.TryGetValue(galleryId, out var count) ? count : 0;

    private static Dictionary<string, object>? GetCustomFields(IReadOnlyDictionary<int, Dictionary<string, object>> lookup, int id)
        => lookup.TryGetValue(id, out var values) && values.Count > 0 ? values : null;

    private static List<TagProvenanceDto> GetTagProvenance(IReadOnlyDictionary<int, List<TagProvenanceDto>>? provenanceLookup, int tagId)
        => provenanceLookup != null && provenanceLookup.TryGetValue(tagId, out var provenance) ? provenance : [];

    private static DateOnly? ParseDate(string? date) => DateOnly.TryParse(date, out var d) ? d : null;

    // ===== Image Management =====

    [HttpPost("{id:int}/images")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<IActionResult> AddImages(int id, [FromBody] GalleryAddImagesDto dto, CancellationToken ct)
    {
        var gallery = await db.Galleries.Include(g => g.ImageGalleries).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (gallery == null) return NotFound();

        var existing = gallery.ImageGalleries.Select(ig => ig.ImageId).ToHashSet();
        foreach (var imageId in dto.ImageIds.Where(iid => !existing.Contains(iid)))
            gallery.ImageGalleries.Add(new ImageGallery { ImageId = imageId, GalleryId = id });

        await db.SaveChangesAsync(ct);
        return Ok(new { added = dto.ImageIds.Count });
    }

    [HttpDelete("{id:int}/images")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<IActionResult> RemoveImages(int id, [FromBody] GalleryRemoveImagesDto dto, CancellationToken ct)
    {
        var toRemove = await db.Set<ImageGallery>()
            .Where(ig => ig.GalleryId == id && dto.ImageIds.Contains(ig.ImageId))
            .ToListAsync(ct);

        db.Set<ImageGallery>().RemoveRange(toRemove);
        await db.SaveChangesAsync(ct);
        return Ok(new { removed = toRemove.Count });
    }

    // ===== Chapters =====

    [HttpGet("{id:int}/chapters")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<List<GalleryChapterDto>>> GetChapters(int id, CancellationToken ct)
    {
        var chapters = await db.GalleryChapters
            .Where(c => c.GalleryId == id)
            .OrderBy(c => c.ImageIndex)
            .Select(c => new GalleryChapterDto(c.Id, c.Title, c.ImageIndex, c.GalleryId, c.CreatedAt.ToString("o"), c.UpdatedAt.ToString("o")))
            .ToListAsync(ct);

        return Ok(chapters);
    }

    [HttpPost("{id:int}/chapters")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite)]
    public async Task<ActionResult<GalleryChapterDto>> CreateChapter(int id, [FromBody] GalleryChapterCreateDto dto, CancellationToken ct)
    {
        var gallery = await db.Galleries.FindAsync([id], ct);
        if (gallery == null) return NotFound();

        var chapter = new GalleryChapter { Title = dto.Title, ImageIndex = dto.ImageIndex, GalleryId = id };
        db.GalleryChapters.Add(chapter);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetChapters), new { id }, new GalleryChapterDto(chapter.Id, chapter.Title, chapter.ImageIndex, chapter.GalleryId, chapter.CreatedAt.ToString("o"), chapter.UpdatedAt.ToString("o")));
    }

    [HttpPut("{galleryId:int}/chapters/{chapterId:int}")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite, RouteValueName = "galleryId")]
    public async Task<ActionResult<GalleryChapterDto>> UpdateChapter(int galleryId, int chapterId, [FromBody] GalleryChapterUpdateDto dto, CancellationToken ct)
    {
        var chapter = await db.GalleryChapters.FirstOrDefaultAsync(c => c.Id == chapterId && c.GalleryId == galleryId, ct);
        if (chapter == null) return NotFound();

        if (dto.Title != null) chapter.Title = dto.Title;
        if (dto.ImageIndex.HasValue) chapter.ImageIndex = dto.ImageIndex.Value;
        await db.SaveChangesAsync(ct);
        return Ok(new GalleryChapterDto(chapter.Id, chapter.Title, chapter.ImageIndex, chapter.GalleryId, chapter.CreatedAt.ToString("o"), chapter.UpdatedAt.ToString("o")));
    }

    [HttpDelete("{galleryId:int}/chapters/{chapterId:int}")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite, RouteValueName = "galleryId")]
    public async Task<IActionResult> DeleteChapter(int galleryId, int chapterId, CancellationToken ct)
    {
        var chapter = await db.GalleryChapters.FirstOrDefaultAsync(c => c.Id == chapterId && c.GalleryId == galleryId, ct);
        if (chapter == null) return NotFound();
        db.GalleryChapters.Remove(chapter);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ===== Bulk Operations =====

    [HttpPost("bulk")]
    [RequiresPermission(Permissions.GalleriesWrite)]
    [RequiresEntityAccess(EntityKinds.Gallery, Permissions.GalleriesWrite, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkGalleryUpdateDto dto, CancellationToken ct)
    {
        var galleries = await db.Galleries
            .Include(g => g.GalleryTags)
            .Include(g => g.GalleryPerformers)
            .AsSplitQuery()
            .Where(g => dto.Ids.Contains(g.Id))
            .ToListAsync(ct);
        var clearFields = dto.ClearFields?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (var gallery in galleries)
        {
            if (clearFields.Contains("studioId")) gallery.StudioId = null;
            if (clearFields.Contains("date")) gallery.Date = null;
            if (clearFields.Contains("code")) gallery.Code = null;
            if (clearFields.Contains("details")) gallery.Details = null;
            if (clearFields.Contains("photographer")) gallery.Photographer = null;
            if (dto.Organized.HasValue) gallery.Organized = dto.Organized.Value;
            if (dto.StudioId.HasValue) gallery.StudioId = dto.StudioId;
            if (dto.Date != null) gallery.Date = ParseDate(dto.Date);
            if (dto.Code != null) gallery.Code = dto.Code;
            if (dto.Details != null) gallery.Details = dto.Details;
            if (dto.Photographer != null) gallery.Photographer = dto.Photographer;

            if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Set)
            {
                gallery.GalleryTags.Clear();
                gallery.GalleryTags = dto.TagIds.Select(tid => new GalleryTag { TagId = tid, GalleryId = gallery.Id }).ToList();
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Add)
            {
                var existing = gallery.GalleryTags.Select(gt => gt.TagId).ToHashSet();
                foreach (var tid in dto.TagIds.Where(t => !existing.Contains(t)))
                    gallery.GalleryTags.Add(new GalleryTag { TagId = tid, GalleryId = gallery.Id });
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Remove)
            {
                gallery.GalleryTags = gallery.GalleryTags.Where(gt => !dto.TagIds.Contains(gt.TagId)).ToList();
            }

            if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Set)
            {
                gallery.GalleryPerformers.Clear();
                gallery.GalleryPerformers = dto.PerformerIds.Select(pid => new GalleryPerformer { PerformerId = pid, GalleryId = gallery.Id }).ToList();
            }
            else if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Add)
            {
                var existing = gallery.GalleryPerformers.Select(gp => gp.PerformerId).ToHashSet();
                foreach (var pid in dto.PerformerIds.Where(p => !existing.Contains(p)))
                    gallery.GalleryPerformers.Add(new GalleryPerformer { PerformerId = pid, GalleryId = gallery.Id });
            }
            else if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Remove)
            {
                gallery.GalleryPerformers = gallery.GalleryPerformers.Where(gp => !dto.PerformerIds.Contains(gp.PerformerId)).ToList();
            }
        }

        await db.SaveChangesAsync(ct);
        if (dto.Rating.HasValue)
        {
            foreach (var gallery in galleries)
                await engagementService.SetRatingAsync(AffinityHostType.Gallery, gallery.Id, dto.Rating, cancellationToken: ct);
        }
        return Ok(new { updated = galleries.Count });
    }
}
