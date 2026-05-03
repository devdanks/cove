using Cove.Core.DTOs;

namespace Cove.Core.Interfaces;

public interface IFaceSuggester
{
    Task<IReadOnlyList<FaceSuggestionDto>> SuggestForAsync(int faceId, int maxResults, CancellationToken cancellationToken = default);
}