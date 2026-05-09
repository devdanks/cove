using Cove.Core.DTOs;
using Cove.Core.Entities;

namespace Cove.Core.Interfaces;

public interface IFieldProvenanceService
{
    Task RecordAsync(
        AffinityHostType hostType,
        int hostId,
        string fieldKey,
        object? value,
        string sourceKey,
        string? sourceRunId = null,
        string? modelKey = null,
        float? confidence = null,
        CancellationToken cancellationToken = default);

    Task RecordManyAsync(
        AffinityHostType hostType,
        int hostId,
        IReadOnlyDictionary<string, object?> fields,
        string sourceKey,
        string? sourceRunId = null,
        string? modelKey = null,
        float? confidence = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FieldProvenanceDto>> GetForHostAsync(
        AffinityHostType hostType,
        int hostId,
        CancellationToken cancellationToken = default);
}