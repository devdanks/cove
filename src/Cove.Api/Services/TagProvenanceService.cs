using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Api.Services;

public sealed class TagProvenanceService(CoveContext db, IServiceScopeFactory? scopeFactory = null) : ITagProvenanceService
{
    private readonly CoveContext _db = db;
    private readonly IServiceScopeFactory? _scopeFactory = scopeFactory;

    public Task RecordAsync(
        AffinityHostType hostType,
        int hostId,
        int tagId,
        string sourceKey,
        string? sourceRunId = null,
        string? modelKey = null,
        float? confidence = null,
        CancellationToken cancellationToken = default)
    {
        if (tagId <= 0)
        {
            return Task.CompletedTask;
        }

        return EnsureApplicationAsync(hostType, hostId, tagId, null, sourceKey, sourceRunId, modelKey, confidence, cancellationToken);
    }

    public Task RecordAsync(
        AffinityHostType hostType,
        int hostId,
        Tag tag,
        string sourceKey,
        string? sourceRunId = null,
        string? modelKey = null,
        float? confidence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Id > 0)
        {
            return EnsureApplicationAsync(hostType, hostId, tag.Id, null, sourceKey, sourceRunId, modelKey, confidence, cancellationToken);
        }

        return EnsureApplicationAsync(hostType, hostId, null, tag, sourceKey, sourceRunId, modelKey, confidence, cancellationToken);
    }

    public async Task SyncTagSetAsync(
        AffinityHostType hostType,
        int hostId,
        IReadOnlyCollection<int> previousTagIds,
        IReadOnlyCollection<int> currentTagIds,
        string sourceKey = "user",
        CancellationToken cancellationToken = default)
    {
        var previous = NormalizeTagIds(previousTagIds);
        var current = NormalizeTagIds(currentTagIds);

        var removedTagIds = previous.Except(current).ToArray();
        if (removedTagIds.Length > 0)
        {
            var removedApplications = await _db.TagApplications
                .Where(application => application.HostType == hostType && application.HostId == hostId && removedTagIds.Contains(application.TagId))
                .ToListAsync(cancellationToken);

            if (removedApplications.Count > 0)
            {
                _db.TagApplications.RemoveRange(removedApplications);
            }
        }

        foreach (var tagId in current.Except(previous))
        {
            await RecordAsync(hostType, hostId, tagId, sourceKey, cancellationToken: cancellationToken);
        }
    }

    public async Task RemoveForHostAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
    {
        var applications = await _db.TagApplications
            .Where(application => application.HostType == hostType && application.HostId == hostId)
            .ToListAsync(cancellationToken);

        if (applications.Count > 0)
        {
            _db.TagApplications.RemoveRange(applications);
        }
    }

    public async Task<IReadOnlyDictionary<int, List<TagProvenanceDto>>> GetLookupAsync(
        AffinityHostType hostType,
        int hostId,
        IReadOnlyCollection<int> tagIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedTagIds = NormalizeTagIds(tagIds);
        if (normalizedTagIds.Count == 0)
        {
            return new Dictionary<int, List<TagProvenanceDto>>();
        }

        using var scope = _scopeFactory?.CreateScope();
        var lookupDb = scope?.ServiceProvider.GetRequiredService<CoveContext>() ?? _db;

        var applications = await lookupDb.TagApplications
            .AsNoTracking()
            .Where(application => application.HostType == hostType && application.HostId == hostId && normalizedTagIds.Contains(application.TagId))
            .OrderBy(application => application.SourceKey)
            .ThenBy(application => application.CreatedAt)
            .ToListAsync(cancellationToken);

        return applications
            .GroupBy(application => application.TagId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(MapToDto).ToList());
    }

    private async Task EnsureApplicationAsync(
        AffinityHostType hostType,
        int hostId,
        int? tagId,
        Tag? tag,
        string sourceKey,
        string? sourceRunId,
        string? modelKey,
        float? confidence,
        CancellationToken cancellationToken)
    {
        var normalizedSourceKey = NormalizeRequired(sourceKey);
        var normalizedSourceRunId = NormalizeOptional(sourceRunId);
        var normalizedModelKey = NormalizeOptional(modelKey);

        TagApplication? application;
        if (tagId.HasValue)
        {
            application = _db.TagApplications.Local.FirstOrDefault(
                candidate => candidate.HostType == hostType
                    && candidate.HostId == hostId
                    && candidate.TagId == tagId.Value
                    && candidate.SourceKey == normalizedSourceKey
                    && candidate.SourceRunId == normalizedSourceRunId
                    && candidate.ModelKey == normalizedModelKey);

            if (application is null)
            {
                application = await _db.TagApplications.FirstOrDefaultAsync(
                    candidate => candidate.HostType == hostType
                        && candidate.HostId == hostId
                        && candidate.TagId == tagId.Value
                        && candidate.SourceKey == normalizedSourceKey
                        && candidate.SourceRunId == normalizedSourceRunId
                        && candidate.ModelKey == normalizedModelKey,
                    cancellationToken);
            }
        }
        else
        {
            application = _db.TagApplications.Local.FirstOrDefault(
                candidate => candidate.HostType == hostType
                    && candidate.HostId == hostId
                    && ReferenceEquals(candidate.Tag, tag)
                    && candidate.SourceKey == normalizedSourceKey
                    && candidate.SourceRunId == normalizedSourceRunId
                    && candidate.ModelKey == normalizedModelKey);
        }

        if (application is null)
        {
            application = new TagApplication
            {
                HostType = hostType,
                HostId = hostId,
                TagId = tagId ?? 0,
                Tag = tag,
                SourceKey = normalizedSourceKey,
                SourceRunId = normalizedSourceRunId,
                ModelKey = normalizedModelKey,
                Confidence = confidence,
            };
            _db.TagApplications.Add(application);
            return;
        }

        if (confidence.HasValue && (!application.Confidence.HasValue || confidence.Value > application.Confidence.Value))
        {
            application.Confidence = confidence.Value;
        }
    }

    private static HashSet<int> NormalizeTagIds(IReadOnlyCollection<int> tagIds)
        => tagIds.Where(static tagId => tagId > 0).ToHashSet();

    private static string NormalizeRequired(string value)
        => string.IsNullOrWhiteSpace(value) ? "user" : value.Trim();

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static TagProvenanceDto MapToDto(TagApplication application)
        => new(
            application.SourceKey,
            string.IsNullOrWhiteSpace(application.SourceRunId) ? null : application.SourceRunId,
            string.IsNullOrWhiteSpace(application.ModelKey) ? null : application.ModelKey,
            application.Confidence,
            application.CreatedAt.ToString("o"));
}