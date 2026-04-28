namespace Cove.Core.Auth;

public interface IContentRuleService
{
    Task<IReadOnlyList<ContentRuleDto>> ListAsync(int? roleId = null, CancellationToken ct = default);
    Task<ContentRuleDto> CreateAsync(CreateContentRuleRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task<ContentRuleDto> UpdateAsync(int id, UpdateContentRuleRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task DeleteAsync(int id, CovePrincipal? actor, CancellationToken ct = default);

    Task<IReadOnlyList<EntityOverrideDto>> ListOverridesAsync(int? roleId = null, string? entityKind = null, CancellationToken ct = default);
    Task<EntityOverrideDto> CreateOverrideAsync(CreateEntityOverrideRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task DeleteOverrideAsync(int id, CovePrincipal? actor, CancellationToken ct = default);
}

public sealed record ContentRuleDto(
    int Id,
    int RoleId,
    string RoleName,
    string EntityKind,
    string Effect,
    string ScopeKind,
    string ScopeValue,
    string AppliesTo,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateContentRuleRequest(
    int RoleId,
    string EntityKind,
    string Effect,
    string ScopeKind,
    string ScopeValue,
    string AppliesTo);

public sealed record UpdateContentRuleRequest(
    string? Effect,
    string? ScopeKind,
    string? ScopeValue,
    string? AppliesTo);

public sealed record EntityOverrideDto(
    int Id,
    int RoleId,
    string RoleName,
    string EntityKind,
    string EntityId,
    string Effect,
    string AppliesTo,
    DateTime CreatedAt);

public sealed record CreateEntityOverrideRequest(
    int RoleId,
    string EntityKind,
    string EntityId,
    string Effect,
    string AppliesTo);