using System.Collections.Concurrent;
using Cove.Core.DTOs;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public sealed record SceneBatchScrapeExecutionSummary(
    int TotalCount,
    int ScrapedCount,
    int AppliedCount,
    int PartialAppliedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<string> Issues);

public class SceneBatchScrapeService(
    IServiceScopeFactory scopeFactory,
    CoveConfiguration config,
    ILogger<SceneBatchScrapeService> logger)
{
    public async Task<SceneBatchScrapeExecutionSummary> RunAsync(BatchSceneScrapeStartRequestDto request, IJobProgress? progress, CancellationToken ct)
    {
        var normalizedInputKind = request.InputKind?.Trim().ToLowerInvariant();
        if (normalizedInputKind is not ("url" or "name"))
            throw new InvalidOperationException($"Unsupported batch scene scrape input kind '{request.InputKind}'.");

        var sceneIds = request.SceneIds.Where(id => id > 0).Distinct().ToList();
        if (sceneIds.Count == 0)
            return new SceneBatchScrapeExecutionSummary(0, 0, 0, 0, 0, 0, []);

        var issues = new ConcurrentQueue<string>();
        var processed = 0;
        var scraped = 0;
        var applied = 0;
        var partialApplied = 0;
        var skipped = 0;
        var failed = 0;

        await Parallel.ForEachAsync(
            sceneIds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = ResolveParallelism(),
                CancellationToken = ct,
            },
            async (sceneId, token) =>
            {
                string label = $"Scene {sceneId}";

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
                    var scrapeAttemptService = scope.ServiceProvider.GetRequiredService<ScrapeAttemptService>();

                    var scene = await db.Scenes
                        .AsNoTracking()
                        .Include(item => item.Urls)
                        .FirstOrDefaultAsync(item => item.Id == sceneId, token);

                    if (scene == null)
                    {
                        Interlocked.Increment(ref skipped);
                        issues.Enqueue($"Scene {sceneId}: scene not found.");
                        return;
                    }

                    label = string.IsNullOrWhiteSpace(scene.Title) ? $"Scene {scene.Id}" : scene.Title;
                    var input = normalizedInputKind == "url"
                        ? scene.Urls.Select(item => item.Url).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
                        : scene.Title;

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        Interlocked.Increment(ref skipped);
                        issues.Enqueue($"{label}: no {(normalizedInputKind == "url" ? "URL" : "title")} available.");
                        return;
                    }

                    var attempt = await scrapeAttemptService.CreateAttemptAsync(
                        new CreateScrapeAttemptDto(
                            request.ScraperId,
                            "scene",
                            scene.Id,
                            normalizedInputKind,
                            normalizedInputKind == "url" ? input : null,
                            normalizedInputKind == "name" ? input : null,
                            null),
                        token);

                    if (string.Equals(attempt.Status, "Failure", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref failed);
                        issues.Enqueue($"{label}: {attempt.Error ?? "scrape returned no results."}");
                        return;
                    }

                    Interlocked.Increment(ref scraped);

                    if (!request.AutoApply)
                        return;

                    var appliedAttempt = await scrapeAttemptService.ApplySceneAttemptWithDefaultPlanAsync(
                        attempt.Id,
                        new ApplySceneScrapeAttemptDto(
                            ReplaceFields: null,
                            CollectionModes: null,
                            CreateMissingTags: request.CreateMissingTags,
                            CreateMissingPerformers: request.CreateMissingPerformers,
                            CreateMissingStudio: request.CreateMissingStudio,
                            MarkOrganized: request.MarkOrganized,
                            HydratePerformers: request.HydratePerformers),
                        token);

                    if (appliedAttempt == null)
                    {
                        Interlocked.Increment(ref failed);
                        issues.Enqueue($"{label}: failed to apply the scraped result.");
                        return;
                    }

                    if (string.Equals(appliedAttempt.Status, "AppliedPartial", StringComparison.OrdinalIgnoreCase))
                        Interlocked.Increment(ref partialApplied);
                    else
                        Interlocked.Increment(ref applied);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    issues.Enqueue($"{label}: {ex.Message}");
                    logger.LogWarning(ex, "Batch scene scrape failed for {SceneLabel}", label);
                }
                finally
                {
                    var completed = Interlocked.Increment(ref processed);
                    progress?.Report(completed / (double)sceneIds.Count, $"Processed {completed}/{sceneIds.Count}: {label}");
                }
            });

        return new SceneBatchScrapeExecutionSummary(
            sceneIds.Count,
            scraped,
            applied,
            partialApplied,
            skipped,
            failed,
            issues.ToArray());
    }

    private int ResolveParallelism()
    {
        var configured = config.MaxParallelTasks;
        var desired = configured <= 0 ? Environment.ProcessorCount : configured;
        return Math.Clamp(desired, 1, 8);
    }
}