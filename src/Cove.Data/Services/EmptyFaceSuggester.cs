using Cove.Core.DTOs;
using Cove.Core.Interfaces;

namespace Cove.Data.Services;

public sealed class EmptyFaceSuggester : IFaceSuggester
{
    public Task<IReadOnlyList<FaceSuggestionDto>> SuggestForAsync(int faceId, int maxResults, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FaceSuggestionDto>>([]);
}