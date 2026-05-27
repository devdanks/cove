using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Plugins;

namespace Cove.Api.Services;

public class AutoTagService(
    IJobService jobService,
    IServiceScopeFactory scopeFactory,
    ExtensionManager extensionManager,
    ITagProvenanceService tagProvenanceService,
    ILogger<AutoTagService> logger) : IAutoTagService
{
    public string StartAutoTag(IEnumerable<string>? performerIds = null, IEnumerable<string>? studioIds = null, IEnumerable<string>? tagIds = null)
    {
        return jobService.Enqueue("auto-tag", "Auto-tagging library content", async (progress, ct) =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
            var matchers = extensionManager.GetAutoTagMatchers();
            var matcherLookup = BuildMatcherLookup(matchers);
            var performers = BuildCandidateSet(FilterCandidates(await LoadPerformersAsync(db, ct), performerIds), AutoTagEntityKind.Performer, matcherLookup);
            var studios = BuildCandidateSet(FilterCandidates(await LoadStudiosAsync(db, ct), studioIds), AutoTagEntityKind.Studio, matcherLookup);
            var tags = BuildCandidateSet(FilterCandidates(await LoadTagsAsync(db, ct), tagIds), AutoTagEntityKind.Tag, matcherLookup);
            var workItems = await LoadContentWorkItemsAsync(db, ct);

            var updatedItems = 0;
            var createdAssociations = 0;
            var total = workItems.Count;
            var autoDetectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            try
            {
                for (var index = 0; index < total; index++)
                {
                    ct.ThrowIfCancellationRequested();

                    var workItem = workItems[index];
                    var itemAssociations = 0;
                    itemAssociations += await ApplyPerformerMatchesAsync(workItem, performers, ct);
                    itemAssociations += await ApplyStudioMatchesAsync(workItem, studios, ct);
                    itemAssociations += await ApplyTagMatchesAsync(workItem, tags, ct);

                    if (itemAssociations > 0)
                    {
                        updatedItems++;
                        createdAssociations += itemAssociations;
                    }

                    progress.Report(total == 0 ? 1d : (double)(index + 1) / total, $"{workItem.Candidate.ContentType}: {workItem.Candidate.DisplayName ?? workItem.Candidate.ContentId.ToString()} ({index + 1}/{Math.Max(total, 1)})");
                }
            }
            finally
            {
                db.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
            }

            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Auto-tag complete: {Associations} associations across {Items} content items", createdAssociations, updatedItems);

            await RunLegacyParticipantsAsync(progress, performers.RawCandidates, studios.RawCandidates, tags.RawCandidates, ct);
        }, exclusive: false);
    }

    private static async Task<List<AutoTagEntityCandidate>> LoadPerformersAsync(CoveContext db, CancellationToken ct)
    {
        return await db.Performers
            .AsTracking()
            .Where(performer => !performer.IgnoreAutoTag)
            .Include(performer => performer.Aliases)
            .Select(performer => new AutoTagEntityCandidate(
                AutoTagEntityKind.Performer,
                performer.Id,
                performer.Name,
                performer.Aliases.Select(alias => alias.Alias).ToList()))
            .ToListAsync(ct);
    }

    private static async Task<List<AutoTagEntityCandidate>> LoadStudiosAsync(CoveContext db, CancellationToken ct)
    {
        return await db.Studios
            .AsTracking()
            .Where(studio => !studio.IgnoreAutoTag)
            .Select(studio => new AutoTagEntityCandidate(AutoTagEntityKind.Studio, studio.Id, studio.Name, null))
            .ToListAsync(ct);
    }

    private static async Task<List<AutoTagEntityCandidate>> LoadTagsAsync(CoveContext db, CancellationToken ct)
    {
        return await db.Tags
            .AsTracking()
            .Where(tag => !tag.IgnoreAutoTag)
            .Include(tag => tag.Aliases)
            .Select(tag => new AutoTagEntityCandidate(
                AutoTagEntityKind.Tag,
                tag.Id,
                tag.Name,
                tag.Aliases.Select(alias => alias.Alias).ToList()))
            .ToListAsync(ct);
    }

    private static List<AutoTagEntityCandidate> FilterCandidates(List<AutoTagEntityCandidate> candidates, IEnumerable<string>? selectors)
    {
        var normalizedSelectors = selectors?
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .Select(selector => selector.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

        if (normalizedSelectors.Count == 0)
            return candidates;

        return candidates
            .Where(candidate => normalizedSelectors.Any(selector => SelectorMatches(candidate, selector)))
            .ToList();
    }

    private static bool SelectorMatches(AutoTagEntityCandidate candidate, string selector)
    {
        if (int.TryParse(selector, out var numericId) && numericId == candidate.EntityId)
            return true;

        var normalizedSelector = NormalizeText(selector);
        if (string.IsNullOrWhiteSpace(normalizedSelector))
            return false;

        if (NormalizeText(candidate.Name).Contains(normalizedSelector, StringComparison.Ordinal))
            return true;

        return candidate.Aliases?.Any(alias => NormalizeText(alias).Contains(normalizedSelector, StringComparison.Ordinal)) == true;
    }

    private static Dictionary<AutoTagEntityKind, List<IAutoTagMatcher>> BuildMatcherLookup(IReadOnlyList<IAutoTagMatcher> matchers)
    {
        var lookup = new Dictionary<AutoTagEntityKind, List<IAutoTagMatcher>>
        {
            [AutoTagEntityKind.Performer] = [],
            [AutoTagEntityKind.Studio] = [],
            [AutoTagEntityKind.Tag] = [],
        };

        foreach (var matcher in matchers)
        {
            foreach (var kind in matcher.SupportedEntities.Distinct())
            {
                if (lookup.TryGetValue(kind, out var relevantMatchers))
                    relevantMatchers.Add(matcher);
            }
        }

        return lookup;
    }

    private static AutoTagCandidateSet BuildCandidateSet(
        IReadOnlyList<AutoTagEntityCandidate> candidates,
        AutoTagEntityKind entityKind,
        IReadOnlyDictionary<AutoTagEntityKind, List<IAutoTagMatcher>> matcherLookup)
    {
        var prepared = candidates.Select(PrepareCandidate).ToList();
        return new AutoTagCandidateSet(prepared, matcherLookup.TryGetValue(entityKind, out var matchers) ? matchers : []);
    }

    private static PreparedAutoTagEntityCandidate PrepareCandidate(AutoTagEntityCandidate candidate)
    {
        var phrases = new[] { candidate.Name }
            .Concat(candidate.Aliases ?? [])
            .Select(PreparePhrase)
            .Where(phrase => phrase != null)
            .Select(phrase => phrase!)
            .ToList();

        return new PreparedAutoTagEntityCandidate(candidate, phrases);
    }

    private static PreparedAutoTagPhrase? PreparePhrase(string? rawPhrase)
    {
        var normalizedPhrase = NormalizeText(rawPhrase);
        if (string.IsNullOrWhiteSpace(normalizedPhrase))
            return null;

        var compactLength = normalizedPhrase.Replace(" ", string.Empty, StringComparison.Ordinal).Length;
        var tokens = normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new PreparedAutoTagPhrase(normalizedPhrase, compactLength, tokens);
    }

    private async Task<List<AutoTagContentWorkItem>> LoadContentWorkItemsAsync(CoveContext db, CancellationToken ct)
    {
        var scenes = await db.Scenes
            .Include(scene => scene.Files).ThenInclude(file => file.ParentFolder)
            .Include(scene => scene.ScenePerformers)
            .Include(scene => scene.SceneTags)
            .ToListAsync(ct);

        var images = await db.Images
            .Include(image => image.Files).ThenInclude(file => file.ParentFolder)
            .Include(image => image.ImagePerformers)
            .Include(image => image.ImageTags)
            .ToListAsync(ct);

        var galleries = await db.Galleries
            .Include(gallery => gallery.Folder)
            .Include(gallery => gallery.Files).ThenInclude(file => file.ParentFolder)
            .Include(gallery => gallery.GalleryPerformers)
            .Include(gallery => gallery.GalleryTags)
            .ToListAsync(ct);

        var workItems = new List<AutoTagContentWorkItem>(scenes.Count + images.Count + galleries.Count);
        workItems.AddRange(scenes.Select(BuildSceneWorkItem).Where(item => item != null)!);
        workItems.AddRange(images.Select(BuildImageWorkItem).Where(item => item != null)!);
        workItems.AddRange(galleries.Select(BuildGalleryWorkItem).Where(item => item != null)!);
        return workItems;
    }

    private static AutoTagContentWorkItem? BuildSceneWorkItem(Scene scene)
    {
        var file = scene.Files.FirstOrDefault();
        var searchText = BuildSearchText(scene.Title, file?.Path, file?.Basename);
        if (string.IsNullOrWhiteSpace(searchText))
            return null;

        return new AutoTagContentWorkItem(
            new AutoTagContentCandidate(AutoTagContentType.Scene, scene.Id, searchText, scene.Title ?? file?.Basename ?? $"Scene {scene.Id}"),
            scene);
    }

    private static AutoTagContentWorkItem? BuildImageWorkItem(Image image)
    {
        var file = image.Files.FirstOrDefault();
        var searchText = BuildSearchText(image.Title, image.Code, file?.Path, file?.Basename);
        if (string.IsNullOrWhiteSpace(searchText))
            return null;

        return new AutoTagContentWorkItem(
            new AutoTagContentCandidate(AutoTagContentType.Image, image.Id, searchText, image.Title ?? file?.Basename ?? $"Image {image.Id}"),
            image);
    }

    private static AutoTagContentWorkItem? BuildGalleryWorkItem(Gallery gallery)
    {
        var file = gallery.Files.FirstOrDefault();
        var searchText = BuildSearchText(gallery.Title, gallery.Code, gallery.Folder?.Path, file?.Path, file?.Basename);
        if (string.IsNullOrWhiteSpace(searchText))
            return null;

        return new AutoTagContentWorkItem(
            new AutoTagContentCandidate(AutoTagContentType.Gallery, gallery.Id, searchText, gallery.Title ?? file?.Basename ?? $"Gallery {gallery.Id}"),
            gallery);
    }

    private static string BuildSearchText(params string?[] parts)
    {
        return NormalizeText(string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part))));
    }

    private async Task<int> ApplyPerformerMatchesAsync(AutoTagContentWorkItem workItem, AutoTagCandidateSet performers, CancellationToken ct)
    {
        var added = 0;
        foreach (var performer in performers.GetCandidates(workItem.Candidate.SearchText))
        {
            if (HasPerformer(workItem.Entity, performer.Candidate.EntityId))
                continue;

            if (!await IsMatchAsync(performer, workItem.Candidate, performers.Matchers, ct))
                continue;

            AddPerformer(workItem.Entity, performer.Candidate.EntityId);
            added++;
        }

        return added;
    }

    private async Task<int> ApplyStudioMatchesAsync(AutoTagContentWorkItem workItem, AutoTagCandidateSet studios, CancellationToken ct)
    {
        if (HasStudio(workItem.Entity))
            return 0;

        foreach (var studio in studios.GetCandidates(workItem.Candidate.SearchText))
        {
            if (!await IsMatchAsync(studio, workItem.Candidate, studios.Matchers, ct))
                continue;

            SetStudio(workItem.Entity, studio.Candidate.EntityId);
            return 1;
        }

        return 0;
    }

    private async Task<int> ApplyTagMatchesAsync(AutoTagContentWorkItem workItem, AutoTagCandidateSet tags, CancellationToken ct)
    {
        var added = 0;
        foreach (var tag in tags.GetCandidates(workItem.Candidate.SearchText))
        {
            if (HasTag(workItem.Entity, tag.Candidate.EntityId))
                continue;

            if (!await IsMatchAsync(tag, workItem.Candidate, tags.Matchers, ct))
                continue;

            AddTag(workItem.Entity, tag.Candidate.EntityId);
            if (TryGetTagProvenanceHost(workItem.Entity, out var hostType, out var hostId))
                await tagProvenanceService.RecordAsync(hostType, hostId, tag.Candidate.EntityId, "system", cancellationToken: ct);
            added++;
        }

        return added;
    }

    private async Task<bool> IsMatchAsync(PreparedAutoTagEntityCandidate entity, AutoTagContentCandidate content, IReadOnlyList<IAutoTagMatcher> matchers, CancellationToken ct)
    {
        if (HasBuiltInTextMatch(entity, content.SearchText))
            return true;

        if (matchers.Count == 0)
            return false;

        var request = new AutoTagMatchRequest(entity.Candidate, content);
        foreach (var matcher in matchers)
        {
            try
            {
                var matches = await matcher.MatchAsync(request, ct);
                if (matches.Any(match => match.Score >= 0.5))
                    return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto-tag matcher {MatcherId} failed for {EntityKind} {EntityId}", matcher.Id, entity.Candidate.EntityKind, entity.Candidate.EntityId);
            }
        }

        return false;
    }

    private static bool HasBuiltInTextMatch(PreparedAutoTagEntityCandidate entity, string normalizedSearchText)
    {
        var paddedSearchText = $" {normalizedSearchText} ";
        return entity.Phrases.Any(phrase => ContainsNormalizedPhrase(paddedSearchText, phrase));
    }

    private static bool ContainsNormalizedPhrase(string paddedSearchText, PreparedAutoTagPhrase phrase)
    {
        if (phrase.CompactLength < 3)
            return false;

        if (paddedSearchText.Contains($" {phrase.NormalizedPhrase} ", StringComparison.Ordinal))
            return true;

        return phrase.Tokens.Length > 1 && phrase.Tokens.All(token => token.Length > 1 && paddedSearchText.Contains($" {token} ", StringComparison.Ordinal));
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var bufferIndex = 0;
        var previousWasSeparator = true;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[bufferIndex++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                buffer[bufferIndex++] = ' ';
                previousWasSeparator = true;
            }
        }

        if (bufferIndex > 0 && buffer[bufferIndex - 1] == ' ')
            bufferIndex--;

        return new string(buffer[..bufferIndex]);
    }

    private async Task RunLegacyParticipantsAsync(Cove.Core.Interfaces.IJobProgress progress, IReadOnlyList<AutoTagEntityCandidate> performers, IReadOnlyList<AutoTagEntityCandidate> studios, IReadOnlyList<AutoTagEntityCandidate> tags, CancellationToken ct)
    {
        var participants = extensionManager.GetAutoTagParticipants();
        if (participants.Count == 0)
            return;

        var atPerformers = performers.Select(performer => new AutoTagPerformer(performer.EntityId, performer.Name, performer.Aliases?.ToList() ?? [])).ToList();
        var atStudios = studios.Select(studio => new AutoTagStudio(studio.EntityId, studio.Name)).ToList();
        var atTags = tags.Select(tag => new AutoTagTag(tag.EntityId, tag.Name, tag.Aliases?.ToList() ?? [])).ToList();
        var extProgress = new ProgressAdapter(progress);

        for (var participantIndex = 0; participantIndex < participants.Count; participantIndex++)
        {
            var participant = participants[participantIndex];
            try
            {
                logger.LogInformation("Running auto-tag participant: {Name}", participant.Name);
                using var participantScope = scopeFactory.CreateScope();
                var context = new AutoTagContext(atPerformers, atStudios, atTags, extProgress, participantScope.ServiceProvider);
                await participant.AutoTagAsync(context, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Extension auto-tag participant {Name} failed", participant.Name);
            }
        }
    }

    private static bool HasPerformer(object entity, int performerId)
    {
        return entity switch
        {
            Scene scene => scene.ScenePerformers.Any(link => link.PerformerId == performerId),
            Image image => image.ImagePerformers.Any(link => link.PerformerId == performerId),
            Gallery gallery => gallery.GalleryPerformers.Any(link => link.PerformerId == performerId),
            _ => true,
        };
    }

    private static void AddPerformer(object entity, int performerId)
    {
        switch (entity)
        {
            case Scene scene:
                scene.ScenePerformers.Add(new ScenePerformer { SceneId = scene.Id, PerformerId = performerId });
                break;
            case Image image:
                image.ImagePerformers.Add(new ImagePerformer { ImageId = image.Id, PerformerId = performerId });
                break;
            case Gallery gallery:
                gallery.GalleryPerformers.Add(new GalleryPerformer { GalleryId = gallery.Id, PerformerId = performerId });
                break;
        }
    }

    private static bool HasStudio(object entity)
    {
        return entity switch
        {
            Scene scene => scene.StudioId.HasValue,
            Image image => image.StudioId.HasValue,
            Gallery gallery => gallery.StudioId.HasValue,
            _ => true,
        };
    }

    private static void SetStudio(object entity, int studioId)
    {
        switch (entity)
        {
            case Scene scene:
                scene.StudioId = studioId;
                break;
            case Image image:
                image.StudioId = studioId;
                break;
            case Gallery gallery:
                gallery.StudioId = studioId;
                break;
        }
    }

    private static bool HasTag(object entity, int tagId)
    {
        return entity switch
        {
            Scene scene => scene.SceneTags.Any(link => link.TagId == tagId),
            Image image => image.ImageTags.Any(link => link.TagId == tagId),
            Gallery gallery => gallery.GalleryTags.Any(link => link.TagId == tagId),
            _ => true,
        };
    }

    private static void AddTag(object entity, int tagId)
    {
        switch (entity)
        {
            case Scene scene:
                scene.SceneTags.Add(new SceneTag { SceneId = scene.Id, TagId = tagId });
                break;
            case Image image:
                image.ImageTags.Add(new ImageTag { ImageId = image.Id, TagId = tagId });
                break;
            case Gallery gallery:
                gallery.GalleryTags.Add(new GalleryTag { GalleryId = gallery.Id, TagId = tagId });
                break;
        }
    }

    private static bool TryGetTagProvenanceHost(object entity, out AffinityHostType hostType, out int hostId)
    {
        switch (entity)
        {
            case Scene scene:
                hostType = AffinityHostType.Scene;
                hostId = scene.Id;
                return true;
            case Image image:
                hostType = AffinityHostType.Image;
                hostId = image.Id;
                return true;
            case Gallery gallery:
                hostType = AffinityHostType.Gallery;
                hostId = gallery.Id;
                return true;
            default:
                hostType = default;
                hostId = 0;
                return false;
        }
    }

    private sealed record AutoTagContentWorkItem(AutoTagContentCandidate Candidate, object Entity);

    private sealed record PreparedAutoTagPhrase(string NormalizedPhrase, int CompactLength, string[] Tokens);
    private sealed record PreparedAutoTagEntityCandidate(AutoTagEntityCandidate Candidate, IReadOnlyList<PreparedAutoTagPhrase> Phrases);

    private sealed class AutoTagCandidateSet
    {
        private readonly Dictionary<string, List<PreparedAutoTagEntityCandidate>> candidatesByToken = new(StringComparer.Ordinal);

        public AutoTagCandidateSet(IReadOnlyList<PreparedAutoTagEntityCandidate> candidates, IReadOnlyList<IAutoTagMatcher> matchers)
        {
            Candidates = candidates;
            Matchers = matchers;
            RawCandidates = candidates.Select(candidate => candidate.Candidate).ToList();

            foreach (var candidate in candidates)
            {
                foreach (var token in candidate.Phrases.Where(phrase => phrase.CompactLength >= 3).SelectMany(phrase => phrase.Tokens).Where(token => token.Length > 0).Distinct(StringComparer.Ordinal))
                {
                    if (!candidatesByToken.TryGetValue(token, out var bucket))
                    {
                        bucket = [];
                        candidatesByToken[token] = bucket;
                    }

                    bucket.Add(candidate);
                }
            }
        }

        public IReadOnlyList<PreparedAutoTagEntityCandidate> Candidates { get; }
        public IReadOnlyList<IAutoTagMatcher> Matchers { get; }
        public IReadOnlyList<AutoTagEntityCandidate> RawCandidates { get; }

        public IReadOnlyList<PreparedAutoTagEntityCandidate> GetCandidates(string normalizedSearchText)
        {
            if (Matchers.Count > 0)
                return Candidates;

            var tokens = normalizedSearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
                return [];

            var result = new List<PreparedAutoTagEntityCandidate>();
            var seenIds = new HashSet<int>();
            foreach (var token in tokens)
            {
                if (!candidatesByToken.TryGetValue(token, out var candidates))
                    continue;

                foreach (var candidate in candidates)
                {
                    if (seenIds.Add(candidate.Candidate.EntityId))
                        result.Add(candidate);
                }
            }

            return result;
        }
    }

    /// <summary>Adapts the core IJobProgress to the extension IJobProgress.</summary>
    private sealed class ProgressAdapter(Cove.Core.Interfaces.IJobProgress inner) : Cove.Plugins.IJobProgress
    {
        public void Report(double percent, string? message = null) => inner.Report(percent / 100.0, message);
    }
}
