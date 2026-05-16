using System.Text.Json;
using System.Text;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public class ScrapeAttemptService(CoveContext db, ScraperService scraperService, ISceneCoverService sceneCoverService, PerformerScrapeService performerScrapeService, ITagProvenanceService tagProvenanceService, ILogger<ScrapeAttemptService> logger, IFieldProvenanceService? fieldProvenanceService = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ScrapeAttemptDto> CreateAttemptAsync(CreateScrapeAttemptDto dto, CancellationToken ct = default)
    {
        var inputKind = dto.InputKind?.Trim().ToLowerInvariant();
        if (inputKind is not ("url" or "name" or "fragment"))
            throw new ArgumentException($"Unsupported scrape input kind '{dto.InputKind}'.", nameof(dto));

        var fragmentInput = inputKind == "fragment"
            ? await BuildFragmentInputAsync(dto, ct)
            : dto.Fragment;

        var inputJson = inputKind switch
        {
            "url" => JsonSerializer.Serialize(new Dictionary<string, object?> { ["url"] = dto.Url }, JsonOptions),
            "name" => JsonSerializer.Serialize(new Dictionary<string, object?> { ["name"] = dto.Name }, JsonOptions),
            _ => JsonSerializer.Serialize(fragmentInput ?? new Dictionary<string, object>(), JsonOptions),
        };

        var attempt = new ScrapeAttempt
        {
            ScraperId = dto.ScraperId,
            EntityType = dto.EntityType,
            EntityId = dto.EntityId,
            InputKind = inputKind,
            InputJson = inputJson,
            EntitySnapshotJson = await BuildEntitySnapshotJsonAsync(dto.EntityType, dto.EntityId, ct),
        };

        try
        {
            Dictionary<string, object>? result = null;
            List<Dictionary<string, object>>? candidateResults = null;

            switch (inputKind)
            {
                case "url":
                    result = string.IsNullOrWhiteSpace(dto.Url)
                        ? null
                        : await scraperService.ScrapeUrlAsync(dto.ScraperId, dto.EntityType, dto.Url, ct);
                    break;
                case "name":
                    candidateResults = string.IsNullOrWhiteSpace(dto.Name)
                        ? null
                        : await scraperService.ScrapeNameAsync(dto.ScraperId, dto.EntityType, dto.Name, ct);
                    candidateResults = OrderCandidatesBySearchTerm(candidateResults, dto.Name);
                    result = SelectPrimaryCandidate(candidateResults, dto.Name);
                    break;
                default:
                    result = fragmentInput == null
                        ? null
                        : await scraperService.ScrapeFragmentAsync(dto.ScraperId, dto.EntityType, fragmentInput, ct);
                    break;
            }

            if (result == null || result.Count == 0)
            {
                attempt.Status = "Failure";
                attempt.Error = "Scrape returned no results.";
            }
            else
            {
                attempt.Status = "Success";
                attempt.ResultJson = JsonSerializer.Serialize(result, JsonOptions);
                attempt.CandidateResultsJson = candidateResults is { Count: > 1 }
                    ? JsonSerializer.Serialize(candidateResults, JsonOptions)
                    : null;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scrape attempt failed for {ScraperId} {EntityType} {EntityId}", dto.ScraperId, dto.EntityType, dto.EntityId);
            attempt.Status = "Failure";
            attempt.Error = ex.Message;
        }

        db.ScrapeAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return MapAttempt(attempt);
    }

    private async Task<Dictionary<string, object>?> BuildFragmentInputAsync(CreateScrapeAttemptDto dto, CancellationToken ct)
    {
        if (dto.Fragment == null)
            return null;

        var fragment = new Dictionary<string, object>(dto.Fragment, StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(dto.EntityType, "scene", StringComparison.OrdinalIgnoreCase) || dto.EntityId == null)
            return fragment;

        var fingerprints = await db.VideoFiles
            .Where(file => file.SceneId == dto.EntityId.Value)
            .SelectMany(file => file.Fingerprints.Select(fingerprint => new { fingerprint.Type, fingerprint.Value }))
            .ToListAsync(ct);

        foreach (var type in new[] { "phash", "oshash", "md5" })
        {
            if (fragment.ContainsKey(type))
                continue;

            var value = fingerprints
                .Where(fingerprint => string.Equals(fingerprint.Type, type, StringComparison.OrdinalIgnoreCase))
                .Select(fingerprint => fingerprint.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(value))
                fragment[type] = value;
        }

        return fragment;
    }

    public async Task<IReadOnlyList<ScrapeAttemptDto>> ListAttemptsAsync(string? entityType, int? entityId, int limit = 20, CancellationToken ct = default)
    {
        var query = db.ScrapeAttempts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(attempt => attempt.EntityType == entityType);

        if (entityId.HasValue)
            query = query.Where(attempt => attempt.EntityId == entityId.Value);

        return await query
            .OrderByDescending(attempt => attempt.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(attempt => MapAttempt(attempt))
            .ToListAsync(ct);
    }

    public async Task<ScrapeAttemptDto?> GetAttemptAsync(Guid id, CancellationToken ct = default)
    {
        var attempt = await db.ScrapeAttempts.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, ct);
        return attempt == null ? null : MapAttempt(attempt);
    }

    public Task<ScrapeAttemptDto?> ApplySceneAttemptAsync(Guid id, ApplySceneScrapeAttemptDto dto, CancellationToken ct = default)
        => ApplyAttemptAsync(id, dto, ct);

    public async Task<ScrapeAttemptDto?> ApplyAttemptAsync(Guid id, ApplySceneScrapeAttemptDto dto, CancellationToken ct = default)
    {
        var attempt = await db.ScrapeAttempts.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (attempt == null || attempt.EntityId == null)
            return null;

        var resultJson = ResolveResultJson(attempt, dto.SelectedCandidateIndex);
        if (string.IsNullOrWhiteSpace(resultJson))
            throw new InvalidOperationException("Scrape attempt does not contain a result to apply.");

        attempt.ResultJson = resultJson;

        return attempt.EntityType.Trim().ToLowerInvariant() switch
        {
            EntityKinds.Scene => await ApplySceneAttemptInternalAsync(attempt, dto, resultJson, ct),
            EntityKinds.Audio => await ApplyAudioAttemptInternalAsync(attempt, dto, resultJson, ct),
            EntityKinds.Text => await ApplyTextAttemptInternalAsync(attempt, dto, resultJson, ct),
            EntityKinds.Image => await ApplyImageAttemptInternalAsync(attempt, dto, resultJson, ct),
            _ => throw new InvalidOperationException($"Scrape apply is not supported for entity type '{attempt.EntityType}'."),
        };
    }

    private async Task<ScrapeAttemptDto?> ApplySceneAttemptInternalAsync(ScrapeAttempt attempt, ApplySceneScrapeAttemptDto dto, string resultJson, CancellationToken ct)
    {
        if (!string.Equals(attempt.EntityType, EntityKinds.Scene, StringComparison.OrdinalIgnoreCase) || attempt.EntityId == null)
            return null;

        var scene = await db.Scenes
            .Include(item => item.Urls)
            .Include(item => item.SceneTags).ThenInclude(item => item.Tag)
            .Include(item => item.ScenePerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == attempt.EntityId.Value, ct);

        if (scene == null)
            return null;

        attempt.EntitySnapshotJson = await BuildSceneSnapshotJsonAsync(scene.Id, ct);

        using var resultDocument = JsonDocument.Parse(resultJson);
        var root = resultDocument.RootElement;
        var replaceFields = new HashSet<string>(dto.ReplaceFields ?? [], StringComparer.OrdinalIgnoreCase);
        var collectionModes = new Dictionary<string, string>(dto.CollectionModes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var tagSelections = BuildSelectionLookup(dto.TagSelections);
        var performerSelections = BuildSelectionLookup(dto.PerformerSelections);

        var availableFields = GetAvailableSceneFields(root);

        if (replaceFields.Contains("title"))
        {
            var title = GetString(root, "Title", "Name");
            if (!string.IsNullOrWhiteSpace(title))
                scene.Title = title;
        }

        if (replaceFields.Contains("code"))
        {
            var code = GetString(root, "Code");
            if (!string.IsNullOrWhiteSpace(code))
                scene.Code = code;
        }

        if (replaceFields.Contains("details"))
        {
            var details = GetString(root, "Details", "Description", "Synopsis");
            if (!string.IsNullOrWhiteSpace(details))
                scene.Details = details;
        }

        if (replaceFields.Contains("director"))
        {
            var director = GetString(root, "Director");
            if (!string.IsNullOrWhiteSpace(director))
                scene.Director = director;
        }

        if (replaceFields.Contains("date"))
        {
            var date = GetString(root, "Date", "ReleaseDate");
            if (ScrapedSceneDateParser.TryParse(date, out var parsedDate))
                scene.Date = parsedDate;
        }

        if (replaceFields.Contains("image"))
        {
            var imageUrl = GetString(root, "Image", "ImageUrl", "ImageURL");
            await sceneCoverService.TryApplyRemoteCoverAsync(scene, imageUrl, ct);
        }

        ApplyUrls(scene, root, collectionModes);
        await ApplyTagsAsync(scene, root, collectionModes, dto.CreateMissingTags, tagSelections, ct);
        await ApplyPerformersAsync(scene, root, collectionModes, dto.CreateMissingPerformers, performerSelections, ct);
        if (dto.HydratePerformers)
            await HydratePerformersAsync(root, dto.CreateMissingPerformers, dto.CreateMissingTags, performerSelections, ct);
        await ApplyStudioAsync(scene, root, collectionModes, dto.CreateMissingStudio, ct);

        var fieldProvenance = BuildAppliedSceneFieldProvenance(root, replaceFields, collectionModes, tagSelections, performerSelections);
        if (fieldProvenance.Count > 0 && fieldProvenanceService != null)
            await fieldProvenanceService.RecordManyAsync(AffinityHostType.Scene, scene.Id, fieldProvenance, "scraper", sourceRunId: attempt.Id.ToString(), cancellationToken: ct);

        if (dto.MarkOrganized)
            scene.Organized = true;

        attempt.AppliedAt = DateTime.UtcNow;
        attempt.Status = DetermineApplyStatus(availableFields, replaceFields, collectionModes, dto);

        await db.SaveChangesAsync(ct);
        return MapAttempt(attempt);
    }

    public async Task<ScrapeAttemptDto?> ApplySceneAttemptWithDefaultPlanAsync(Guid id, ApplySceneScrapeAttemptDto dto, CancellationToken ct = default)
    {
        var attempt = await db.ScrapeAttempts.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, ct);
        if (attempt == null || !string.Equals(attempt.EntityType, "scene", StringComparison.OrdinalIgnoreCase) || attempt.EntityId == null)
            return null;

        var resultJson = ResolveResultJson(attempt, dto.SelectedCandidateIndex);
        if (string.IsNullOrWhiteSpace(resultJson))
            throw new InvalidOperationException("Scrape attempt does not contain a result to apply.");

        var scene = await db.Scenes
            .AsNoTracking()
            .Include(item => item.Urls)
            .Include(item => item.SceneTags).ThenInclude(item => item.Tag)
            .Include(item => item.ScenePerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == attempt.EntityId.Value, ct);

        if (scene == null)
            return null;

        using var resultDocument = JsonDocument.Parse(resultJson);
        var root = resultDocument.RootElement;

        return await ApplySceneAttemptAsync(id, dto with
        {
            ReplaceFields = BuildDefaultReplaceFields(scene, root),
            CollectionModes = BuildDefaultCollectionModes(scene, root),
        }, ct);
    }

    private async Task<string?> BuildEntitySnapshotJsonAsync(string entityType, int? entityId, CancellationToken ct)
    {
        if (entityId == null)
            return null;

        return entityType.Trim().ToLowerInvariant() switch
        {
            EntityKinds.Scene => await BuildSceneSnapshotJsonAsync(entityId.Value, ct),
            EntityKinds.Audio => await BuildAudioSnapshotJsonAsync(entityId.Value, ct),
            EntityKinds.Text => await BuildTextSnapshotJsonAsync(entityId.Value, ct),
            EntityKinds.Image => await BuildImageSnapshotJsonAsync(entityId.Value, ct),
            _ => null,
        };
    }

    private async Task<string?> BuildSceneSnapshotJsonAsync(int sceneId, CancellationToken ct)
    {
        var scene = await db.Scenes
            .AsNoTracking()
            .Include(item => item.Urls)
            .Include(item => item.SceneTags).ThenInclude(item => item.Tag)
            .Include(item => item.ScenePerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == sceneId, ct);

        if (scene == null)
            return null;

        var snapshot = new
        {
            title = scene.Title,
            code = scene.Code,
            details = scene.Details,
            director = scene.Director,
            date = scene.Date?.ToString("yyyy-MM-dd"),
            urls = scene.Urls.Select(item => item.Url).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            studio = scene.Studio?.Name,
            tags = scene.SceneTags.Where(item => item.Tag != null).Select(item => item.Tag!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            performers = scene.ScenePerformers.Where(item => item.Performer != null).Select(item => item.Performer!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            organized = scene.Organized,
        };

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private async Task<string?> BuildAudioSnapshotJsonAsync(int audioId, CancellationToken ct)
    {
        var audio = await db.Audios
            .AsNoTracking()
            .Include(item => item.Urls)
            .Include(item => item.AudioTags).ThenInclude(item => item.Tag)
            .Include(item => item.AudioPerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == audioId, ct);

        if (audio == null)
            return null;

        var snapshot = new
        {
            title = audio.Title,
            code = audio.Code,
            details = audio.Details,
            date = audio.Date?.ToString("yyyy-MM-dd"),
            urls = audio.Urls.Select(item => item.Url).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            studio = audio.Studio?.Name,
            tags = audio.AudioTags.Where(item => item.Tag != null).Select(item => item.Tag!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            performers = audio.AudioPerformers.Where(item => item.Performer != null).Select(item => item.Performer!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            organized = audio.Organized,
        };

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private async Task<string?> BuildTextSnapshotJsonAsync(int textDocumentId, CancellationToken ct)
    {
        var textDocument = await db.TextDocuments
            .AsNoTracking()
            .Include(item => item.Urls)
            .Include(item => item.TextTags).ThenInclude(item => item.Tag)
            .Include(item => item.TextPerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == textDocumentId, ct);

        if (textDocument == null)
            return null;

        var snapshot = new
        {
            title = textDocument.Title,
            code = textDocument.Code,
            details = textDocument.Details,
            date = textDocument.Date?.ToString("yyyy-MM-dd"),
            urls = textDocument.Urls.Select(item => item.Url).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            studio = textDocument.Studio?.Name,
            tags = textDocument.TextTags.Where(item => item.Tag != null).Select(item => item.Tag!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            performers = textDocument.TextPerformers.Where(item => item.Performer != null).Select(item => item.Performer!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            organized = textDocument.Organized,
        };

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private async Task<string?> BuildImageSnapshotJsonAsync(int imageId, CancellationToken ct)
    {
        var image = await db.Images
            .AsNoTracking()
            .Include(item => item.Urls)
            .Include(item => item.ImageTags).ThenInclude(item => item.Tag)
            .Include(item => item.ImagePerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == imageId, ct);

        if (image == null)
            return null;

        var snapshot = new
        {
            title = image.Title,
            code = image.Code,
            details = image.Details,
            photographer = image.Photographer,
            date = image.Date?.ToString("yyyy-MM-dd"),
            urls = image.Urls.Select(item => item.Url).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            studio = image.Studio?.Name,
            tags = image.ImageTags.Where(item => item.Tag != null).Select(item => item.Tag!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            performers = image.ImagePerformers.Where(item => item.Performer != null).Select(item => item.Performer!.Name).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            organized = image.Organized,
        };

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private async Task<ScrapeAttemptDto?> ApplyAudioAttemptInternalAsync(ScrapeAttempt attempt, ApplySceneScrapeAttemptDto dto, string resultJson, CancellationToken ct)
    {
        if (!string.Equals(attempt.EntityType, EntityKinds.Audio, StringComparison.OrdinalIgnoreCase) || attempt.EntityId == null)
            return null;

        var audio = await db.Audios
            .Include(item => item.Urls)
            .Include(item => item.AudioTags).ThenInclude(item => item.Tag)
            .Include(item => item.AudioPerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == attempt.EntityId.Value, ct);

        if (audio == null)
            return null;

        attempt.EntitySnapshotJson = await BuildAudioSnapshotJsonAsync(audio.Id, ct);

        using var resultDocument = JsonDocument.Parse(resultJson);
        var root = resultDocument.RootElement;
        var replaceFields = new HashSet<string>(dto.ReplaceFields ?? [], StringComparer.OrdinalIgnoreCase);
        var collectionModes = new Dictionary<string, string>(dto.CollectionModes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var tagSelections = BuildSelectionLookup(dto.TagSelections);
        var performerSelections = BuildSelectionLookup(dto.PerformerSelections);
        var availableFields = GetAvailableAudioFields(root);

        if (replaceFields.Contains("title"))
        {
            var title = GetString(root, "Title", "Name");
            if (!string.IsNullOrWhiteSpace(title))
                audio.Title = title;
        }

        if (replaceFields.Contains("code"))
        {
            var code = GetString(root, "Code");
            if (!string.IsNullOrWhiteSpace(code))
                audio.Code = code;
        }

        if (replaceFields.Contains("details"))
        {
            var details = GetString(root, "Details", "Description", "Synopsis");
            if (!string.IsNullOrWhiteSpace(details))
                audio.Details = details;
        }

        if (replaceFields.Contains("date"))
        {
            var date = GetString(root, "Date", "ReleaseDate");
            if (ScrapedSceneDateParser.TryParse(date, out var parsedDate))
                audio.Date = parsedDate;
        }

        ApplyAudioUrls(audio, root, collectionModes);
        await ApplyAudioTagsAsync(audio, root, collectionModes, dto.CreateMissingTags, tagSelections, ct);
        await ApplyAudioPerformersAsync(audio, root, collectionModes, dto.CreateMissingPerformers, performerSelections, ct);
        await ApplyAudioStudioAsync(audio, root, collectionModes, dto.CreateMissingStudio, ct);

        var fieldProvenance = BuildAppliedAudioFieldProvenance(root, replaceFields, collectionModes, tagSelections, performerSelections);
        if (fieldProvenance.Count > 0 && fieldProvenanceService != null)
            await fieldProvenanceService.RecordManyAsync(AffinityHostType.Audio, audio.Id, fieldProvenance, "scraper", sourceRunId: attempt.Id.ToString(), cancellationToken: ct);

        if (dto.MarkOrganized)
            audio.Organized = true;

        attempt.AppliedAt = DateTime.UtcNow;
        attempt.Status = DetermineApplyStatus(availableFields, replaceFields, collectionModes, dto);

        await db.SaveChangesAsync(ct);
        await RefreshAudioArraysAsync(audio, ct);
        return MapAttempt(attempt);
    }

    private async Task<ScrapeAttemptDto?> ApplyTextAttemptInternalAsync(ScrapeAttempt attempt, ApplySceneScrapeAttemptDto dto, string resultJson, CancellationToken ct)
    {
        if (!string.Equals(attempt.EntityType, EntityKinds.Text, StringComparison.OrdinalIgnoreCase) || attempt.EntityId == null)
            return null;

        var textDocument = await db.TextDocuments
            .Include(item => item.Urls)
            .Include(item => item.TextTags).ThenInclude(item => item.Tag)
            .Include(item => item.TextPerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == attempt.EntityId.Value, ct);

        if (textDocument == null)
            return null;

        attempt.EntitySnapshotJson = await BuildTextSnapshotJsonAsync(textDocument.Id, ct);

        using var resultDocument = JsonDocument.Parse(resultJson);
        var root = resultDocument.RootElement;
        var replaceFields = new HashSet<string>(dto.ReplaceFields ?? [], StringComparer.OrdinalIgnoreCase);
        var collectionModes = new Dictionary<string, string>(dto.CollectionModes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var tagSelections = BuildSelectionLookup(dto.TagSelections);
        var performerSelections = BuildSelectionLookup(dto.PerformerSelections);
        var availableFields = GetAvailableTextFields(root);

        if (replaceFields.Contains("title"))
        {
            var title = GetString(root, "Title", "Name");
            if (!string.IsNullOrWhiteSpace(title))
                textDocument.Title = title;
        }

        if (replaceFields.Contains("code"))
        {
            var code = GetString(root, "Code");
            if (!string.IsNullOrWhiteSpace(code))
                textDocument.Code = code;
        }

        if (replaceFields.Contains("details"))
        {
            var details = GetString(root, "Details", "Description", "Synopsis");
            if (!string.IsNullOrWhiteSpace(details))
                textDocument.Details = details;
        }

        if (replaceFields.Contains("date"))
        {
            var date = GetString(root, "Date", "ReleaseDate");
            if (ScrapedSceneDateParser.TryParse(date, out var parsedDate))
                textDocument.Date = parsedDate;
        }

        ApplyTextUrls(textDocument, root, collectionModes);
        await ApplyTextTagsAsync(textDocument, root, collectionModes, dto.CreateMissingTags, tagSelections, ct);
        await ApplyTextPerformersAsync(textDocument, root, collectionModes, dto.CreateMissingPerformers, performerSelections, ct);
        await ApplyTextStudioAsync(textDocument, root, collectionModes, dto.CreateMissingStudio, ct);

        var fieldProvenance = BuildAppliedTextFieldProvenance(root, replaceFields, collectionModes, tagSelections, performerSelections);
        if (fieldProvenance.Count > 0 && fieldProvenanceService != null)
            await fieldProvenanceService.RecordManyAsync(AffinityHostType.Text, textDocument.Id, fieldProvenance, "scraper", sourceRunId: attempt.Id.ToString(), cancellationToken: ct);

        if (dto.MarkOrganized)
            textDocument.Organized = true;

        attempt.AppliedAt = DateTime.UtcNow;
        attempt.Status = DetermineApplyStatus(availableFields, replaceFields, collectionModes, dto);

        await db.SaveChangesAsync(ct);
        await RefreshTextArraysAsync(textDocument, ct);
        return MapAttempt(attempt);
    }

    private async Task<ScrapeAttemptDto?> ApplyImageAttemptInternalAsync(ScrapeAttempt attempt, ApplySceneScrapeAttemptDto dto, string resultJson, CancellationToken ct)
    {
        if (!string.Equals(attempt.EntityType, EntityKinds.Image, StringComparison.OrdinalIgnoreCase) || attempt.EntityId == null)
            return null;

        var image = await db.Images
            .Include(item => item.Urls)
            .Include(item => item.ImageTags).ThenInclude(item => item.Tag)
            .Include(item => item.ImagePerformers).ThenInclude(item => item.Performer)
            .Include(item => item.Studio)
            .FirstOrDefaultAsync(item => item.Id == attempt.EntityId.Value, ct);

        if (image == null)
            return null;

        attempt.EntitySnapshotJson = await BuildImageSnapshotJsonAsync(image.Id, ct);

        using var resultDocument = JsonDocument.Parse(resultJson);
        var root = resultDocument.RootElement;
        var replaceFields = new HashSet<string>(dto.ReplaceFields ?? [], StringComparer.OrdinalIgnoreCase);
        var collectionModes = new Dictionary<string, string>(dto.CollectionModes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var tagSelections = BuildSelectionLookup(dto.TagSelections);
        var performerSelections = BuildSelectionLookup(dto.PerformerSelections);
        var availableFields = GetAvailableImageFields(root);

        if (replaceFields.Contains("title"))
        {
            var title = GetString(root, "Title", "Name");
            if (!string.IsNullOrWhiteSpace(title))
                image.Title = title;
        }

        if (replaceFields.Contains("code"))
        {
            var code = GetString(root, "Code");
            if (!string.IsNullOrWhiteSpace(code))
                image.Code = code;
        }

        if (replaceFields.Contains("details"))
        {
            var details = GetString(root, "Details", "Description", "Synopsis");
            if (!string.IsNullOrWhiteSpace(details))
                image.Details = details;
        }

        if (replaceFields.Contains("photographer"))
        {
            var photographer = GetString(root, "Photographer");
            if (!string.IsNullOrWhiteSpace(photographer))
                image.Photographer = photographer;
        }

        if (replaceFields.Contains("date"))
        {
            var date = GetString(root, "Date", "ReleaseDate");
            if (ScrapedSceneDateParser.TryParse(date, out var parsedDate))
                image.Date = parsedDate;
        }

        ApplyImageUrls(image, root, collectionModes);
        await ApplyImageTagsAsync(image, root, collectionModes, dto.CreateMissingTags, tagSelections, ct);
        await ApplyImagePerformersAsync(image, root, collectionModes, dto.CreateMissingPerformers, performerSelections, ct);
        await ApplyImageStudioAsync(image, root, collectionModes, dto.CreateMissingStudio, ct);

        var fieldProvenance = BuildAppliedImageFieldProvenance(root, replaceFields, collectionModes, tagSelections, performerSelections);
        if (fieldProvenance.Count > 0 && fieldProvenanceService != null)
            await fieldProvenanceService.RecordManyAsync(AffinityHostType.Image, image.Id, fieldProvenance, "scraper", sourceRunId: attempt.Id.ToString(), cancellationToken: ct);

        if (dto.MarkOrganized)
            image.Organized = true;

        attempt.AppliedAt = DateTime.UtcNow;
        attempt.Status = DetermineApplyStatus(availableFields, replaceFields, collectionModes, dto);

        await db.SaveChangesAsync(ct);
        await RefreshImageArraysAsync(image, ct);
        return MapAttempt(attempt);
    }

    private static void ApplyAudioUrls(Audio audio, JsonElement root, IDictionary<string, string> collectionModes)
    {
        var mode = GetMode(collectionModes, "urls");
        if (mode == "skip")
            return;

        var scrapedUrls = GetStringList(root, "URLs", "Url", "URL");
        if (scrapedUrls.Count == 0)
            return;

        var existing = audio.Urls.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            audio.Urls.Clear();
            foreach (var url in scrapedUrls)
                audio.Urls.Add(new AudioUrl { AudioId = audio.Id, Url = url });
            return;
        }

        foreach (var url in scrapedUrls)
        {
            if (existing.Add(url))
                audio.Urls.Add(new AudioUrl { AudioId = audio.Id, Url = url });
        }
    }

    private static void ApplyTextUrls(TextDocument textDocument, JsonElement root, IDictionary<string, string> collectionModes)
    {
        var mode = GetMode(collectionModes, "urls");
        if (mode == "skip")
            return;

        var scrapedUrls = GetStringList(root, "URLs", "Url", "URL");
        if (scrapedUrls.Count == 0)
            return;

        var existing = textDocument.Urls.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            textDocument.Urls.Clear();
            foreach (var url in scrapedUrls)
                textDocument.Urls.Add(new TextUrl { TextDocumentId = textDocument.Id, Url = url });
            return;
        }

        foreach (var url in scrapedUrls)
        {
            if (existing.Add(url))
                textDocument.Urls.Add(new TextUrl { TextDocumentId = textDocument.Id, Url = url });
        }
    }

    private static void ApplyImageUrls(Image image, JsonElement root, IDictionary<string, string> collectionModes)
    {
        var mode = GetMode(collectionModes, "urls");
        if (mode == "skip")
            return;

        var scrapedUrls = GetStringList(root, "URLs", "Url", "URL");
        if (scrapedUrls.Count == 0)
            return;

        var existing = image.Urls.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            image.Urls.Clear();
            foreach (var url in scrapedUrls)
                image.Urls.Add(new ImageUrl { ImageId = image.Id, Url = url });
            return;
        }

        foreach (var url in scrapedUrls)
        {
            if (existing.Add(url))
                image.Urls.Add(new ImageUrl { ImageId = image.Id, Url = url });
        }
    }

    private async Task ApplyAudioTagsAsync(Audio audio, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "tags");
        if (mode == "skip")
            return;

        var tagNames = GetTagNames(root, "Tags", "Tag", "TagNames");
        if (tagNames.Count == 0)
            return;

        var selectedTagNames = ResolveSelectedRelationNames(tagNames, selections, createMissing);
        if (selectedTagNames.Count == 0)
        {
            if (mode == "replace")
                audio.AudioTags.Clear();
            return;
        }

        var normalizedTagNames = selectedTagNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();
        var tagLookup = await db.Tags
            .Where(tag => normalizedTagNames.Contains(tag.Name.ToLower()))
            .ToDictionaryAsync(tag => tag.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existingTagNames = audio.AudioTags
            .Where(item => item.Tag != null)
            .Select(item => item.Tag!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            audio.AudioTags.Clear();
            existingTagNames.Clear();
        }

        foreach (var selectedTag in selectedTagNames)
        {
            var tagName = selectedTag.Name;
            if (!tagLookup.TryGetValue(tagName, out var tag))
            {
                if (!selectedTag.AllowCreate)
                    continue;

                tag = new Tag { Name = tagName };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
                tagLookup[tagName] = tag;
            }

            if (!existingTagNames.Add(tag.Name))
                continue;

            audio.AudioTags.Add(new AudioTag { AudioId = audio.Id, TagId = tag.Id, Tag = tag });
            await tagProvenanceService.RecordAsync(AffinityHostType.Audio, audio.Id, tag, "scraper", cancellationToken: ct);
        }
    }

    private async Task ApplyTextTagsAsync(TextDocument textDocument, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "tags");
        if (mode == "skip")
            return;

        var tagNames = GetTagNames(root, "Tags", "Tag", "TagNames");
        if (tagNames.Count == 0)
            return;

        var selectedTagNames = ResolveSelectedRelationNames(tagNames, selections, createMissing);
        if (selectedTagNames.Count == 0)
        {
            if (mode == "replace")
                textDocument.TextTags.Clear();
            return;
        }

        var normalizedTagNames = selectedTagNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();
        var tagLookup = await db.Tags
            .Where(tag => normalizedTagNames.Contains(tag.Name.ToLower()))
            .ToDictionaryAsync(tag => tag.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existingTagNames = textDocument.TextTags
            .Where(item => item.Tag != null)
            .Select(item => item.Tag!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            textDocument.TextTags.Clear();
            existingTagNames.Clear();
        }

        foreach (var selectedTag in selectedTagNames)
        {
            var tagName = selectedTag.Name;
            if (!tagLookup.TryGetValue(tagName, out var tag))
            {
                if (!selectedTag.AllowCreate)
                    continue;

                tag = new Tag { Name = tagName };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
                tagLookup[tagName] = tag;
            }

            if (!existingTagNames.Add(tag.Name))
                continue;

            textDocument.TextTags.Add(new TextTag { TextDocumentId = textDocument.Id, TagId = tag.Id, Tag = tag });
            await tagProvenanceService.RecordAsync(AffinityHostType.Text, textDocument.Id, tag, "scraper", cancellationToken: ct);
        }
    }

    private async Task ApplyImageTagsAsync(Image image, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "tags");
        if (mode == "skip")
            return;

        var tagNames = GetTagNames(root, "Tags", "Tag", "TagNames");
        if (tagNames.Count == 0)
            return;

        var selectedTagNames = ResolveSelectedRelationNames(tagNames, selections, createMissing);
        if (selectedTagNames.Count == 0)
        {
            if (mode == "replace")
                image.ImageTags.Clear();
            return;
        }

        var normalizedTagNames = selectedTagNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();
        var tagLookup = await db.Tags
            .Where(tag => normalizedTagNames.Contains(tag.Name.ToLower()))
            .ToDictionaryAsync(tag => tag.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existingTagNames = image.ImageTags
            .Where(item => item.Tag != null)
            .Select(item => item.Tag!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            image.ImageTags.Clear();
            existingTagNames.Clear();
        }

        foreach (var selectedTag in selectedTagNames)
        {
            var tagName = selectedTag.Name;
            if (!tagLookup.TryGetValue(tagName, out var tag))
            {
                if (!selectedTag.AllowCreate)
                    continue;

                tag = new Tag { Name = tagName };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
                tagLookup[tagName] = tag;
            }

            if (!existingTagNames.Add(tag.Name))
                continue;

            image.ImageTags.Add(new ImageTag { ImageId = image.Id, TagId = tag.Id, Tag = tag });
            await tagProvenanceService.RecordAsync(AffinityHostType.Image, image.Id, tag, "scraper", cancellationToken: ct);
        }
    }

    private async Task ApplyAudioPerformersAsync(Audio audio, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "performers");
        if (mode == "skip")
            return;

        var performerNames = GetAudioPerformerNames(root);
        if (performerNames.Count == 0)
            return;

        var selectedPerformerNames = ResolveSelectedRelationNames(performerNames, selections, createMissing);
        if (selectedPerformerNames.Count == 0)
        {
            if (mode == "replace")
                audio.AudioPerformers.Clear();
            return;
        }

        var normalizedPerformerNames = selectedPerformerNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();
        var performerLookup = await db.Performers
            .Where(performer => normalizedPerformerNames.Contains(performer.Name.ToLower()))
            .ToDictionaryAsync(performer => performer.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existingPerformerNames = audio.AudioPerformers
            .Where(item => item.Performer != null)
            .Select(item => item.Performer!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            audio.AudioPerformers.Clear();
            existingPerformerNames.Clear();
        }

        foreach (var selectedPerformer in selectedPerformerNames)
        {
            var performerName = selectedPerformer.Name;
            if (!performerLookup.TryGetValue(performerName, out var performer))
            {
                if (!selectedPerformer.AllowCreate)
                    continue;

                performer = new Performer { Name = performerName };
                db.Performers.Add(performer);
                await db.SaveChangesAsync(ct);
                performerLookup[performerName] = performer;
            }

            if (existingPerformerNames.Add(performer.Name))
                audio.AudioPerformers.Add(new AudioPerformer { AudioId = audio.Id, PerformerId = performer.Id, Performer = performer });
        }
    }

    private async Task ApplyTextPerformersAsync(TextDocument textDocument, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "performers");
        if (mode == "skip")
            return;

        var performerNames = GetTextPerformerNames(root);
        if (performerNames.Count == 0)
            return;

        var selectedPerformerNames = ResolveSelectedRelationNames(performerNames, selections, createMissing);
        if (selectedPerformerNames.Count == 0)
        {
            if (mode == "replace")
                textDocument.TextPerformers.Clear();
            return;
        }

        var normalizedPerformerNames = selectedPerformerNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();
        var performerLookup = await db.Performers
            .Where(performer => normalizedPerformerNames.Contains(performer.Name.ToLower()))
            .ToDictionaryAsync(performer => performer.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existingPerformerNames = textDocument.TextPerformers
            .Where(item => item.Performer != null)
            .Select(item => item.Performer!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            textDocument.TextPerformers.Clear();
            existingPerformerNames.Clear();
        }

        foreach (var selectedPerformer in selectedPerformerNames)
        {
            var performerName = selectedPerformer.Name;
            if (!performerLookup.TryGetValue(performerName, out var performer))
            {
                if (!selectedPerformer.AllowCreate)
                    continue;

                performer = new Performer { Name = performerName };
                db.Performers.Add(performer);
                await db.SaveChangesAsync(ct);
                performerLookup[performerName] = performer;
            }

            if (existingPerformerNames.Add(performer.Name))
                textDocument.TextPerformers.Add(new TextPerformer { TextDocumentId = textDocument.Id, PerformerId = performer.Id, Performer = performer });
        }
    }

    private async Task ApplyImagePerformersAsync(Image image, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "performers");
        if (mode == "skip")
            return;

        var performerNames = GetNamedItems(root, "Performers", "Performer", "PerformerNames");
        if (performerNames.Count == 0)
            return;

        var selectedPerformerNames = ResolveSelectedRelationNames(performerNames, selections, createMissing);
        if (selectedPerformerNames.Count == 0)
        {
            if (mode == "replace")
                image.ImagePerformers.Clear();
            return;
        }

        var normalizedPerformerNames = selectedPerformerNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();
        var performerLookup = await db.Performers
            .Where(performer => normalizedPerformerNames.Contains(performer.Name.ToLower()))
            .ToDictionaryAsync(performer => performer.Name, StringComparer.OrdinalIgnoreCase, ct);

        var existingPerformerNames = image.ImagePerformers
            .Where(item => item.Performer != null)
            .Select(item => item.Performer!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            image.ImagePerformers.Clear();
            existingPerformerNames.Clear();
        }

        foreach (var selectedPerformer in selectedPerformerNames)
        {
            var performerName = selectedPerformer.Name;
            if (!performerLookup.TryGetValue(performerName, out var performer))
            {
                if (!selectedPerformer.AllowCreate)
                    continue;

                performer = new Performer { Name = performerName };
                db.Performers.Add(performer);
                await db.SaveChangesAsync(ct);
                performerLookup[performerName] = performer;
            }

            if (existingPerformerNames.Add(performer.Name))
                image.ImagePerformers.Add(new ImagePerformer { ImageId = image.Id, PerformerId = performer.Id, Performer = performer });
        }
    }

    private async Task ApplyAudioStudioAsync(Audio audio, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "studio");
        if (mode == "skip")
            return;

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        if (string.IsNullOrWhiteSpace(studioName))
            return;

        var studio = await ResolveStudioAsync(studioName, createMissing, ct);
        if (studio != null)
        {
            audio.Studio = studio;
            audio.StudioId = studio.Id;
        }
    }

    private async Task ApplyTextStudioAsync(TextDocument textDocument, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "studio");
        if (mode == "skip")
            return;

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        if (string.IsNullOrWhiteSpace(studioName))
            return;

        var studio = await ResolveStudioAsync(studioName, createMissing, ct);
        if (studio != null)
        {
            textDocument.Studio = studio;
            textDocument.StudioId = studio.Id;
        }
    }

    private async Task ApplyImageStudioAsync(Image image, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "studio");
        if (mode == "skip")
            return;

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        if (string.IsNullOrWhiteSpace(studioName))
            return;

        var studio = await ResolveStudioAsync(studioName, createMissing, ct);
        if (studio != null)
        {
            image.Studio = studio;
            image.StudioId = studio.Id;
        }
    }

    private async Task<Studio?> ResolveStudioAsync(string studioName, bool createMissing, CancellationToken ct)
    {
        var normalizedStudioName = studioName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStudioName))
            return null;

        var studio = await db.Studios.FirstOrDefaultAsync(item => item.Name.ToLower() == normalizedStudioName.ToLower(), ct);
        if (studio != null)
            return studio;

        if (!createMissing)
            return null;

        studio = new Studio { Name = normalizedStudioName };
        db.Studios.Add(studio);
        await db.SaveChangesAsync(ct);
        return studio;
    }

    private async Task RefreshAudioArraysAsync(Audio audio, CancellationToken ct)
    {
        var nextTagIds = audio.AudioTags
            .Select(item => item.TagId != 0 ? item.TagId : item.Tag?.Id ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var nextPerformerIds = audio.AudioPerformers
            .Select(item => item.PerformerId != 0 ? item.PerformerId : item.Performer?.Id ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (audio.TagIds.SequenceEqual(nextTagIds) && audio.PerformerIds.SequenceEqual(nextPerformerIds))
            return;

        audio.TagIds = nextTagIds;
        audio.PerformerIds = nextPerformerIds;
        await db.SaveChangesAsync(ct);
    }

    private async Task RefreshTextArraysAsync(TextDocument textDocument, CancellationToken ct)
    {
        var nextTagIds = textDocument.TextTags
            .Select(item => item.TagId != 0 ? item.TagId : item.Tag?.Id ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var nextPerformerIds = textDocument.TextPerformers
            .Select(item => item.PerformerId != 0 ? item.PerformerId : item.Performer?.Id ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (textDocument.TagIds.SequenceEqual(nextTagIds) && textDocument.PerformerIds.SequenceEqual(nextPerformerIds))
            return;

        textDocument.TagIds = nextTagIds;
        textDocument.PerformerIds = nextPerformerIds;
        await db.SaveChangesAsync(ct);
    }

    private async Task RefreshImageArraysAsync(Image image, CancellationToken ct)
    {
        var nextTagIds = image.ImageTags
            .Select(item => item.TagId != 0 ? item.TagId : item.Tag?.Id ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var nextPerformerIds = image.ImagePerformers
            .Select(item => item.PerformerId != 0 ? item.PerformerId : item.Performer?.Id ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (image.TagIds.SequenceEqual(nextTagIds) && image.PerformerIds.SequenceEqual(nextPerformerIds))
            return;

        image.TagIds = nextTagIds;
        image.PerformerIds = nextPerformerIds;
        await db.SaveChangesAsync(ct);
    }

    private static void ApplyUrls(Scene scene, JsonElement root, IDictionary<string, string> collectionModes)
    {
        var mode = GetMode(collectionModes, "urls");
        if (mode == "skip")
            return;

        var scrapedUrls = GetStringList(root, "URLs", "Url", "URL");
        if (scrapedUrls.Count == 0)
            return;

        var existing = scene.Urls.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mode == "replace")
        {
            scene.Urls.Clear();
            foreach (var url in scrapedUrls)
                scene.Urls.Add(new SceneUrl { SceneId = scene.Id, Url = url });
            return;
        }

        foreach (var url in scrapedUrls)
        {
            if (existing.Add(url))
                scene.Urls.Add(new SceneUrl { SceneId = scene.Id, Url = url });
        }
    }

    private async Task ApplyTagsAsync(Scene scene, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "tags");
        if (mode == "skip")
            return;

        var tagNames = GetTagNames(root, "Tags", "Tag", "TagNames");
        if (tagNames.Count == 0)
            return;

        var selectedTagNames = ResolveSelectedRelationNames(tagNames, selections, createMissing);
        if (selectedTagNames.Count == 0)
        {
            if (mode == "replace")
                scene.SceneTags.Clear();
            return;
        }

        var normalizedTagNames = selectedTagNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();

        var tagLookup = await db.Tags
            .Where(tag => normalizedTagNames.Contains(tag.Name.ToLower()))
            .ToDictionaryAsync(tag => tag.Name, StringComparer.OrdinalIgnoreCase, ct);

        if (mode == "replace")
            scene.SceneTags.Clear();

        var existingTagIds = scene.SceneTags.Select(item => item.TagId).ToHashSet();
        var appliedTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedTag in selectedTagNames)
        {
            var tagName = selectedTag.Name;
            if (!tagLookup.TryGetValue(tagName, out var tag))
            {
                if (!selectedTag.AllowCreate)
                    continue;

                tag = new Tag { Name = tagName };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
                tagLookup[tagName] = tag;
            }

            appliedTagNames.Add(tag.Name);

            if (existingTagIds.Add(tag.Id))
            {
                scene.SceneTags.Add(new SceneTag { SceneId = scene.Id, TagId = tag.Id, Tag = tag });
                await tagProvenanceService.RecordAsync(AffinityHostType.Scene, scene.Id, tag, "scraper", cancellationToken: ct);
            }
        }

        await ApplyTagHierarchyAsync(root, tagLookup, selections == null && createMissing, appliedTagNames, ct);
    }

    private async Task ApplyTagHierarchyAsync(JsonElement root, Dictionary<string, Tag> tagLookup, bool createMissing, IReadOnlySet<string> selectedTagNames, CancellationToken ct)
    {
        var tagItems = GetObjectItems(root, "Tags", "Tag", "TagNames");
        if (tagItems.Count == 0)
            return;

        var relationKeys = new HashSet<(int ParentId, int ChildId)>();

        foreach (var tagItem in tagItems)
        {
            var tagName = NormalizeTagName(GetString(tagItem, "Name", "name", "Title", "title"));
            if (string.IsNullOrWhiteSpace(tagName))
                continue;
            if (!selectedTagNames.Contains(tagName))
                continue;

            var tag = await ResolveHierarchyTagAsync(tagName, tagLookup, createMissing, ct);
            if (tag == null)
                continue;

            foreach (var parentName in GetTagNames(tagItem, "Parents", "ParentTags", "ParentTag", "Parent"))
            {
                var parent = await ResolveHierarchyTagAsync(parentName, tagLookup, createMissing, ct);
                if (parent == null || parent.Id == tag.Id)
                    continue;
                await AddTagRelationAsync(parent.Id, tag.Id, relationKeys, ct);
            }

            foreach (var childName in GetTagNames(tagItem, "Children", "ChildTags", "ChildTag", "Child"))
            {
                var child = await ResolveHierarchyTagAsync(childName, tagLookup, createMissing, ct);
                if (child == null || child.Id == tag.Id)
                    continue;
                await AddTagRelationAsync(tag.Id, child.Id, relationKeys, ct);
            }
        }
    }

    private async Task<Tag?> ResolveHierarchyTagAsync(string name, Dictionary<string, Tag> tagLookup, bool createMissing, CancellationToken ct)
    {
        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        if (tagLookup.TryGetValue(normalizedName, out var existingTag))
            return existingTag;

        var normalizedKey = normalizedName.ToLowerInvariant();
        var tag = await db.Tags.FirstOrDefaultAsync(item => item.Name.ToLower() == normalizedKey, ct);
        if (tag == null)
        {
            if (!createMissing)
                return null;

            tag = new Tag { Name = normalizedName };
            db.Tags.Add(tag);
            await db.SaveChangesAsync(ct);
        }

        tagLookup[normalizedName] = tag;
        return tag;
    }

    private async Task AddTagRelationAsync(int parentId, int childId, HashSet<(int ParentId, int ChildId)> relationKeys, CancellationToken ct)
    {
        if (!relationKeys.Add((parentId, childId)))
            return;

        var exists = await db.Set<TagParent>().AnyAsync(relation => relation.ParentId == parentId && relation.ChildId == childId, ct);
        if (!exists)
            db.Set<TagParent>().Add(new TagParent { ParentId = parentId, ChildId = childId });
    }

    private async Task ApplyPerformersAsync(Scene scene, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, IReadOnlyDictionary<string, string>? selections, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "performers");
        if (mode == "skip")
            return;

        var performerNames = GetNamedItems(root, "Performers", "Performer", "PerformerNames");
        if (performerNames.Count == 0)
            return;

        var selectedPerformerNames = ResolveSelectedRelationNames(performerNames, selections, createMissing);
        if (selectedPerformerNames.Count == 0)
        {
            if (mode == "replace")
                scene.ScenePerformers.Clear();
            return;
        }

        var normalizedPerformerNames = selectedPerformerNames.Select(item => item.Name.ToLowerInvariant()).ToHashSet();

        var performerLookup = await db.Performers
            .Where(performer => normalizedPerformerNames.Contains(performer.Name.ToLower()))
            .ToDictionaryAsync(performer => performer.Name, StringComparer.OrdinalIgnoreCase, ct);

        if (mode == "replace")
            scene.ScenePerformers.Clear();

        var existingPerformerIds = scene.ScenePerformers.Select(item => item.PerformerId).ToHashSet();
        foreach (var selectedPerformer in selectedPerformerNames)
        {
            var performerName = selectedPerformer.Name;
            if (!performerLookup.TryGetValue(performerName, out var performer))
            {
                if (!selectedPerformer.AllowCreate)
                    continue;

                performer = new Performer { Name = performerName };
                db.Performers.Add(performer);
                await db.SaveChangesAsync(ct);
                performerLookup[performerName] = performer;
            }

            if (existingPerformerIds.Add(performer.Id))
                scene.ScenePerformers.Add(new ScenePerformer { SceneId = scene.Id, PerformerId = performer.Id, Performer = performer });
        }
    }

    private async Task ApplyStudioAsync(Scene scene, JsonElement root, IDictionary<string, string> collectionModes, bool createMissing, CancellationToken ct)
    {
        var mode = GetMode(collectionModes, "studio");
        if (mode == "skip")
            return;

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        if (string.IsNullOrWhiteSpace(studioName))
            return;

        var normalizedStudioName = studioName.ToLowerInvariant();
        var studio = await db.Studios.FirstOrDefaultAsync(item => item.Name.ToLower() == normalizedStudioName, ct);
        if (studio == null && createMissing)
        {
            studio = new Studio { Name = studioName };
            db.Studios.Add(studio);
            await db.SaveChangesAsync(ct);
        }

        if (studio != null)
            scene.StudioId = studio.Id;
    }

    private async Task HydratePerformersAsync(JsonElement root, bool createMissingPerformers, bool createMissingTags, IReadOnlyDictionary<string, string>? performerSelections, CancellationToken ct)
    {
        var performerItems = GetObjectItems(root, "Performers", "Performer");
        var sceneUrl = GetString(root, "URL", "Url", "url");
        foreach (var item in performerItems)
        {
            var sourceUrl = ResolveAbsoluteUrl(GetString(item, "URL", "Url", "url"), sceneUrl);

            var scraped = string.IsNullOrWhiteSpace(sourceUrl) ? null : await performerScrapeService.ScrapeByUrlAsync(sourceUrl, ct);
            var performerName = scraped?.Name ?? GetString(item, "Name", "name", "Title", "title");
            if (string.IsNullOrWhiteSpace(performerName))
                continue;

            var selectedPerformer = ResolveSelectedRelationNames([performerName], performerSelections, createMissingPerformers).FirstOrDefault();
            if (selectedPerformer == null)
                continue;

            var performer = await db.Performers
                .Include(candidate => candidate.Urls)
                .Include(candidate => candidate.Aliases)
                .Include(candidate => candidate.PerformerTags)
                .FirstOrDefaultAsync(candidate => candidate.Name.ToLower() == performerName.ToLower(), ct);

            if (performer == null)
            {
                if (!selectedPerformer.AllowCreate)
                    continue;

                performer = new Performer { Name = performerName };
                db.Performers.Add(performer);
            }

            if (!string.IsNullOrWhiteSpace(sourceUrl)
                && !performer.Urls.Any(candidate => string.Equals(candidate.Url, sourceUrl, StringComparison.OrdinalIgnoreCase)))
            {
                performer.Urls.Add(new PerformerUrl { Performer = performer, Url = sourceUrl });
            }

            if (scraped != null)
                await performerScrapeService.ApplyAsync(performer, scraped, createMissingTags, ct: ct);
        }
    }

    private static HashSet<string> GetAvailableSceneFields(JsonElement root)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(GetString(root, "Title", "Name"))) available.Add("title");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Code"))) available.Add("code");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Details", "Description", "Synopsis"))) available.Add("details");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Director"))) available.Add("director");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Date", "ReleaseDate"))) available.Add("date");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Image", "ImageUrl", "ImageURL"))) available.Add("image");
        if (GetStringList(root, "URLs", "Url", "URL").Count > 0) available.Add("urls");
        if (GetTagNames(root, "Tags", "Tag", "TagNames").Count > 0) available.Add("tags");
        if (GetNamedItems(root, "Performers", "Performer", "PerformerNames").Count > 0) available.Add("performers");
        if (!string.IsNullOrWhiteSpace(GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName"))) available.Add("studio");
        return available;
    }

    private static HashSet<string> GetAvailableAudioFields(JsonElement root)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(GetString(root, "Title", "Name"))) available.Add("title");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Code"))) available.Add("code");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Details", "Description", "Synopsis"))) available.Add("details");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Date", "ReleaseDate"))) available.Add("date");
        if (GetStringList(root, "URLs", "Url", "URL").Count > 0) available.Add("urls");
        if (GetTagNames(root, "Tags", "Tag", "TagNames").Count > 0) available.Add("tags");
        if (GetAudioPerformerNames(root).Count > 0) available.Add("performers");
        if (!string.IsNullOrWhiteSpace(GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName"))) available.Add("studio");
        return available;
    }

    private static HashSet<string> GetAvailableTextFields(JsonElement root)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(GetString(root, "Title", "Name"))) available.Add("title");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Code"))) available.Add("code");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Details", "Description", "Synopsis"))) available.Add("details");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Date", "ReleaseDate"))) available.Add("date");
        if (GetStringList(root, "URLs", "Url", "URL").Count > 0) available.Add("urls");
        if (GetTagNames(root, "Tags", "Tag", "TagNames").Count > 0) available.Add("tags");
        if (GetTextPerformerNames(root).Count > 0) available.Add("performers");
        if (!string.IsNullOrWhiteSpace(GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName"))) available.Add("studio");
        return available;
    }

    private static HashSet<string> GetAvailableImageFields(JsonElement root)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(GetString(root, "Title", "Name"))) available.Add("title");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Code"))) available.Add("code");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Details", "Description", "Synopsis"))) available.Add("details");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Photographer"))) available.Add("photographer");
        if (!string.IsNullOrWhiteSpace(GetString(root, "Date", "ReleaseDate"))) available.Add("date");
        if (GetStringList(root, "URLs", "Url", "URL").Count > 0) available.Add("urls");
        if (GetTagNames(root, "Tags", "Tag", "TagNames").Count > 0) available.Add("tags");
        if (GetNamedItems(root, "Performers", "Performer", "PerformerNames").Count > 0) available.Add("performers");
        if (!string.IsNullOrWhiteSpace(GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName"))) available.Add("studio");
        return available;
    }

    private static string DetermineApplyStatus(HashSet<string> availableFields, HashSet<string> replaceFields, IDictionary<string, string> collectionModes, ApplySceneScrapeAttemptDto dto)
    {
        var skipped = availableFields.Any(field => field switch
        {
            "title" or "code" or "details" or "director" or "photographer" or "date" or "image" => !replaceFields.Contains(field),
            _ => GetMode(collectionModes, field) == "skip",
        });

        var skippedSelection = GetMode(collectionModes, "tags") != "skip" && HasExcludedSelections(dto.TagSelections)
            || GetMode(collectionModes, "performers") != "skip" && HasExcludedSelections(dto.PerformerSelections);

        return skipped || skippedSelection ? "AppliedPartial" : "Applied";
    }

    private static Dictionary<string, object?> BuildAppliedSceneFieldProvenance(JsonElement root, HashSet<string> replaceFields, IDictionary<string, string> collectionModes, IReadOnlyDictionary<string, string>? tagSelections, IReadOnlyDictionary<string, string>? performerSelections)
    {
        var fields = new Dictionary<string, object?>();

        AddStringField(fields, "title", replaceFields.Contains("title"), GetString(root, "Title", "Name"));
        AddStringField(fields, "code", replaceFields.Contains("code"), GetString(root, "Code"));
        AddStringField(fields, "details", replaceFields.Contains("details"), GetString(root, "Details", "Description", "Synopsis"));
        AddStringField(fields, "director", replaceFields.Contains("director"), GetString(root, "Director"));

        if (replaceFields.Contains("date") && ScrapedSceneDateParser.TryParse(GetString(root, "Date", "ReleaseDate"), out var parsedDate))
            fields["date"] = parsedDate.ToString("yyyy-MM-dd");

        AddStringField(fields, "image_url", replaceFields.Contains("image"), GetString(root, "Image", "ImageUrl", "ImageURL"));
        AddListField(fields, "urls", GetMode(collectionModes, "urls") != "skip", GetStringList(root, "URLs", "Url", "URL"));
        AddListField(fields, "tags", GetMode(collectionModes, "tags") != "skip", FilterSelectedRelationNames(GetTagNames(root, "Tags", "Tag", "TagNames"), tagSelections));
        AddListField(fields, "performers", GetMode(collectionModes, "performers") != "skip", FilterSelectedRelationNames(GetNamedItems(root, "Performers", "Performer", "PerformerNames"), performerSelections));

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        AddStringField(fields, "studio", GetMode(collectionModes, "studio") != "skip", studioName);

        return fields;
    }

    private static Dictionary<string, object?> BuildAppliedAudioFieldProvenance(JsonElement root, HashSet<string> replaceFields, IDictionary<string, string> collectionModes, IReadOnlyDictionary<string, string>? tagSelections, IReadOnlyDictionary<string, string>? performerSelections)
    {
        var fields = new Dictionary<string, object?>();

        AddStringField(fields, "title", replaceFields.Contains("title"), GetString(root, "Title", "Name"));
        AddStringField(fields, "code", replaceFields.Contains("code"), GetString(root, "Code"));
        AddStringField(fields, "details", replaceFields.Contains("details"), GetString(root, "Details", "Description", "Synopsis"));

        if (replaceFields.Contains("date") && ScrapedSceneDateParser.TryParse(GetString(root, "Date", "ReleaseDate"), out var parsedDate))
            fields["date"] = parsedDate.ToString("yyyy-MM-dd");

        AddListField(fields, "urls", GetMode(collectionModes, "urls") != "skip", GetStringList(root, "URLs", "Url", "URL"));
        AddListField(fields, "tags", GetMode(collectionModes, "tags") != "skip", FilterSelectedRelationNames(GetTagNames(root, "Tags", "Tag", "TagNames"), tagSelections));
        AddListField(fields, "performers", GetMode(collectionModes, "performers") != "skip", FilterSelectedRelationNames(GetAudioPerformerNames(root), performerSelections));

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        AddStringField(fields, "studio", GetMode(collectionModes, "studio") != "skip", studioName);

        return fields;
    }

    private static Dictionary<string, object?> BuildAppliedTextFieldProvenance(JsonElement root, HashSet<string> replaceFields, IDictionary<string, string> collectionModes, IReadOnlyDictionary<string, string>? tagSelections, IReadOnlyDictionary<string, string>? performerSelections)
    {
        var fields = new Dictionary<string, object?>();

        AddStringField(fields, "title", replaceFields.Contains("title"), GetString(root, "Title", "Name"));
        AddStringField(fields, "code", replaceFields.Contains("code"), GetString(root, "Code"));
        AddStringField(fields, "details", replaceFields.Contains("details"), GetString(root, "Details", "Description", "Synopsis"));

        if (replaceFields.Contains("date") && ScrapedSceneDateParser.TryParse(GetString(root, "Date", "ReleaseDate"), out var parsedDate))
            fields["date"] = parsedDate.ToString("yyyy-MM-dd");

        AddListField(fields, "urls", GetMode(collectionModes, "urls") != "skip", GetStringList(root, "URLs", "Url", "URL"));
        AddListField(fields, "tags", GetMode(collectionModes, "tags") != "skip", FilterSelectedRelationNames(GetTagNames(root, "Tags", "Tag", "TagNames"), tagSelections));
        AddListField(fields, "performers", GetMode(collectionModes, "performers") != "skip", FilterSelectedRelationNames(GetTextPerformerNames(root), performerSelections));

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        AddStringField(fields, "studio", GetMode(collectionModes, "studio") != "skip", studioName);

        return fields;
    }

    private static Dictionary<string, object?> BuildAppliedImageFieldProvenance(JsonElement root, HashSet<string> replaceFields, IDictionary<string, string> collectionModes, IReadOnlyDictionary<string, string>? tagSelections, IReadOnlyDictionary<string, string>? performerSelections)
    {
        var fields = new Dictionary<string, object?>();

        AddStringField(fields, "title", replaceFields.Contains("title"), GetString(root, "Title", "Name"));
        AddStringField(fields, "code", replaceFields.Contains("code"), GetString(root, "Code"));
        AddStringField(fields, "details", replaceFields.Contains("details"), GetString(root, "Details", "Description", "Synopsis"));
        AddStringField(fields, "photographer", replaceFields.Contains("photographer"), GetString(root, "Photographer"));

        if (replaceFields.Contains("date") && ScrapedSceneDateParser.TryParse(GetString(root, "Date", "ReleaseDate"), out var parsedDate))
            fields["date"] = parsedDate.ToString("yyyy-MM-dd");

        AddListField(fields, "urls", GetMode(collectionModes, "urls") != "skip", GetStringList(root, "URLs", "Url", "URL"));
        AddListField(fields, "tags", GetMode(collectionModes, "tags") != "skip", FilterSelectedRelationNames(GetTagNames(root, "Tags", "Tag", "TagNames"), tagSelections));
        AddListField(fields, "performers", GetMode(collectionModes, "performers") != "skip", FilterSelectedRelationNames(GetNamedItems(root, "Performers", "Performer", "PerformerNames"), performerSelections));

        var studioName = GetNamedItems(root, "Studio", "StudioName").FirstOrDefault() ?? GetString(root, "Studio", "StudioName");
        AddStringField(fields, "studio", GetMode(collectionModes, "studio") != "skip", studioName);

        return fields;
    }

    private static void AddStringField(Dictionary<string, object?> fields, string fieldKey, bool applied, string? value)
    {
        if (applied && !string.IsNullOrWhiteSpace(value))
            fields[fieldKey] = value.Trim();
    }

    private static void AddListField(Dictionary<string, object?> fields, string fieldKey, bool applied, IReadOnlyList<string> values)
    {
        if (!applied || values.Count == 0)
            return;

        fields[fieldKey] = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record SelectedRelationName(string Name, bool AllowCreate);

    private static Dictionary<string, string>? BuildSelectionLookup(List<ScrapeCollectionItemSelectionDto>? selections)
    {
        if (selections == null)
            return null;

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            var name = NormalizeSelectionName(selection.Name);
            var action = NormalizeSelectionAction(selection.Action);
            if (string.IsNullOrWhiteSpace(name) || action == null)
                continue;

            lookup[name] = action;
        }

        return lookup;
    }

    private static List<SelectedRelationName> ResolveSelectedRelationNames(IReadOnlyList<string> names, IReadOnlyDictionary<string, string>? selections, bool createMissing)
    {
        var selected = new List<SelectedRelationName>();
        foreach (var name in names)
        {
            var normalizedName = NormalizeSelectionName(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
                continue;

            if (selections == null)
            {
                selected.Add(new SelectedRelationName(normalizedName, createMissing));
                continue;
            }

            if (!selections.TryGetValue(normalizedName, out var action) || action == "exclude")
                continue;

            selected.Add(new SelectedRelationName(normalizedName, action == "create"));
        }

        return selected
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Any(item => item.AllowCreate)
                ? new SelectedRelationName(group.First().Name, true)
                : group.First())
            .ToList();
    }

    private static IReadOnlyList<string> FilterSelectedRelationNames(IReadOnlyList<string> names, IReadOnlyDictionary<string, string>? selections)
    {
        if (selections == null)
            return names;

        return names
            .Select(NormalizeSelectionName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => selections.TryGetValue(name!, out var action) && action != "exclude")
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasExcludedSelections(List<ScrapeCollectionItemSelectionDto>? selections)
        => selections?.Any(selection => NormalizeSelectionAction(selection.Action) == "exclude") == true;

    private static string? NormalizeSelectionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            trimmed = trimmed[1..^1].Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeSelectionAction(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "include" => "include",
            "create" => "create",
            "exclude" or "skip" => "exclude",
            _ => null,
        };
    }

    private static string GetMode(IDictionary<string, string> collectionModes, string key)
        => collectionModes.TryGetValue(key, out var mode) && !string.IsNullOrWhiteSpace(mode)
            ? mode.Trim().ToLowerInvariant()
            : key == "studio" ? "replace" : "merge";

    private static List<string> BuildDefaultReplaceFields(Scene scene, JsonElement root)
    {
        var replaceFields = new List<string>();
        var currentDate = scene.Date?.ToString("yyyy-MM-dd");
        var scrapedDate = GetNormalizedSceneDate(root);

        var title = GetString(root, "Title", "Name");
        if (!string.IsNullOrWhiteSpace(title) && !string.Equals(title, scene.Title, StringComparison.Ordinal))
            replaceFields.Add("title");

        var code = GetString(root, "Code");
        if (!string.IsNullOrWhiteSpace(code) && !string.Equals(code, scene.Code, StringComparison.Ordinal))
            replaceFields.Add("code");

        var details = GetString(root, "Details", "Description", "Synopsis");
        if (!string.IsNullOrWhiteSpace(details) && !string.Equals(details, scene.Details, StringComparison.Ordinal))
            replaceFields.Add("details");

        var director = GetString(root, "Director");
        if (!string.IsNullOrWhiteSpace(director) && !string.Equals(director, scene.Director, StringComparison.Ordinal))
            replaceFields.Add("director");

        if (!string.IsNullOrWhiteSpace(scrapedDate) && !string.Equals(scrapedDate, currentDate, StringComparison.Ordinal))
            replaceFields.Add("date");

        var imageUrl = GetString(root, "Image", "ImageUrl", "ImageURL");
        if (!string.IsNullOrWhiteSpace(imageUrl))
            replaceFields.Add("image");

        return replaceFields;
    }

    private static Dictionary<string, string> BuildDefaultCollectionModes(Scene scene, JsonElement root)
    {
        var currentUrls = scene.Urls.Select(item => item.Url).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var currentTags = scene.SceneTags.Where(item => item.Tag != null).Select(item => item.Tag!.Name).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var currentPerformers = scene.ScenePerformers.Where(item => item.Performer != null).Select(item => item.Performer!.Name).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var currentStudio = scene.Studio?.Name?.Trim();

        var scrapedUrls = GetStringList(root, "URLs", "Url", "URL");
        var scrapedTags = GetTagNames(root, "Tags", "Tag", "TagNames");
        var scrapedPerformers = GetNamedItems(root, "Performers", "Performer", "PerformerNames");
        var scrapedStudio = GetString(root, "Studio", "StudioName");

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["studio"] = !string.IsNullOrWhiteSpace(scrapedStudio) && !string.Equals(scrapedStudio, currentStudio, StringComparison.OrdinalIgnoreCase) ? "replace" : "skip",
            ["urls"] = scrapedUrls.Count > 0 && !StringListsEqual(scrapedUrls, currentUrls) ? "merge" : "skip",
            ["tags"] = scrapedTags.Count > 0 && !StringListsEqual(scrapedTags, currentTags) ? "merge" : "skip",
            ["performers"] = scrapedPerformers.Count > 0 && !StringListsEqual(scrapedPerformers, currentPerformers) ? "merge" : "skip",
        };
    }

    private static bool StringListsEqual(IEnumerable<string> left, IEnumerable<string> right)
    {
        var normalizedLeft = left
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToLowerInvariant())
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var normalizedRight = right
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToLowerInvariant())
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        return normalizedLeft.SequenceEqual(normalizedRight, StringComparer.Ordinal);
    }

    private static string? GetNormalizedSceneDate(JsonElement root)
    {
        var date = GetString(root, "Date", "ReleaseDate");
        return ScrapedSceneDateParser.TryParse(date, out var parsedDate)
            ? parsedDate.ToString("yyyy-MM-dd")
            : null;
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim();

            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }

        return null;
    }

    private static List<string> GetStringList(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return [];
    }

    private static List<string> GetNamedItems(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                var candidate = GetString(value, "Name", "name", "Title", "title");
                return string.IsNullOrWhiteSpace(candidate) ? [] : [candidate];
            }

            if (value.ValueKind != JsonValueKind.Array)
                continue;

            var items = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var stringValue = item.GetString();
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        items.Add(stringValue.Trim());
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    var candidate = GetString(item, "Name", "name", "Title", "title");
                    if (!string.IsNullOrWhiteSpace(candidate))
                        items.Add(candidate);
                }
            }

            return items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return [];
    }

    private static List<string> GetAudioPerformerNames(JsonElement root)
        => NormalizeNames(GetNamedItems(root, "Performers", "Performer", "PerformerNames")
            .Concat(GetNamedItems(root, "Artist", "artist", "Creator", "creator", "Author", "author")));

    private static List<string> GetTextPerformerNames(JsonElement root)
        => NormalizeNames(GetNamedItems(root, "Performers", "Performer", "PerformerNames")
            .Concat(GetNamedItems(root, "Author", "author", "Creator", "creator", "Artist", "artist")));

    private static List<string> GetTagNames(JsonElement root, params string[] names)
        => NormalizeNames(GetNamedItems(root, names));

    private static List<string> NormalizeNames(IEnumerable<string> values)
        => values
            .Select(NormalizeTagName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;

    private static string? NormalizeTagName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            trimmed = trimmed[1..^1].Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static List<JsonElement> GetObjectItems(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Object)
                return [value];

            if (value.ValueKind != JsonValueKind.Array)
                continue;

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .ToList();
        }

        return [];
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Dictionary<string, object>? SelectPrimaryCandidate(List<Dictionary<string, object>>? candidates, string? searchTerm)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        var normalizedSearchTerm = searchTerm?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSearchTerm))
            return candidates[0];

        return OrderCandidatesBySearchTerm(candidates, normalizedSearchTerm).FirstOrDefault();
    }

    private static List<Dictionary<string, object>> OrderCandidatesBySearchTerm(List<Dictionary<string, object>>? candidates, string? searchTerm)
    {
        if (candidates == null || candidates.Count == 0)
            return [];

        var normalizedSearchTerm = searchTerm?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSearchTerm))
            return candidates;

        return candidates
            .OrderByDescending(candidate => ScoreCandidate(candidate, normalizedSearchTerm))
            .ThenBy(candidate => candidate.TryGetValue("Title", out _) ? 0 : 1)
            .ThenBy(candidate => GetCandidateTitle(candidate), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ScoreCandidate(IReadOnlyDictionary<string, object> candidate, string searchTerm)
    {
        var bestScore = 0;
        var normalizedSearchTerm = NormalizeSearchText(searchTerm);
        foreach (var field in new[] { "Title", "Name", "URL", "Url" })
        {
            if (!candidate.TryGetValue(field, out var value) || value is not string text || string.IsNullOrWhiteSpace(text))
                continue;

            if (string.Equals(text, searchTerm, StringComparison.OrdinalIgnoreCase))
                bestScore = Math.Max(bestScore, 1200);
            else if (string.Equals(NormalizeSearchText(text), normalizedSearchTerm, StringComparison.Ordinal))
                bestScore = Math.Max(bestScore, 1000);
            else if (text.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase))
                bestScore = Math.Max(bestScore, 700);
            else if (NormalizeSearchText(text).StartsWith(normalizedSearchTerm, StringComparison.Ordinal))
                bestScore = Math.Max(bestScore, 600);
            else if (text.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                bestScore = Math.Max(bestScore, 400);
            else if (NormalizeSearchText(text).Contains(normalizedSearchTerm, StringComparison.Ordinal))
                bestScore = Math.Max(bestScore, 350);
            else if (searchTerm.Contains(text, StringComparison.OrdinalIgnoreCase))
                bestScore = Math.Max(bestScore, 150);
        }

        return bestScore;
    }

    private static string GetCandidateTitle(IReadOnlyDictionary<string, object> candidate)
    {
        foreach (var field in new[] { "Title", "Name", "URL", "Url" })
        {
            if (candidate.TryGetValue(field, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
                continue;
            }

            if (lastWasSpace)
                continue;

            builder.Append(' ');
            lastWasSpace = true;
        }

        return builder.ToString().Trim();
    }

    private static string? ResolveAbsoluteUrl(string? url, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.ToString();

        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var resolved))
            return resolved.ToString();

        return url;
    }

    private static string? ResolveResultJson(ScrapeAttempt attempt, int? selectedCandidateIndex)
    {
        if (string.IsNullOrWhiteSpace(attempt.CandidateResultsJson))
            return attempt.ResultJson;

        try
        {
            var candidates = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(attempt.CandidateResultsJson, JsonOptions);
            if (candidates == null || candidates.Count == 0)
                return attempt.ResultJson;

            var candidateIndex = selectedCandidateIndex.GetValueOrDefault();
            if (candidateIndex < 0 || candidateIndex >= candidates.Count)
                candidateIndex = 0;

            return JsonSerializer.Serialize(candidates[candidateIndex], JsonOptions);
        }
        catch
        {
            return attempt.ResultJson;
        }
    }

    private static ScrapeAttemptDto MapAttempt(ScrapeAttempt attempt) => new(
        attempt.Id,
        attempt.ScraperId,
        attempt.EntityType,
        attempt.EntityId,
        attempt.InputKind,
        attempt.InputJson,
        attempt.ResultJson,
        attempt.CandidateResultsJson,
        attempt.EntitySnapshotJson,
        attempt.Status,
        attempt.Error,
        attempt.CreatedAt.ToString("o"),
        attempt.AppliedAt?.ToString("o"));
}