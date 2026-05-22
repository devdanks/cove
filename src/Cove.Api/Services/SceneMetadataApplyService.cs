using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Events;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public interface ISceneMetadataApplyService
{
    Task<bool> ApplyAsync(int sceneId, ScrapedSceneDto metadata, DownloaderMetadataApplyOptions? options = null, CancellationToken ct = default);
}

public class SceneMetadataApplyService(CoveContext db, IEventBus eventBus, ISceneCoverService sceneCoverService, ITagProvenanceService tagProvenanceService, IFieldProvenanceService? fieldProvenanceService = null) : ISceneMetadataApplyService
{
    public async Task<bool> ApplyAsync(int sceneId, ScrapedSceneDto metadata, DownloaderMetadataApplyOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        options ??= new DownloaderMetadataApplyOptions();

        var scene = await db.Scenes
            .Include(item => item.Urls)
            .Include(item => item.SceneTags).ThenInclude(item => item.Tag)
            .Include(item => item.ScenePerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == sceneId, ct);

        if (scene == null)
            return false;

        var fieldProvenance = new Dictionary<string, object?>();
        var sourceKey = BuildScraperSourceKey(metadata.SourceScraperId);

        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            scene.Title = metadata.Title.Trim();
            fieldProvenance["title"] = scene.Title;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Code))
        {
            scene.Code = metadata.Code.Trim();
            fieldProvenance["code"] = scene.Code;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Details))
        {
            scene.Details = metadata.Details.Trim();
            fieldProvenance["details"] = scene.Details;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Director))
        {
            scene.Director = metadata.Director.Trim();
            fieldProvenance["director"] = scene.Director;
        }

        if (ScrapedSceneDateParser.TryParse(metadata.Date, out var parsedDate))
        {
            scene.Date = parsedDate;
            fieldProvenance["date"] = parsedDate.ToString("yyyy-MM-dd");
        }

        if (options.MarkOrganized)
            scene.Organized = true;

        await sceneCoverService.TryApplyRemoteCoverAsync(scene, metadata.ImageUrl, ct);
        if (!string.IsNullOrWhiteSpace(metadata.ImageUrl))
            fieldProvenance["image_url"] = metadata.ImageUrl.Trim();

        var urls = NormalizeNames(metadata.Urls);
        if (urls.Count > 0)
            fieldProvenance["urls"] = urls;
        ApplyUrls(scene, urls);

        var tagNames = NormalizeNames(metadata.TagNames);
        if (tagNames.Count > 0)
            fieldProvenance["tags"] = tagNames;
        await ApplyTagsAsync(scene, tagNames, options.CreateMissingTags, sourceKey, ct);

        var performerNames = NormalizeNames(metadata.PerformerNames);
        if (performerNames.Count > 0)
            fieldProvenance["performers"] = performerNames;
        await ApplyPerformersAsync(scene, performerNames, options.CreateMissingPerformers, ct);

        var studioName = string.IsNullOrWhiteSpace(metadata.StudioName) ? null : metadata.StudioName.Trim();
        if (!string.IsNullOrWhiteSpace(studioName))
            fieldProvenance["studio"] = studioName;
        await ApplyStudioAsync(scene, studioName, options.CreateMissingStudio, ct);

        if (fieldProvenance.Count > 0 && fieldProvenanceService != null)
            await fieldProvenanceService.RecordManyAsync(AffinityHostType.Scene, scene.Id, fieldProvenance, sourceKey, cancellationToken: ct);

        await db.SaveChangesAsync(ct);
        eventBus.Publish(new EntityEvent(EventType.SceneUpdated, "Scene", scene.Id));
        return true;
    }

    private static void ApplyUrls(Scene scene, IReadOnlyList<string> urls)
    {
        var existing = scene.Urls.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var url in NormalizeNames(urls))
        {
            if (existing.Add(url))
                scene.Urls.Add(new SceneUrl { SceneId = scene.Id, Url = url });
        }
    }

    private static string BuildScraperSourceKey(string? scraperId)
        => string.IsNullOrWhiteSpace(scraperId) ? "scraper" : $"scraper:{scraperId.Trim()}";

    private async Task ApplyTagsAsync(Scene scene, IReadOnlyList<string> tagNames, bool createMissing, string sourceKey, CancellationToken ct)
    {
        var names = NormalizeNames(tagNames);
        if (names.Count == 0)
            return;

        var normalizedNames = names.Select(name => name.ToLowerInvariant()).ToHashSet();
        var tagLookup = await db.Tags
            .Where(tag => normalizedNames.Contains(tag.Name.ToLower()))
            .ToDictionaryAsync(tag => tag.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existing = scene.SceneTags
            .Where(item => item.Tag != null)
            .Select(item => item.Tag!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (!tagLookup.TryGetValue(name, out var tag))
            {
                if (!createMissing)
                    continue;

                tag = new Tag { Name = name };
                db.Tags.Add(tag);
                tagLookup[name] = tag;
            }

            if (existing.Add(tag.Name))
                scene.SceneTags.Add(new SceneTag { Scene = scene, Tag = tag });

            await tagProvenanceService.RecordAsync(AffinityHostType.Scene, scene.Id, tag, sourceKey, cancellationToken: ct);
        }
    }

    private async Task ApplyPerformersAsync(Scene scene, IReadOnlyList<string> performerNames, bool createMissing, CancellationToken ct)
    {
        var names = NormalizeNames(performerNames);
        if (names.Count == 0)
            return;

        var normalizedNames = names.Select(name => name.ToLowerInvariant()).ToHashSet();
        var performerLookup = await db.Performers
            .Where(performer => normalizedNames.Contains(performer.Name.ToLower()))
            .ToDictionaryAsync(performer => performer.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existing = scene.ScenePerformers
            .Where(item => item.Performer != null)
            .Select(item => item.Performer!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (!performerLookup.TryGetValue(name, out var performer))
            {
                if (!createMissing)
                    continue;

                performer = new Performer { Name = name };
                db.Performers.Add(performer);
                performerLookup[name] = performer;
            }

            if (existing.Add(performer.Name))
                scene.ScenePerformers.Add(new ScenePerformer { Scene = scene, Performer = performer });
        }
    }

    private async Task ApplyStudioAsync(Scene scene, string? studioName, bool createMissing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studioName))
            return;

        var normalizedStudioName = studioName.Trim();
        var studio = await db.Studios.FirstOrDefaultAsync(item => item.Name == normalizedStudioName, ct);
        if (studio == null && !createMissing)
            return;

        studio ??= new Studio { Name = normalizedStudioName };

        if (studio.Id == 0)
            db.Studios.Add(studio);

        scene.Studio = studio;
        scene.StudioId = studio.Id == 0 ? null : studio.Id;
    }

    private static List<string> NormalizeNames(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}