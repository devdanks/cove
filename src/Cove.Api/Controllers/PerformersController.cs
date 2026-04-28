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
[RequiresPermission(Permissions.PerformersRead)]
public class PerformersController(IPerformerRepository performerRepo, MetadataServerService metadataServerService, PerformerScrapeService performerScrapeService, Data.CoveContext db, IEntityIdentifierService entityIdentifiers) : ControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<PaginatedResponse<PerformerDto>>> Find(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int perPage = 25,
        [FromQuery] string? sort = null, [FromQuery] string? direction = null,
        [FromQuery] int? seed = null,
        [FromQuery] string? name = null, [FromQuery] bool? favorite = null,
        [FromQuery] int? rating = null, [FromQuery] string? tagIds = null,
        [FromQuery] int? studioId = null,
        CancellationToken ct = default)
    {
        var filter = new PerformerFilter { Name = name, Favorite = favorite, Rating = rating, TagIds = ParseIntList(tagIds), StudioId = studioId };
        var findFilter = new FindFilter
        {
            Q = q, Page = page, PerPage = perPage, Sort = sort,
            Direction = direction == "desc" ? SortDirection.Desc : SortDirection.Asc,
            Seed = seed,
        };

        var (items, totalCount) = await performerRepo.FindAsync(filter, findFilter, ct);
        var dtos = items.Select(p => MapToDto(p)).ToList();
        return Ok(new PaginatedResponse<PerformerDto>(dtos, totalCount, page, perPage));
    }

    [HttpPost("find")]
    public async Task<ActionResult<PaginatedResponse<PerformerDto>>> FindPost([FromBody] FilteredQueryRequest<PerformerFilter> req, CancellationToken ct)
    {
        var findFilter = req.FindFilter ?? new FindFilter();
        var filter = req.ObjectFilter ?? new PerformerFilter();
        var (items, totalCount) = await performerRepo.FindAsync(filter, findFilter, ct);
        var dtos = items.Select(p => MapToDto(p)).ToList();
        return Ok(new PaginatedResponse<PerformerDto>(dtos, totalCount, findFilter.Page, findFilter.PerPage));
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<PerformerDto>> GetById(int id, CancellationToken ct)
    {
        var performer = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (performer == null) return NotFound();
        return Ok(MapToDto(performer));
    }

    [HttpPost]
    [RequiresPermission(Permissions.PerformersWrite)]
    public async Task<ActionResult<PerformerDto>> Create([FromBody] PerformerCreateDto dto, CancellationToken ct)
    {
        var performer = new Performer
        {
            Name = dto.Name, Disambiguation = dto.Disambiguation,
            Gender = ParseEnum<GenderEnum>(dto.Gender), Birthdate = ParseDate(dto.Birthdate),
            DeathDate = ParseDate(dto.DeathDate), Ethnicity = dto.Ethnicity, Country = dto.Country,
            EyeColor = dto.EyeColor, HairColor = dto.HairColor, HeightCm = dto.HeightCm,
            Weight = dto.Weight, Measurements = dto.Measurements, FakeTits = dto.FakeTits,
            PenisLength = dto.PenisLength, Circumcised = ParseEnum<CircumcisedEnum>(dto.Circumcised),
            CareerStart = ParseDate(dto.CareerStart), CareerEnd = ParseDate(dto.CareerEnd),
            Tattoos = dto.Tattoos, Piercings = dto.Piercings,
            Favorite = dto.Favorite, Rating = dto.Rating, Details = dto.Details,
            IgnoreAutoTag = dto.IgnoreAutoTag
        };
        if (dto.Urls?.Count > 0) performer.Urls = dto.Urls.Select(u => new PerformerUrl { Url = u }).ToList();
        if (dto.Aliases?.Count > 0) performer.Aliases = dto.Aliases.Select(a => new PerformerAlias { Alias = a }).ToList();
        if (dto.TagIds?.Count > 0) performer.PerformerTags = dto.TagIds.Select(id => new PerformerTag { TagId = id }).ToList();

        performer = await performerRepo.AddAsync(performer, ct);
        if (dto.Urls?.Count > 0)
            await entityIdentifiers.SyncAsync(EntityKinds.Performer, performer.Id, IdentifierSchemes.Url, dto.Urls, null, ct);
        if (dto.Aliases?.Count > 0)
            await entityIdentifiers.SyncAsync(EntityKinds.Performer, performer.Id, IdentifierSchemes.Alias, dto.Aliases, null, ct);
        var result = await performerRepo.GetByIdWithRelationsAsync(performer.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = performer.Id }, MapToDto(result!));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<ActionResult<PerformerDto>> Update(int id, [FromBody] PerformerUpdateDto dto, CancellationToken ct)
    {
        var p = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (p == null) return NotFound();

        if (dto.Name != null) p.Name = dto.Name;
        if (dto.Disambiguation != null) p.Disambiguation = dto.Disambiguation;
        if (dto.Gender != null) p.Gender = ParseEnum<GenderEnum>(dto.Gender);
        if (dto.Birthdate != null) p.Birthdate = ParseDate(dto.Birthdate);
        if (dto.DeathDate != null) p.DeathDate = ParseDate(dto.DeathDate);
        if (dto.Ethnicity != null) p.Ethnicity = dto.Ethnicity;
        if (dto.Country != null) p.Country = dto.Country;
        if (dto.EyeColor != null) p.EyeColor = dto.EyeColor;
        if (dto.HairColor != null) p.HairColor = dto.HairColor;
        if (dto.HeightCm.HasValue) p.HeightCm = dto.HeightCm;
        if (dto.Weight.HasValue) p.Weight = dto.Weight;
        if (dto.Measurements != null) p.Measurements = dto.Measurements;
        if (dto.FakeTits != null) p.FakeTits = dto.FakeTits;
        if (dto.PenisLength.HasValue) p.PenisLength = dto.PenisLength;
        if (dto.Circumcised != null) p.Circumcised = ParseEnum<CircumcisedEnum>(dto.Circumcised);
        if (dto.CareerStart != null) p.CareerStart = ParseDate(dto.CareerStart);
        if (dto.CareerEnd != null) p.CareerEnd = ParseDate(dto.CareerEnd);
        if (dto.Tattoos != null) p.Tattoos = dto.Tattoos;
        if (dto.Piercings != null) p.Piercings = dto.Piercings;
        if (dto.Favorite.HasValue) p.Favorite = dto.Favorite.Value;
        if (dto.Rating.HasValue) p.Rating = dto.Rating;
        if (dto.Details != null) p.Details = dto.Details;
        if (dto.IgnoreAutoTag.HasValue) p.IgnoreAutoTag = dto.IgnoreAutoTag.Value;

        if (dto.Urls != null)
        {
            p.Urls.Clear();
            p.Urls = dto.Urls.Select(u => new PerformerUrl { Url = u, PerformerId = id }).ToList();
        }
        if (dto.Aliases != null)
        {
            p.Aliases.Clear();
            p.Aliases = dto.Aliases.Select(a => new PerformerAlias { Alias = a, PerformerId = id }).ToList();
        }
        if (dto.TagIds != null)
        {
            p.PerformerTags.Clear();
            p.PerformerTags = dto.TagIds.Select(tid => new PerformerTag { TagId = tid, PerformerId = id }).ToList();
        }
        if (dto.CustomFields != null) p.CustomFields = dto.CustomFields;

        await performerRepo.UpdateAsync(p, ct);
        if (dto.Urls != null)
            await entityIdentifiers.SyncAsync(EntityKinds.Performer, id, IdentifierSchemes.Url, dto.Urls, null, ct);
        if (dto.Aliases != null)
            await entityIdentifiers.SyncAsync(EntityKinds.Performer, id, IdentifierSchemes.Alias, dto.Aliases, null, ct);
        var updated = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(MapToDto(updated!));
    }

    [HttpPost("{id:int}/scrape-url")]
    [RequiresPermission(Permissions.PerformersScrape, Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<ActionResult<PerformerDto>> ScrapeUrl(int id, [FromBody] PerformerScrapeUrlRequestDto dto, CancellationToken ct)
    {
        return await Scrape(id, new PerformerScrapeRequestDto("url", null, dto.Url, null, dto.CreateMissingTags), ct);
    }

    [HttpPost("{id:int}/scrape")]
    [RequiresPermission(Permissions.PerformersScrape, Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<ActionResult<PerformerDto>> Scrape(int id, [FromBody] PerformerScrapeRequestDto dto, CancellationToken ct)
    {
        var performer = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (performer == null)
            return NotFound();

        var resolvedScrape = await ResolveScrapeAsync(performer, dto, ct);
        if (resolvedScrape.ErrorResult != null)
            return resolvedScrape.ErrorResult;

        await performerScrapeService.ApplyAsync(performer, resolvedScrape.Scraped!, dto.CreateMissingTags, ct);
        await performerRepo.UpdateAsync(performer, ct);

        var updated = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(MapToDto(updated!));
    }

    [HttpPost("{id:int}/scrape-preview")]
    [RequiresPermission(Permissions.PerformersScrape)]
    public async Task<ActionResult<PerformerScrapePreviewDto>> PreviewScrape(int id, [FromBody] PerformerScrapeRequestDto dto, CancellationToken ct)
    {
        var performer = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (performer == null)
            return NotFound();

        var resolvedScrape = await ResolveScrapeAsync(performer, dto, ct);
        if (resolvedScrape.ErrorResult != null)
            return resolvedScrape.ErrorResult;

        return Ok(new PerformerScrapePreviewDto(resolvedScrape.Scraped!, resolvedScrape.InputKind!, resolvedScrape.SourceValue));
    }

    [HttpPost("{id:int}/apply-scraped")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<ActionResult<PerformerDto>> ApplyScraped(int id, [FromBody] PerformerApplyScrapedRequestDto dto, CancellationToken ct)
    {
        var performer = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (performer == null)
            return NotFound();

        await performerScrapeService.ApplyAsync(performer, dto.Scraped, dto.CreateMissingTags, ct);
        await performerRepo.UpdateAsync(performer, ct);

        var updated = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(MapToDto(updated!));
    }

    private async Task<ResolvedPerformerScrape> ResolveScrapeAsync(Performer performer, PerformerScrapeRequestDto dto, CancellationToken ct)
    {

        var inputKind = dto.InputKind?.Trim().ToLowerInvariant();
        if (inputKind is not ("url" or "name"))
            inputKind = !string.IsNullOrWhiteSpace(dto.Name) ? "name" : "url";

        ScrapedPerformerDto? scraped;
        string? sourceValue;
        if (inputKind == "name")
        {
            var name = string.IsNullOrWhiteSpace(dto.Name) ? performer.Name : dto.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return new ResolvedPerformerScrape(BadRequest(new { error = "A performer name is required before scraping." }), null, null, null);

            scraped = await performerScrapeService.ScrapeByNameAsync(name, dto.ScraperId, ct);
            sourceValue = name;
        }
        else
        {
            var url = string.IsNullOrWhiteSpace(dto.Url)
                ? performer.Urls.Select(item => item.Url).FirstOrDefault()
                : dto.Url.Trim();

            if (string.IsNullOrWhiteSpace(url))
                return new ResolvedPerformerScrape(BadRequest(new { error = "A performer URL is required before scraping." }), null, null, null);

            scraped = await performerScrapeService.ScrapeByUrlAsync(url, dto.ScraperId, ct);
            sourceValue = url;
        }

        if (scraped == null)
            return new ResolvedPerformerScrape(NotFound(new { error = "Scrape returned no performer metadata." }), null, null, null);

        return new ResolvedPerformerScrape(null, scraped, inputKind, sourceValue);
    }

    private sealed record ResolvedPerformerScrape(ActionResult? ErrorResult, ScrapedPerformerDto? Scraped, string? InputKind, string? SourceValue);

    [HttpGet("{id:int}/metadata-server/search")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<IReadOnlyList<MetadataServerPerformerMatchDto>>> SearchMetadataServer(int id, [FromQuery] string? term, [FromQuery] string? endpoint, CancellationToken ct)
    {
        var performer = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (performer == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(term))
        {
            var existingRemoteId = performer.RemoteIds.FirstOrDefault(remoteId => string.IsNullOrWhiteSpace(endpoint) || string.Equals(remoteId.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
            if (existingRemoteId != null)
            {
                var existing = await metadataServerService.GetPerformerMatchAsync(existingRemoteId.Endpoint, existingRemoteId.RemoteId, ct);
                if (existing != null)
                    return Ok(new[] { existing });
            }

            term = performer.Name;
        }

        return Ok(await metadataServerService.SearchPerformersAsync(term, endpoint, ct));
    }

    [HttpPost("metadata-server/find-by-ids")]
    public async Task<ActionResult<IReadOnlyList<MetadataServerPerformerMatchDto>>> FindMetadataServerPerformersByIds([FromBody] MetadataServerFindByIdsRequestDto dto, CancellationToken ct)
    {
        if (dto.Ids.Count == 0)
            return Ok(Array.Empty<MetadataServerPerformerMatchDto>());

        return Ok(await metadataServerService.GetPerformerMatchesAsync(dto.Endpoint, dto.Ids, ct));
    }

    [HttpPost("{id:int}/metadata-server/import")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<ActionResult<PerformerDto>> ImportFromMetadataServer(int id, [FromBody] MetadataServerPerformerImportRequestDto dto, CancellationToken ct)
    {
        var performer = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (performer == null)
            return NotFound();

        var imported = await metadataServerService.MergePerformerAsync(performer, dto.Endpoint, dto.PerformerId, ct);
        if (!imported)
            return NotFound();

        await performerRepo.UpdateAsync(performer, ct);
        var updated = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(MapToDto(updated!));
    }

    [HttpPost("{id:int}/metadata-server/submit-draft")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite)]
    public async Task<IActionResult> SubmitPerformerDraft(int id, [FromBody] MetadataServerEndpointDto dto, CancellationToken ct)
    {
        var performer = await performerRepo.GetByIdWithRelationsAsync(id, ct);
        if (performer == null) return NotFound();

        var draftId = await metadataServerService.SubmitPerformerDraftAsync(performer, dto.Endpoint, ct);
        return Ok(new { draftId });
    }

    [HttpPost("metadata-server/batch-tag")]
    [RequiresPermission(Permissions.PerformersWrite)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersWrite, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<ActionResult<object>> BatchTagFromMetadataServer([FromBody] MetadataServerPerformerBatchTagRequestDto dto, [FromServices] IJobService jobService, [FromServices] IServiceScopeFactory scopeFactory, [FromServices] IAuthorizationService authorizationService, [FromServices] ICurrentPrincipalAccessor principalAccessor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Endpoint))
            return BadRequest(new { message = "Endpoint is required" });

        var ids = await ResolveSelectedPerformerIdsAsync(dto, ct);
        if (ids.Count == 0)
            return BadRequest(new { message = "No performers selected for batch tagging" });

        var principal = principalAccessor.Current;
        if (principal == null)
            return Forbid();

        foreach (var id in ids)
        {
            var result = await authorizationService.AuthorizeAsync(
                principal,
                Permissions.PerformersWrite,
                new EntityRef(EntityKinds.Performer, id.ToString()),
                ct);

            if (!result.Allowed)
                return Forbid();
        }

        var jobId = jobService.Enqueue(
            "metadata-server:performers",
            $"Tagging {ids.Count} performers from {dto.Endpoint}",
            async (progress, jobCt) =>
            {
                using var scope = scopeFactory.CreateScope();
                var metadataServerService = scope.ServiceProvider.GetRequiredService<MetadataServerService>();
                await metadataServerService.BatchTagPerformersAsync(dto.Endpoint, ids, dto.RefreshAlreadyTagged, dto.ExcludeFields, progress, jobCt);
            });

        return Ok(new { jobId, itemCount = ids.Count });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.PerformersDelete)]
    [RequiresEntityAccess(EntityKinds.Performer, Permissions.PerformersDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var p = await performerRepo.GetByIdAsync(id, ct);
        if (p == null) return NotFound();
        await performerRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    private static PerformerDto MapToDto(Performer p, int? sceneCount = null, int? imageCount = null, int? galleryCount = null, int? groupCount = null) => new(
        p.Id, p.Name, p.Disambiguation, p.Gender?.ToString(),
        p.Birthdate?.ToString("yyyy-MM-dd"), p.DeathDate?.ToString("yyyy-MM-dd"),
        p.Ethnicity, p.Country, p.EyeColor, p.HairColor, p.HeightCm, p.Weight,
        p.Measurements, p.FakeTits, p.PenisLength, p.Circumcised?.ToString(),
        p.CareerStart?.ToString("yyyy-MM-dd"), p.CareerEnd?.ToString("yyyy-MM-dd"),
        p.Tattoos, p.Piercings, p.Favorite, p.Rating, p.Details, p.IgnoreAutoTag,
        p.Urls.Select(u => u.Url).ToList(),
        p.Aliases.Select(a => a.Alias).ToList(),
        p.PerformerTags.Where(pt => pt.Tag != null).Select(pt => new TagDto(pt.Tag!.Id, pt.Tag.Name, pt.Tag.Description, pt.Tag.Favorite, pt.Tag.IgnoreAutoTag, [])).ToList(),
        p.RemoteIds.Select(remoteId => new PerformerRemoteIdDto(remoteId.Endpoint, remoteId.RemoteId)).ToList(),
        sceneCount ?? p.SceneCount, imageCount ?? p.ImageCount, galleryCount ?? p.GalleryCount, groupCount ?? 0,
        p.ImageBlobId != null ? EntityImageUrls.Performer(p.Id, p.UpdatedAt) : null,
        p.CustomFields,
        p.CreatedAt.ToString("o"), p.UpdatedAt.ToString("o")
    );

    private static DateOnly? ParseDate(string? date) => DateOnly.TryParse(date, out var d) ? d : null;
    private static T? ParseEnum<T>(string? value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var e) ? e : null;
    private static List<int>? ParseIntList(string? csv) => string.IsNullOrEmpty(csv) ? null : csv.Split(',').Select(int.Parse).ToList();

    private async Task<List<int>> ResolveSelectedPerformerIdsAsync(MetadataServerPerformerBatchTagRequestDto dto, CancellationToken ct)
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
            var (items, totalCount) = await performerRepo.FindAsync(dto.Filter, new FindFilter
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

    // ===== Bulk Operations =====

    [HttpPost("bulk")]
    [RequiresPermission(Permissions.PerformersWrite)]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkPerformerUpdateDto dto, CancellationToken ct)
    {
        var performers = await db.Performers
            .Include(p => p.PerformerTags)
            .Where(p => dto.Ids.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var p in performers)
        {
            if (dto.Rating.HasValue) p.Rating = dto.Rating;
            if (dto.Favorite.HasValue) p.Favorite = dto.Favorite.Value;

            if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Set)
            {
                p.PerformerTags.Clear();
                p.PerformerTags = dto.TagIds.Select(tid => new PerformerTag { TagId = tid, PerformerId = p.Id }).ToList();
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Add)
            {
                var existing = p.PerformerTags.Select(pt => pt.TagId).ToHashSet();
                foreach (var tid in dto.TagIds.Where(t => !existing.Contains(t)))
                    p.PerformerTags.Add(new PerformerTag { TagId = tid, PerformerId = p.Id });
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Remove)
            {
                p.PerformerTags = p.PerformerTags.Where(pt => !dto.TagIds.Contains(pt.TagId)).ToList();
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { updated = performers.Count });
    }

    // ===== Merge =====

    [HttpPost("merge")]
    [RequiresPermission(Permissions.PerformersWrite)]
    public async Task<ActionResult<PerformerDto>> MergePerformers([FromBody] PerformerMergeDto dto, CancellationToken ct)
    {
        var target = await performerRepo.GetByIdWithRelationsAsync(dto.TargetId, ct);
        if (target == null) return NotFound("Target performer not found");

        var sources = await db.Performers
            .Include(p => p.Aliases)
            .Include(p => p.Urls)
            .Include(p => p.ScenePerformers)
            .Include(p => p.ImagePerformers)
            .Include(p => p.GalleryPerformers)
            .Where(p => dto.SourceIds.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var source in sources)
        {
            // Move scene associations
            foreach (var sp in source.ScenePerformers)
                if (!target.ScenePerformers.Any(t => t.SceneId == sp.SceneId))
                    target.ScenePerformers.Add(new ScenePerformer { SceneId = sp.SceneId, PerformerId = target.Id });
            // Move image associations
            foreach (var ip in source.ImagePerformers)
                if (!target.ImagePerformers.Any(t => t.ImageId == ip.ImageId))
                    target.ImagePerformers.Add(new ImagePerformer { ImageId = ip.ImageId, PerformerId = target.Id });
            // Add source name as alias
            if (!target.Aliases.Any(a => a.Alias == source.Name))
                target.Aliases.Add(new PerformerAlias { Alias = source.Name, PerformerId = target.Id });
            // Delete source
            db.Performers.Remove(source);
        }

        await db.SaveChangesAsync(ct);
        var result = await performerRepo.GetByIdWithRelationsAsync(target.Id, ct);
        return Ok(MapToDto(result!));
    }
}
