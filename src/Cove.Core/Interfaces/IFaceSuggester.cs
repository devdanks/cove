using Cove.Core.DTOs;

namespace Cove.Core.Interfaces;

public sealed record FaceSuggestionOptions(bool IncludeReferenceMatches = true);

public interface IFaceSuggester
{
    Task<IReadOnlyList<FaceSuggestionDto>> SuggestForAsync(int faceId, int maxResults, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FaceSuggestionDto>> SuggestForAsync(int faceId, int maxResults, FaceSuggestionOptions options, CancellationToken cancellationToken = default)
        => SuggestForAsync(faceId, maxResults, cancellationToken);

    async Task<IReadOnlyDictionary<int, IReadOnlyList<FaceSuggestionDto>>> SuggestForBatchAsync(
        IReadOnlyCollection<int> faceIds,
        int maxResults,
        FaceSuggestionOptions options,
        CancellationToken cancellationToken = default)
    {
        var suggestionsByFaceId = new Dictionary<int, IReadOnlyList<FaceSuggestionDto>>();
        foreach (var faceId in faceIds.Where(static id => id > 0).Distinct())
        {
            suggestionsByFaceId[faceId] = await SuggestForAsync(faceId, maxResults, options, cancellationToken);
        }

        return suggestionsByFaceId;
    }
}