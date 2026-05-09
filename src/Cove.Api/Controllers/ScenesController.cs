using System.Text.Json;
using System.Numerics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.Common;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequiresPermission(Permissions.ScenesRead)]
public class ScenesController(ISceneRepository sceneRepo, Data.CoveContext db, MetadataServerService metadataServerService, IThumbnailService thumbnailService, IScanService scanService, IMemoryCache memoryCache, IBlobService blobService, IStreamService streamService, IEntityIdentifierService entityIdentifiers, IUserEngagementService engagementService, CustomFieldService customFields, ITagProvenanceService? tagProvenanceService = null, ICurrentPrincipalAccessor? principalAccessor = null) : ControllerBase
{
    private bool CanReadFiles => principalAccessor?.Current?.Has(Permissions.FilesRead) == true;
    private bool HasUserScopedEngagement => principalAccessor?.Current?.UserId != null;
    private static string GetVisibleBasename(string path, string basename) => string.IsNullOrWhiteSpace(basename) ? System.IO.Path.GetFileName(path) : basename;

    [HttpGet]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<PaginatedResponse<SceneDto>>> Find(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int perPage = 25,
        [FromQuery] string? sort = null, [FromQuery] string? direction = null,
        [FromQuery] int? seed = null,
        [FromQuery] string? title = null, [FromQuery] int? rating = null,
        [FromQuery] bool? organized = null, [FromQuery] int? studioId = null,
        [FromQuery] int? groupId = null, [FromQuery] int? galleryId = null, [FromQuery] string? tagIds = null, [FromQuery] string? performerIds = null,
        CancellationToken ct = default)
    {
        var filter = new SceneFilter
        {
            Title = title, Rating = rating, Organized = organized, StudioId = studioId, GroupId = groupId, GalleryId = galleryId,
            TagIds = QueryParsing.ParseIntList(tagIds)?.ToList(), PerformerIds = QueryParsing.ParseIntList(performerIds)?.ToList()
        };
        var findFilter = new FindFilter
        {
            Q = q, Page = page, PerPage = perPage, Sort = sort,
            Direction = direction == "desc" ? Core.Enums.SortDirection.Desc : Core.Enums.SortDirection.Asc,
            Seed = seed,
        };

        var (items, totalCount) = await sceneRepo.FindAsync(filter, findFilter, ct);
        var engagement = await engagementService.GetSceneSnapshotsAsync(items.Select(scene => scene.Id), ct);
        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Scene, items.Select(scene => scene.Id), ct);
        var dtos = items.Select(scene => MapListToDto(scene, GetCustomFields(customFieldValues, scene.Id), engagement.GetValueOrDefault(scene.Id), HasUserScopedEngagement)).ToList();
        return Ok(new PaginatedResponse<SceneDto>(dtos, totalCount, page, perPage));
    }

    /// <summary>POST-based filtered query supporting advanced criteria (JSON body).</summary>
    [HttpPost("find")]
    public async Task<IActionResult> FindPost([FromBody] FilteredQueryRequest<SceneFilter> req, CancellationToken ct)
    {
        var cacheKey = $"scenes_find_{JsonSerializer.Serialize(req)}";
        if (memoryCache.TryGetValue(cacheKey, out PaginatedResponse<SceneDto>? cachedResult) && cachedResult != null)
        {
            return Ok(cachedResult);
        }

        var findFilter = req.FindFilter ?? new FindFilter();
        var filter = req.ObjectFilter ?? new SceneFilter();
        var (items, totalCount) = await sceneRepo.FindAsync(filter, findFilter, ct);
        var engagement = await engagementService.GetSceneSnapshotsAsync(items.Select(scene => scene.Id), ct);
        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Scene, items.Select(scene => scene.Id), ct);
        var dtos = items.Select(scene => MapListToDto(scene, GetCustomFields(customFieldValues, scene.Id), engagement.GetValueOrDefault(scene.Id), HasUserScopedEngagement)).ToList();
        var result = new PaginatedResponse<SceneDto>(dtos, totalCount, findFilter.Page, findFilter.PerPage);

        memoryCache.Set(cacheKey, result, TimeSpan.FromSeconds(1));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = "ShortCache")]
    public async Task<ActionResult<SceneDto>> GetById(int id, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        if (scene == null) return NotFound();
        var engagement = (await engagementService.GetSceneSnapshotsAsync([id], ct)).GetValueOrDefault(id);
        return Ok(await MapToDtoWithProvenanceAsync(scene, engagement, HasUserScopedEngagement, ct));
    }

    [HttpPost]
    [RequiresPermission(Permissions.ScenesWrite)]
    public async Task<ActionResult<SceneDto>> Create([FromBody] SceneCreateDto dto, CancellationToken ct)
    {
        var scene = new Scene
        {
            Title = dto.Title, Code = dto.Code, Details = dto.Details, Director = dto.Director,
            Date = ParseDate(dto.Date), Organized = dto.Organized, StudioId = dto.StudioId,
            Captions = dto.Captions, InteractiveSpeed = dto.InteractiveSpeed,
        };
        if (dto.Urls?.Count > 0)
            scene.Urls = dto.Urls.Select(u => new SceneUrl { Url = u }).ToList();
        if (dto.TagIds?.Count > 0)
            scene.SceneTags = dto.TagIds.Select(id => new SceneTag { TagId = id }).ToList();
        if (dto.PerformerIds?.Count > 0)
            scene.ScenePerformers = dto.PerformerIds.Select(id => new ScenePerformer { PerformerId = id }).ToList();
        if (dto.GalleryIds?.Count > 0)
            scene.SceneGalleries = dto.GalleryIds.Select(id => new SceneGallery { GalleryId = id }).ToList();
        if (dto.Groups?.Count > 0)
            scene.GroupItems = dto.Groups.Select(group => new GroupItem
            {
                GroupId = group.GroupId,
                OrderIndex = group.SceneIndex,
                Kind = GroupItemKind.Scene,
            }).ToList();

        scene = await sceneRepo.AddAsync(scene, ct);
        if (dto.CustomFields != null)
            await customFields.SaveValuesAsync(CustomFieldEntityTypes.Scene, scene.Id, dto.CustomFields, ct);
        if (dto.TagIds?.Count > 0 && tagProvenanceService != null)
        {
            await tagProvenanceService.SyncTagSetAsync(AffinityHostType.Scene, scene.Id, [], dto.TagIds, cancellationToken: ct);
            await db.SaveChangesAsync(ct);
        }
        if (dto.Urls?.Count > 0)
            await entityIdentifiers.SyncAsync(EntityKinds.Scene, scene.Id, IdentifierSchemes.Url, dto.Urls, null, ct);
        if (dto.Rating.HasValue)
            await engagementService.SetSceneRatingAsync(scene.Id, dto.Rating, cancellationToken: ct);

        var result = await sceneRepo.GetByIdWithRelationsAsync(scene.Id, ct);
        var engagement = (await engagementService.GetSceneSnapshotsAsync([scene.Id], ct)).GetValueOrDefault(scene.Id);
        return CreatedAtAction(nameof(GetById), new { id = scene.Id }, await MapToDtoWithProvenanceAsync(result!, engagement, HasUserScopedEngagement, ct));
    }

    [HttpPut("{id:int}")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<ActionResult<SceneDto>> Update(int id, [FromBody] SceneUpdateDto dto, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        if (scene == null) return NotFound();
        var previousTagIds = dto.TagIds != null ? scene.SceneTags.Select(sceneTag => sceneTag.TagId).ToArray() : [];

        if (dto.Title != null) scene.Title = dto.Title;
        if (dto.Code != null) scene.Code = dto.Code;
        if (dto.Details != null) scene.Details = dto.Details;
        if (dto.Director != null) scene.Director = dto.Director;
        if (dto.Date != null) scene.Date = ParseDate(dto.Date);
        if (dto.Organized.HasValue) scene.Organized = dto.Organized.Value;
        if (dto.StudioId.HasValue) scene.StudioId = dto.StudioId;
        if (dto.Captions != null) scene.Captions = dto.Captions;
        if (dto.InteractiveSpeed.HasValue) scene.InteractiveSpeed = dto.InteractiveSpeed;

        if (dto.Urls != null)
        {
            scene.Urls.Clear();
            scene.Urls = dto.Urls.Select(u => new SceneUrl { Url = u, SceneId = id }).ToList();
        }
        if (dto.TagIds != null)
        {
            scene.SceneTags.Clear();
            scene.SceneTags = dto.TagIds.Select(tid => new SceneTag { TagId = tid, SceneId = id }).ToList();
        }
        if (dto.PerformerIds != null)
        {
            scene.ScenePerformers.Clear();
            scene.ScenePerformers = dto.PerformerIds.Select(pid => new ScenePerformer { PerformerId = pid, SceneId = id }).ToList();
        }
        if (dto.GalleryIds != null)
        {
            scene.SceneGalleries.Clear();
            scene.SceneGalleries = dto.GalleryIds.Select(gid => new SceneGallery { GalleryId = gid, SceneId = id }).ToList();
        }
        if (dto.Groups != null)
        {
            ReplaceWholeSceneGroupItems(scene, dto.Groups);
        }
        if (dto.TagIds != null && tagProvenanceService != null)
        {
            await tagProvenanceService.SyncTagSetAsync(
                AffinityHostType.Scene,
                id,
                previousTagIds,
                scene.SceneTags.Select(sceneTag => sceneTag.TagId).ToArray(),
                cancellationToken: ct);
        }

        await sceneRepo.UpdateAsync(scene, ct);
        if (dto.CustomFields != null)
            await customFields.SaveValuesAsync(CustomFieldEntityTypes.Scene, id, dto.CustomFields, ct);
        if (dto.Urls != null)
            await entityIdentifiers.SyncAsync(EntityKinds.Scene, id, IdentifierSchemes.Url, dto.Urls, null, ct);
        if (dto.Rating.HasValue)
            await engagementService.SetSceneRatingAsync(id, dto.Rating, cancellationToken: ct);
        var updated = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        var engagement = (await engagementService.GetSceneSnapshotsAsync([id], ct)).GetValueOrDefault(id);
        return Ok(await MapToDtoWithProvenanceAsync(updated!, engagement, HasUserScopedEngagement, ct));
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission(Permissions.ScenesDelete)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesDelete)]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool deleteFile = false, CancellationToken ct = default)
    {
        var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        if (scene == null) return NotFound();
        if (deleteFile)
        {
            foreach (var file in scene.Files)
            {
                var path = file.Path;
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }
        if (tagProvenanceService != null)
            await tagProvenanceService.RemoveForHostAsync(AffinityHostType.Scene, id, ct);
        await customFields.DeleteValuesForEntityAsync(CustomFieldEntityTypes.Scene, id, ct);
        await sceneRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("destroy")]
    [RequiresPermission(Permissions.ScenesDelete)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesDelete, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<IActionResult> DestroyBatch([FromBody] BatchDeleteDto dto, CancellationToken ct)
    {
        var deletedCount = 0;
        var idsToDelete = dto.Ids.ToHashSet();
        var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in dto.Ids)
        {
            var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
            if (scene != null)
            {
                if (dto.DeleteFiles)
                {
                    foreach (var file in scene.Files)
                    {
                        var path = file.Path;
                        if (string.IsNullOrWhiteSpace(path) || !deletedPaths.Add(path))
                            continue;

                        var referencedByKeptScene = await db.Set<VideoFile>()
                            .AnyAsync(videoFile => videoFile.Path == path && videoFile.SceneId.HasValue && !idsToDelete.Contains(videoFile.SceneId.Value), ct);
                        if (!referencedByKeptScene && System.IO.File.Exists(path))
                            System.IO.File.Delete(path);
                    }
                }

                if (tagProvenanceService != null)
                    await tagProvenanceService.RemoveForHostAsync(AffinityHostType.Scene, id, ct);
                await customFields.DeleteValuesForEntityAsync(CustomFieldEntityTypes.Scene, id, ct);
                await sceneRepo.DeleteAsync(id, ct);
                deletedCount++;
            }
        }
        return Ok(new { deleted = deletedCount });
    }

    [HttpGet("{id:int}/metadata-server/search")]
    public async Task<ActionResult<IReadOnlyList<MetadataServerSceneMatchDto>>> SearchMetadataServer(int id, [FromQuery] string? term, [FromQuery] string? endpoint, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        if (scene == null) return NotFound();

        return Ok(await metadataServerService.SearchScenesAsync(scene, term, endpoint, ct));
    }

    [HttpPost("{id:int}/metadata-server/import")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<ActionResult<SceneDto>> ImportFromMetadataServer(int id, [FromBody] MetadataServerSceneImportRequestDto dto, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        if (scene == null) return NotFound();

        var imported = await metadataServerService.MergeSceneAsync(scene, dto.Endpoint, dto.SceneId, dto, ct);
        if (!imported) return NotFound();

        await db.SaveChangesAsync(ct);
        var updated = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        return Ok(await MapToDtoWithProvenanceAsync(updated!, cancellationToken: ct));
    }

    [HttpPost("{id:int}/metadata-server/submit-fingerprints")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> SubmitFingerprints(int id, [FromBody] MetadataServerEndpointDto dto, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        if (scene == null) return NotFound();

        await metadataServerService.SubmitFingerprintsAsync(scene, dto.Endpoint, ct);
        return Ok();
    }

    [HttpPost("{id:int}/metadata-server/submit-draft")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> SubmitSceneDraft(int id, [FromBody] MetadataServerEndpointDto dto, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdWithRelationsAsync(id, ct);
        if (scene == null) return NotFound();

        var draftId = await metadataServerService.SubmitSceneDraftAsync(scene, dto.Endpoint, ct);
        return Ok(new { draftId });
    }

    [HttpPost("{id:int}/cover/from-frame")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> SetCoverFromFrame(int id, [FromBody] GenerateScreenshotDto? dto, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdAsync(id, ct);
        if (scene == null) return NotFound();

        await thumbnailService.GenerateSceneThumbnailAsync(id, dto?.AtSeconds, ct);
        var screenshot = await streamService.GetSceneScreenshot(id, dto?.AtSeconds, ct);
        if (screenshot == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(scene.ImageBlobId))
            await blobService.DeleteBlobAsync(scene.ImageBlobId, ct);

        await using var screenshotStream = screenshot.Value.stream;
        scene.ImageBlobId = await blobService.StoreBlobAsync(screenshotStream, screenshot.Value.contentType, ct);
        await sceneRepo.UpdateAsync(scene, ct);

        return Ok(new { success = true });
    }

    private async Task<SceneDto> MapToDtoWithProvenanceAsync(Scene scene, UserEngagementSnapshot? engagement = null, bool preferUserSnapshot = false, CancellationToken cancellationToken = default)
    {
        var tagIds = scene.SceneTags
            .Where(sceneTag => sceneTag.Tag != null)
            .Select(sceneTag => sceneTag.Tag!.Id)
            .Distinct()
            .ToArray();
        var provenanceLookup = tagProvenanceService == null
            ? null
            : await tagProvenanceService.GetLookupAsync(AffinityHostType.Scene, scene.Id, tagIds, cancellationToken);
        var contextTagApplications = await LoadContextTagApplicationsAsync(scene.Id, cancellationToken);

        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Scene, scene.Id, cancellationToken);
        return MapToDto(scene, customFieldValues, engagement, preferUserSnapshot, provenanceLookup, contextTagApplications);
    }

    private SceneDto MapToDto(Scene s, Dictionary<string, object>? customFieldValues = null, UserEngagementSnapshot? engagement = null, bool preferUserSnapshot = false, IReadOnlyDictionary<int, List<TagProvenanceDto>>? provenanceLookup = null, List<TagApplicationDto>? contextTagApplications = null) => new(
        s.Id, s.Title, s.Code, s.Details, s.Director,
        s.Date?.ToString("yyyy-MM-dd"),
        s.Organized, s.StudioId, s.Studio?.Name,
        s.Captions, s.InteractiveSpeed,
        s.Urls.Select(u => u.Url).ToList(),
        s.SceneTags.Where(st => st.Tag != null).Select(st => MapTagDto(st.Tag!, GetTagProvenance(provenanceLookup, st.Tag!.Id))).ToList(),
        s.ScenePerformers.Where(sp => sp.Performer != null).Select(sp => new PerformerSummaryDto(sp.Performer!.Id, sp.Performer.Name, sp.Performer.Disambiguation, sp.Performer.Gender?.ToString(), sp.Performer.Birthdate?.ToString("yyyy-MM-dd"), sp.Performer.Favorite, sp.Performer.ImageBlobId != null ? EntityImageUrls.Performer(ControllerContext.HttpContext, sp.Performer.Id, sp.Performer.UpdatedAt) : null)).ToList(),
        s.Files.Select(f => new VideoFileDto(
            f.Id,
            CanReadFiles ? f.Path : string.Empty,
            GetVisibleBasename(f.Path, f.Basename),
            f.Format,
            f.Width,
            f.Height,
            f.Duration,
            f.VideoCodec,
            f.AudioCodec,
            f.FrameRate,
            f.BitRate,
            f.Size,
            f.Fingerprints.Select(fp => new FingerprintDto(fp.Type, fp.Value)).ToList(),
            f.Captions.Select(c => new CaptionDto(c.Id, c.LanguageCode, c.CaptionType, c.Filename)).ToList())).ToList(),
        MapWholeSceneGroups(s),
        s.SceneGalleries.Where(sg => sg.Gallery != null).Select(sg => new GallerySummaryDto(sg.Gallery!.Id, sg.Gallery.Title, sg.Gallery.Date?.ToString("yyyy-MM-dd"))).ToList(),
        s.RemoteIds.Select(remoteId => new SceneRemoteIdDto(remoteId.Endpoint, remoteId.RemoteId)).ToList(),
        customFieldValues,
        s.CreatedAt.ToString("o"), s.UpdatedAt.ToString("o"),
        contextTagApplications
    );

    private SceneDto MapListToDto(Scene s, Dictionary<string, object>? customFieldValues = null, UserEngagementSnapshot? engagement = null, bool preferUserSnapshot = false) => new(
        s.Id, s.Title, s.Code, s.Details, s.Director,
        s.Date?.ToString("yyyy-MM-dd"),
        s.Organized, s.StudioId, s.Studio?.Name,
        s.Captions, s.InteractiveSpeed,
        s.Urls.Select(u => u.Url).ToList(),
        s.SceneTags.Where(st => st.Tag != null).Select(st => MapTagDto(st.Tag!)).ToList(),
        s.ScenePerformers.Where(sp => sp.Performer != null).Select(sp => new PerformerSummaryDto(sp.Performer!.Id, sp.Performer.Name, sp.Performer.Disambiguation, sp.Performer.Gender?.ToString(), sp.Performer.Birthdate?.ToString("yyyy-MM-dd"), sp.Performer.Favorite, sp.Performer.ImageBlobId != null ? EntityImageUrls.Performer(ControllerContext.HttpContext, sp.Performer.Id, sp.Performer.UpdatedAt) : null)).ToList(),
        s.Files.Select(f => new VideoFileDto(
            f.Id,
            CanReadFiles ? f.Path : string.Empty,
            GetVisibleBasename(f.Path, f.Basename),
            f.Format,
            f.Width,
            f.Height,
            f.Duration,
            f.VideoCodec,
            f.AudioCodec,
            f.FrameRate,
            f.BitRate,
            f.Size,
            [],
            [])).ToList(),
        MapWholeSceneGroups(s),
        s.SceneGalleries.Where(sg => sg.Gallery != null).Select(sg => new GallerySummaryDto(sg.Gallery!.Id, sg.Gallery.Title, sg.Gallery.Date?.ToString("yyyy-MM-dd"))).ToList(),
        [],
        customFieldValues,
        s.CreatedAt.ToString("o"), s.UpdatedAt.ToString("o")
    );

    private static Dictionary<string, object>? GetCustomFields(IReadOnlyDictionary<int, Dictionary<string, object>> lookup, int id)
        => lookup.TryGetValue(id, out var values) && values.Count > 0 ? values : null;

    private async Task<List<TagApplicationDto>> LoadContextTagApplicationsAsync(int sceneId, CancellationToken ct)
    {
        var applications = await db.TagApplications
            .AsNoTracking()
            .Include(application => application.Tag).ThenInclude(tag => tag!.Aliases)
            .Include(application => application.Tag).ThenInclude(tag => tag!.TagGroup)
            .AsSplitQuery()
            .Where(application => application.HostType == AffinityHostType.Scene
                && application.HostId == sceneId
                && application.ContextType != null
                && application.ContextId != null)
            .OrderBy(application => application.ContextType)
            .ThenBy(application => application.ContextId)
            .ThenBy(application => application.Tag!.Name)
            .ToListAsync(ct);

        return applications.Select(TagApplicationsController.Map).ToList();
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

    private static List<TagProvenanceDto> GetTagProvenance(IReadOnlyDictionary<int, List<TagProvenanceDto>>? provenanceLookup, int tagId)
        => provenanceLookup != null && provenanceLookup.TryGetValue(tagId, out var provenance) ? provenance : [];

    // ===== Activity Tracking =====

    [HttpPost("{id:int}/play")]
    [RequiresPermission(Permissions.ScenesRead)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead)]
    public async Task<IActionResult> RecordPlay(int id, CancellationToken ct)
    {
        var snapshot = await engagementService.RecordScenePlayAsync(id, ct);
        if (snapshot == null) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:int}/play")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> DeletePlay(int id, CancellationToken ct)
    {
        var snapshot = await engagementService.DeleteScenePlayAsync(id, ct);
        if (snapshot == null) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/play/reset")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> ResetPlayCount(int id, CancellationToken ct)
    {
        var snapshot = await engagementService.ResetScenePlayAsync(id, ct);
        if (snapshot == null) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/like")]
    [RequiresPermission(Permissions.ScenesRead)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead)]
    public async Task<ActionResult<int>> IncrementLike(int id, CancellationToken ct)
    {
        var snapshot = await engagementService.IncrementSceneLikeAsync(id, ct);
        if (snapshot == null) return NotFound();
        return Ok(snapshot.LikeCount);
    }

    [HttpDelete("{id:int}/like")]
    [RequiresPermission(Permissions.ScenesRead)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead)]
    public async Task<IActionResult> DecrementLike(int id, CancellationToken ct)
    {
        var snapshot = await engagementService.DecrementSceneLikeAsync(id, ct);
        if (snapshot == null) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/like/reset")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> ResetLike(int id, CancellationToken ct)
    {
        var snapshot = await engagementService.ResetSceneLikeAsync(id, ct);
        if (snapshot == null) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:int}/history")]
    [RequiresPermission(Permissions.ScenesRead)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead)]
    public async Task<ActionResult<SceneHistoryDto>> GetHistory(int id, CancellationToken ct)
    {
        var history = await engagementService.GetSceneHistoryAsync(id, ct);
        return history is null ? NotFound() : Ok(history);
    }

    [HttpPost("{id:int}/activity/reset")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> ResetActivity(int id, CancellationToken ct)
    {
        var snapshot = await engagementService.ResetSceneActivityAsync(id, ct);
        if (snapshot == null) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/rating")]
    [RequiresPermission(Permissions.ScenesRead)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead)]
    public async Task<ActionResult<int?>> SetRating(int id, [FromBody] SceneRatingDto dto, CancellationToken ct)
    {
        var snapshot = await engagementService.SetSceneRatingAsync(id, dto.Value, dto.Aspect, ct);
        return snapshot is null ? NotFound() : Ok(snapshot.Rating);
    }

    [HttpGet("{id:int}/ratings")]
    [RequiresPermission(Permissions.ScenesRead)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead)]
    public async Task<ActionResult<EntityRatingsDto>> GetRatings(int id, CancellationToken ct)
    {
        var ratings = await engagementService.GetRatingsByAspectAsync(AffinityHostType.Scene, id, ct);
        return ratings is null ? NotFound() : Ok(new EntityRatingsDto(id, ratings));
    }

    [HttpDelete("{id:int}/rating")]
    [RequiresPermission(Permissions.ScenesRead)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesRead)]
    public async Task<IActionResult> ClearRating(int id, [FromQuery] string aspect = "overall", CancellationToken ct = default)
    {
        var snapshot = await engagementService.SetSceneRatingAsync(id, null, aspect, ct);
        return snapshot is null ? NotFound() : NoContent();
    }

    // ===== Scene Wall/Discovery =====

    [HttpGet("wall")]
    public async Task<ActionResult<List<SceneDto>>> SceneWall([FromQuery] string? q, [FromQuery] int count = 24, CancellationToken ct = default)
    {
        var query = db.Scenes
            .Include(s => s.Files).ThenInclude(f => f.Fingerprints)
            .Include(s => s.SceneTags).ThenInclude(st => st.Tag)
            .Include(s => s.ScenePerformers).ThenInclude(sp => sp.Performer)
            .Include(s => s.Studio)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(s => s.Title != null && EF.Functions.ILike(s.Title, $"%{q}%"));

        var scenes = await query.OrderBy(_ => EF.Functions.Random()).Take(count).ToListAsync(ct);
        var engagement = await engagementService.GetSceneSnapshotsAsync(scenes.Select(scene => scene.Id), ct);
        var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Scene, scenes.Select(scene => scene.Id), ct);
        return Ok(scenes.Select(scene => MapToDto(scene, GetCustomFields(customFieldValues, scene.Id), engagement.GetValueOrDefault(scene.Id), HasUserScopedEngagement)).ToList());
    }

    [HttpGet("duplicates")]
    public async Task<ActionResult<List<List<SceneDto>>>> FindDuplicates(
        [FromQuery] string? matchType = "fingerprint",
        [FromQuery] int distance = 0,
        [FromQuery] double? durationDiff = null,
        CancellationToken ct = default)
    {
        var groups = (matchType ?? "fingerprint").Trim().ToLowerInvariant() switch
        {
            "phash" or "visual" => await FindPhashDuplicateSceneIdsAsync(Math.Max(0, distance), durationDiff, ct),
            "title" => await FindTitleDuplicateSceneIdsAsync(ct),
            "remoteid" or "remote-id" or "remote_id" => await FindRemoteIdDuplicateSceneIdsAsync(ct),
            _ => await FindExactFingerprintDuplicateSceneIdsAsync(ct),
        };

        var result = new List<List<SceneDto>>();
        foreach (var sceneIds in groups)
        {
            var scenes = await db.Scenes
                .Include(s => s.Files).ThenInclude(f => f.Fingerprints)
                .Include(s => s.SceneTags).ThenInclude(st => st.Tag)
                .Include(s => s.ScenePerformers).ThenInclude(sp => sp.Performer)
                .Include(s => s.Studio)
                .Include(s => s.RemoteIds)
                .Where(s => sceneIds.Contains(s.Id))
                .AsNoTracking()
                .ToListAsync(ct);

            if (scenes.Count > 1)
            {
                var orderedScenes = scenes.OrderBy(scene => scene.Title ?? scene.Files.Select(file => file.Basename).FirstOrDefault() ?? string.Empty).ThenBy(scene => scene.Id).ToList();
                var customFieldValues = await customFields.GetValuesAsync(CustomFieldEntityTypes.Scene, orderedScenes.Select(scene => scene.Id), ct);
                result.Add(orderedScenes.Select(scene => MapToDto(scene, GetCustomFields(customFieldValues, scene.Id))).ToList());
            }
        }

        return Ok(result);
    }

    private async Task<List<List<int>>> FindExactFingerprintDuplicateSceneIdsAsync(CancellationToken ct)
    {
        var fingerprintRows = await db.Set<FileFingerprint>()
            .Where(fingerprint => (fingerprint.Type == "oshash" || fingerprint.Type == "md5") && fingerprint.Value != "")
            .Select(fingerprint => new { fingerprint.Type, fingerprint.Value, fingerprint.FileId })
            .AsNoTracking()
            .ToListAsync(ct);

        var keys = fingerprintRows
            .GroupBy(fingerprint => new { fingerprint.Type, fingerprint.Value })
            .Where(group => group.Select(fingerprint => fingerprint.FileId).Distinct().Count() > 1)
            .Select(group => new { group.Key.Type, group.Key.Value })
            .ToList();

        var result = new List<List<int>>();
        var seenGroups = new HashSet<string>();
        foreach (var key in keys)
        {
            var sceneIds = await db.VideoFiles
                .Where(file => file.SceneId.HasValue && file.Fingerprints.Any(fingerprint => fingerprint.Type == key.Type && fingerprint.Value == key.Value))
                .Select(file => file.SceneId!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync(ct);

            AddSceneGroup(result, seenGroups, sceneIds);
        }

        return result;
    }

    private async Task<List<List<int>>> FindPhashDuplicateSceneIdsAsync(int maxDistance, double? durationDiff, CancellationToken ct)
    {
        var files = await db.VideoFiles
            .Include(file => file.Fingerprints)
            .Where(file => file.SceneId.HasValue)
            .AsNoTracking()
            .ToListAsync(ct);

        var candidates = files
            .SelectMany(file => file.Fingerprints
                .Where(fingerprint => fingerprint.Type == "phash" && fingerprint.Value != "")
                .Select(fingerprint => TryParsePHash(fingerprint.Value, out var parsedHash)
                    ? new DuplicatePHashCandidate(file.SceneId!.Value, file.Duration, parsedHash)
                    : (DuplicatePHashCandidate?)null))
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .ToList();

        var sceneIds = candidates.Select(candidate => candidate.SceneId).Distinct().ToArray();
        var parent = sceneIds.ToDictionary(id => id, id => id);

        for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
        {
            var left = candidates[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
            {
                var right = candidates[rightIndex];
                if (left.SceneId == right.SceneId) continue;
                if (durationDiff.HasValue && Math.Abs(left.Duration - right.Duration) > durationDiff.Value) continue;
                if (BitOperations.PopCount(left.Hash ^ right.Hash) <= maxDistance)
                    Union(parent, left.SceneId, right.SceneId);
            }
        }

        return parent.Keys
            .GroupBy(id => Find(parent, id))
            .Select(group => group.OrderBy(id => id).ToList())
            .Where(group => group.Count > 1)
            .OrderBy(group => group[0])
            .ToList();
    }

    private async Task<List<List<int>>> FindTitleDuplicateSceneIdsAsync(CancellationToken ct)
    {
        var rows = await db.Scenes
            .Where(scene => scene.Title != null && scene.Title != "")
            .Select(scene => new { scene.Id, scene.Title })
            .AsNoTracking()
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.Title!.Trim().ToLowerInvariant())
            .Select(group => group.Select(row => row.Id).OrderBy(id => id).ToList())
            .Where(group => group.Count > 1)
            .OrderBy(group => group[0])
            .ToList();
    }

    private async Task<List<List<int>>> FindRemoteIdDuplicateSceneIdsAsync(CancellationToken ct)
    {
        var rows = await db.Set<SceneRemoteId>()
            .Where(remoteId => remoteId.RemoteId != "")
            .Select(remoteId => new { remoteId.SceneId, remoteId.Endpoint, remoteId.RemoteId })
            .AsNoTracking()
            .ToListAsync(ct);

        return rows
            .GroupBy(row => $"{row.Endpoint.Trim().ToLowerInvariant()}\n{row.RemoteId.Trim().ToLowerInvariant()}")
            .Select(group => group.Select(row => row.SceneId).Distinct().OrderBy(id => id).ToList())
            .Where(group => group.Count > 1)
            .OrderBy(group => group[0])
            .ToList();
    }

    private static void AddSceneGroup(List<List<int>> result, HashSet<string> seenGroups, List<int> sceneIds)
    {
        if (sceneIds.Count <= 1) return;
        var key = string.Join(',', sceneIds);
        if (seenGroups.Add(key)) result.Add(sceneIds);
    }

    private static bool TryParsePHash(string value, out ulong hash)
    {
        hash = 0;
        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];
        if (normalized.Length is 0 or > 16) return false;
        if (normalized.Any(character => !Uri.IsHexDigit(character))) return false;
        return ulong.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out hash);
    }

    private static int Find(Dictionary<int, int> parent, int id)
    {
        if (parent[id] == id) return id;
        parent[id] = Find(parent, parent[id]);
        return parent[id];
    }

    private static void Union(Dictionary<int, int> parent, int left, int right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (leftRoot != rightRoot) parent[rightRoot] = leftRoot;
    }

    private readonly record struct DuplicatePHashCandidate(int SceneId, double Duration, ulong Hash);

    // ===== Bulk Operations =====

    [HttpPost("bulk")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite, ActionArgumentName = "dto", PropertyName = "Ids")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkSceneUpdateDto dto, CancellationToken ct)
    {
        var scenes = await db.Scenes
            .Include(s => s.SceneTags)
            .Include(s => s.ScenePerformers)
            .Include(s => s.GroupItems)
            .Where(s => dto.Ids.Contains(s.Id))
            .ToListAsync(ct);
        var clearFields = dto.ClearFields?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (var scene in scenes)
        {
            var previousTagIds = dto.TagIds != null ? scene.SceneTags.Select(sceneTag => sceneTag.TagId).ToArray() : [];

            if (clearFields.Contains("studioId")) scene.StudioId = null;
            if (clearFields.Contains("date")) scene.Date = null;
            if (clearFields.Contains("code")) scene.Code = null;
            if (clearFields.Contains("director")) scene.Director = null;
            if (dto.Organized.HasValue) scene.Organized = dto.Organized.Value;
            if (dto.StudioId.HasValue) scene.StudioId = dto.StudioId;
            if (dto.Date != null) scene.Date = ParseDate(dto.Date);
            if (dto.Code != null) scene.Code = dto.Code;
            if (dto.Director != null) scene.Director = dto.Director;

            if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Set)
            {
                scene.SceneTags.Clear();
                scene.SceneTags = dto.TagIds.Select(tid => new SceneTag { TagId = tid, SceneId = scene.Id }).ToList();
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Add)
            {
                var existing = scene.SceneTags.Select(st => st.TagId).ToHashSet();
                foreach (var tid in dto.TagIds.Where(t => !existing.Contains(t)))
                    scene.SceneTags.Add(new SceneTag { TagId = tid, SceneId = scene.Id });
            }
            else if (dto.TagIds != null && dto.TagMode == BulkUpdateMode.Remove)
            {
                scene.SceneTags = scene.SceneTags.Where(st => !dto.TagIds.Contains(st.TagId)).ToList();
            }

            if (dto.TagIds != null && tagProvenanceService != null)
            {
                await tagProvenanceService.SyncTagSetAsync(
                    AffinityHostType.Scene,
                    scene.Id,
                    previousTagIds,
                    scene.SceneTags.Select(sceneTag => sceneTag.TagId).ToArray(),
                    cancellationToken: ct);
            }

            if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Set)
            {
                scene.ScenePerformers.Clear();
                scene.ScenePerformers = dto.PerformerIds.Select(pid => new ScenePerformer { PerformerId = pid, SceneId = scene.Id }).ToList();
            }
            else if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Add)
            {
                var existing = scene.ScenePerformers.Select(sp => sp.PerformerId).ToHashSet();
                foreach (var pid in dto.PerformerIds.Where(p => !existing.Contains(p)))
                    scene.ScenePerformers.Add(new ScenePerformer { PerformerId = pid, SceneId = scene.Id });
            }
            else if (dto.PerformerIds != null && dto.PerformerMode == BulkUpdateMode.Remove)
            {
                scene.ScenePerformers = scene.ScenePerformers.Where(sp => !dto.PerformerIds.Contains(sp.PerformerId)).ToList();
            }

            if (dto.GroupIds != null && dto.GroupMode == BulkUpdateMode.Set)
            {
                ReplaceWholeSceneGroupItems(scene, dto.GroupIds);
            }
            else if (dto.GroupIds != null && dto.GroupMode == BulkUpdateMode.Add)
            {
                var existing = scene.GroupItems
                    .Where(item => item.Kind == GroupItemKind.Scene)
                    .Select(item => item.GroupId)
                    .ToHashSet();
                foreach (var g in dto.GroupIds.Where(g => !existing.Contains(g.GroupId)))
                    scene.GroupItems.Add(new GroupItem
                    {
                        GroupId = g.GroupId,
                        OrderIndex = g.SceneIndex,
                        Kind = GroupItemKind.Scene,
                        SceneId = scene.Id,
                    });
            }
            else if (dto.GroupIds != null && dto.GroupMode == BulkUpdateMode.Remove)
            {
                var removeIds = dto.GroupIds.Select(g => g.GroupId).ToHashSet();
                RemoveWholeSceneGroupItems(scene, scene.GroupItems.Where(item => item.Kind == GroupItemKind.Scene && removeIds.Contains(item.GroupId)).ToList());
            }
        }

        await db.SaveChangesAsync(ct);
        if (dto.Rating.HasValue)
        {
            foreach (var scene in scenes)
                await engagementService.SetSceneRatingAsync(scene.Id, dto.Rating, cancellationToken: ct);
        }
        return Ok(new { updated = scenes.Count });
    }

    private static List<GroupSummaryDto> MapWholeSceneGroups(Scene scene)
        => scene.GroupItems
            .Where(item => item.Kind == GroupItemKind.Scene && item.Group != null)
            .OrderBy(item => item.OrderIndex)
            .Select(item => new GroupSummaryDto(item.Group!.Id, item.Group.Name, item.OrderIndex))
            .ToList();

    private void ReplaceWholeSceneGroupItems(Scene scene, IEnumerable<SceneGroupInputDto> groups)
    {
        RemoveWholeSceneGroupItems(scene, scene.GroupItems.Where(item => item.Kind == GroupItemKind.Scene).ToList());
        foreach (var group in groups)
        {
            scene.GroupItems.Add(new GroupItem
            {
                GroupId = group.GroupId,
                OrderIndex = group.SceneIndex,
                Kind = GroupItemKind.Scene,
                SceneId = scene.Id,
            });
        }
    }

    private void RemoveWholeSceneGroupItems(Scene scene, IReadOnlyCollection<GroupItem> items)
    {
        foreach (var item in items)
        {
            scene.GroupItems.Remove(item);
        }

        if (items.Count > 0)
        {
            db.GroupItems.RemoveRange(items);
        }
    }

    // ===== Merge =====

    [HttpPost("merge")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite, ActionArgumentName = "dto", PropertyName = "TargetId")]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite, ActionArgumentName = "dto", PropertyName = "SourceIds")]
    public async Task<ActionResult<SceneDto>> MergeScenes([FromBody] SceneMergeDto dto, CancellationToken ct)
    {
        var target = await sceneRepo.GetByIdWithRelationsAsync(dto.TargetId, ct);
        if (target == null) return NotFound("Target scene not found");

        var sources = await db.Scenes
            .Include(s => s.Files)
            .Include(s => s.SceneTags)
            .Include(s => s.ScenePerformers)
            .Include(s => s.SceneGalleries)
            .Include(s => s.Urls)
            .Where(s => dto.SourceIds.Contains(s.Id))
            .ToListAsync(ct);

        var existingTagIds = target.SceneTags.Select(st => st.TagId).ToHashSet();
        var existingPerfIds = target.ScenePerformers.Select(sp => sp.PerformerId).ToHashSet();

        foreach (var source in sources)
        {
            // Move files to target
            foreach (var f in source.Files) f.SceneId = target.Id;
            // Merge tags
            foreach (var st in source.SceneTags.Where(st => !existingTagIds.Contains(st.TagId)))
                target.SceneTags.Add(new SceneTag { TagId = st.TagId, SceneId = target.Id });
            // Merge performers
            foreach (var sp in source.ScenePerformers.Where(sp => !existingPerfIds.Contains(sp.PerformerId)))
                target.ScenePerformers.Add(new ScenePerformer { PerformerId = sp.PerformerId, SceneId = target.Id });
            // Delete source
            if (tagProvenanceService != null)
                await tagProvenanceService.RemoveForHostAsync(AffinityHostType.Scene, source.Id, ct);
            db.Scenes.Remove(source);
        }

        await db.SaveChangesAsync(ct);
        var result = await sceneRepo.GetByIdWithRelationsAsync(target.Id, ct);
        var engagement = (await engagementService.GetSceneSnapshotsAsync([target.Id], ct)).GetValueOrDefault(target.Id);
        return Ok(await MapToDtoWithProvenanceAsync(result!, engagement, HasUserScopedEngagement, ct));
    }

    // ===== Generate Screenshot =====

    [HttpPost("{id:int}/generate-screenshot")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> GenerateScreenshot(int id, [FromBody] GenerateScreenshotDto? dto, CancellationToken ct)
    {
        var scene = await sceneRepo.GetByIdAsync(id, ct);
        if (scene == null) return NotFound();

        await thumbnailService.GenerateSceneThumbnailAsync(id, dto?.AtSeconds, ct);
        scene.UpdatedAt = DateTime.UtcNow;
        await sceneRepo.UpdateAsync(scene, ct);
        return Ok(new { success = true });
    }

    // ===== Rescan =====

    [HttpPost("{id:int}/rescan")]
    [RequiresPermission(Permissions.LibraryScan)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.LibraryScan)]
    public async Task<IActionResult> Rescan(int id, CancellationToken ct)
    {
        var scene = await db.Scenes.Include(s => s.Files).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scene == null) return NotFound();

        var filePath = scene.Files.FirstOrDefault()?.ParentFolder != null 
            ? Path.Combine(scene.Files.First().ParentFolder!.Path, scene.Files.First().Basename)
            : scene.Files.FirstOrDefault()?.Basename;
        
        if (string.IsNullOrEmpty(filePath)) return BadRequest("Scene has no files");

        var jobId = scanService.StartScan(new ScanOperationOptions
        {
            Paths = [filePath],
            Rescan = true,
        });
        return Ok(new { jobId });
    }

    // ===== Assign File =====

    [HttpPost("{id:int}/assign-file")]
    [RequiresPermission(Permissions.ScenesWrite)]
    [RequiresEntityAccess(EntityKinds.Scene, Permissions.ScenesWrite)]
    public async Task<IActionResult> AssignFile(int id, [FromBody] SceneAssignFileDto dto, CancellationToken ct)
    {
        var scene = await db.Scenes.FindAsync([id], ct);
        if (scene == null) return NotFound("Scene not found");

        var file = await db.Set<VideoFile>().FirstOrDefaultAsync(f => f.Id == dto.FileId, ct);
        if (file == null) return NotFound("File not found");

        file.SceneId = id;
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    private static DateOnly? ParseDate(string? date) => DateOnly.TryParse(date, out var d) ? d : null;
}

public record GenerateScreenshotDto(double? AtSeconds = null);
