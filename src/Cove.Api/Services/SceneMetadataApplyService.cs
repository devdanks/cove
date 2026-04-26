using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Events;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public interface ISceneMetadataApplyService
{
    Task<bool> ApplyAsync(int sceneId, ScrapedSceneDto metadata, CancellationToken ct = default);
}

public class SceneMetadataApplyService(CoveContext db, IEventBus eventBus, ISceneCoverService sceneCoverService) : ISceneMetadataApplyService
{
    public async Task<bool> ApplyAsync(int sceneId, ScrapedSceneDto metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var scene = await db.Scenes
            .Include(item => item.Urls)
            .Include(item => item.SceneTags).ThenInclude(item => item.Tag)
            .Include(item => item.ScenePerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == sceneId, ct);

        if (scene == null)
            return false;

        if (!string.IsNullOrWhiteSpace(metadata.Title))
            scene.Title = metadata.Title.Trim();

        if (!string.IsNullOrWhiteSpace(metadata.Code))
            scene.Code = metadata.Code.Trim();

        if (!string.IsNullOrWhiteSpace(metadata.Details))
            scene.Details = metadata.Details.Trim();

        if (!string.IsNullOrWhiteSpace(metadata.Director))
            scene.Director = metadata.Director.Trim();

        if (ScrapedSceneDateParser.TryParse(metadata.Date, out var parsedDate))
            scene.Date = parsedDate;

        await sceneCoverService.TryApplyRemoteCoverAsync(scene, metadata.ImageUrl, ct);

        ApplyUrls(scene, metadata.Urls);
        await ApplyTagsAsync(scene, metadata.TagNames, ct);
        await ApplyPerformersAsync(scene, metadata.PerformerNames, ct);
        await ApplyStudioAsync(scene, metadata.StudioName, ct);

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

    private async Task ApplyTagsAsync(Scene scene, IReadOnlyList<string> tagNames, CancellationToken ct)
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
                tag = new Tag { Name = name };
                db.Tags.Add(tag);
                tagLookup[name] = tag;
            }

            if (existing.Add(tag.Name))
                scene.SceneTags.Add(new SceneTag { Scene = scene, Tag = tag });
        }
    }

    private async Task ApplyPerformersAsync(Scene scene, IReadOnlyList<string> performerNames, CancellationToken ct)
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
                performer = new Performer { Name = name };
                db.Performers.Add(performer);
                performerLookup[name] = performer;
            }

            if (existing.Add(performer.Name))
                scene.ScenePerformers.Add(new ScenePerformer { Scene = scene, Performer = performer });
        }
    }

    private async Task ApplyStudioAsync(Scene scene, string? studioName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studioName))
            return;

        var normalizedStudioName = studioName.Trim();
        var studio = await db.Studios.FirstOrDefaultAsync(item => item.Name == normalizedStudioName, ct)
            ?? new Studio { Name = normalizedStudioName };

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