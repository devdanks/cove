namespace Cove.Core.Auth;

public interface IShareLinkService
{
    Task<IReadOnlyList<ShareLinkDto>> ListAsync(int? createdByUserId = null, CancellationToken ct = default);
    Task<ShareLinkIssued> CreateAsync(CreateShareLinkRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task RevokeAsync(Guid id, CovePrincipal? actor, CancellationToken ct = default);
    Task<CovePrincipal?> ResolveAsync(string token, string? password, string? ip, string? userAgent, CancellationToken ct = default);
}

public sealed record ShareLinkDto(
    Guid Id,
    int? CreatedByUserId,
    string? CreatedByUsername,
    string EntityKind,
    IReadOnlyList<string> EntityIds,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    int ViewCount,
    bool HasPassword,
    bool Revoked);

public sealed record CreateShareLinkRequest(
    string EntityKind,
    IReadOnlyList<string> EntityIds,
    DateTime? ExpiresAt = null,
    string? Password = null);

public sealed record ShareLinkIssued(
    Guid Id,
    string PlaintextToken,
    string EntityKind,
    IReadOnlyList<string> EntityIds,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    bool HasPassword);