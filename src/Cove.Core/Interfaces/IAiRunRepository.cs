using Cove.Core.Entities;

namespace Cove.Core.Interfaces;

/// <summary>
/// Generic repository for AI/automation run history.
/// Tracks the lifecycle of processing runs (start, completion, failure) keyed by
/// a caller-supplied run key and source key. Available to any extension that runs
/// automated media processing and wants to record job history.
/// </summary>
public interface IAiRunRepository
{
    /// <summary>Returns an existing run for <paramref name="runKey"/> + <paramref name="sourceKey"/>,
    /// or creates and saves a new one.</summary>
    Task<AiRun> FindOrCreateAsync(string runKey, string sourceKey,
        AiRunTargetType targetType, int targetId,
        AiRunStatus initialStatus = AiRunStatus.Pending,
        CancellationToken ct = default);

    Task UpdateAsync(AiRun run, CancellationToken ct = default);

    /// <summary>Returns completed runs for a specific target, newest first.</summary>
    Task<IReadOnlyList<AiRun>> GetCompletedAsync(AiRunTargetType targetType, int targetId,
        string sourceKey, CancellationToken ct = default);
}
