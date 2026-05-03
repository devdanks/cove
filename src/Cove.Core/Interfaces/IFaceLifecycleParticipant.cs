using Cove.Core.Entities;

namespace Cove.Core.Interfaces;

public interface IFaceLifecycleParticipant
{
    Task OnDeletingAsync(Face face, CancellationToken cancellationToken = default);
}