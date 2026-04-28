namespace Cove.Core.Auth;

public interface IUserService
{
    Task<UserDto?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<UserDto?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(int id, UpdateUserRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task DeleteAsync(int id, CovePrincipal? actor, CancellationToken ct = default);
    Task ChangePasswordAsync(int userId, string newPassword, CovePrincipal? actor, CancellationToken ct = default);
    Task<bool> VerifyPasswordAsync(int userId, string password, CancellationToken ct = default);
    Task SetRolesAsync(int userId, IEnumerable<string> roleNames, CovePrincipal? actor, CancellationToken ct = default);
    Task<UserUiPreferencesDto?> UpdateUiPreferencesAsync(int userId, UserUiPreferencesDto preferences, CovePrincipal? actor, CancellationToken ct = default);
    Task RecordLoginSuccessAsync(int userId, string? ip, CancellationToken ct = default);
    Task RecordLoginFailureAsync(int userId, CancellationToken ct = default);
    Task UnlockAsync(int userId, CovePrincipal? actor, CancellationToken ct = default);
}

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto?> GetAsync(int id, CancellationToken ct = default);
    Task<RoleDto?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(CreateRoleRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(int id, UpdateRoleRequest req, CovePrincipal? actor, CancellationToken ct = default);
    Task DeleteAsync(int id, CovePrincipal? actor, CancellationToken ct = default);
    Task SetPermissionsAsync(int roleId, IEnumerable<string> permissions, CovePrincipal? actor, CancellationToken ct = default);
}

public interface ITokenService
{
    Task<TokenPair> IssueForUserAsync(int userId, string? ip, string? userAgent, CancellationToken ct = default);
    Task<TokenPair> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default);
    Task RevokeChainAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeAllForUserAsync(int userId, CancellationToken ct = default);

    /// <summary>Resolve a JWT bearer token (or API token) to a CovePrincipal. Returns null if invalid.</summary>
    Task<CovePrincipal?> ResolveAsync(string? authorizationHeader, string? ip, string? userAgent, CancellationToken ct = default);

    Task<ApiTokenIssued> CreateApiTokenAsync(int userId, string name, IEnumerable<string>? scope, DateTime? expiresAt, CovePrincipal? actor, CancellationToken ct = default);
    Task RevokeApiTokenAsync(Guid id, CovePrincipal? actor, CancellationToken ct = default);
    Task<IReadOnlyList<ApiTokenDto>> ListApiTokensAsync(int userId, CancellationToken ct = default);
}

// ===== DTOs =====

public sealed record UserDto(
    int Id,
    string Username,
    string? DisplayName,
    string? Email,
    bool IsActive,
    bool IsLocked,
    bool IsSystem,
    bool MustChangePassword,
    DateTime? LastLoginAt,
    string? LastLoginIp,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles,
    UserUiPreferencesDto? UiPreferences);

public sealed record UserThemePreferencesDto(
    string? ActiveThemeId,
    IReadOnlyList<string>? ActiveComponentStyles,
    string? ActiveLayoutStyle,
    Dictionary<string, string>? CustomThemeColors,
    Dictionary<string, Dictionary<string, string>>? StyleOptions);

public sealed record UserRatingSystemOptionsDto(
    string? Type,
    string? StarPrecision);

public sealed record UserUiPreferencesDto(
    UserThemePreferencesDto? Theme,
    UserRatingSystemOptionsDto? RatingSystemOptions);

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string? DisplayName = null,
    string? Email = null,
    IReadOnlyList<string>? Roles = null,
    bool MustChangePassword = false);

public sealed record UpdateUserRequest(
    string? DisplayName = null,
    string? Email = null,
    bool? IsActive = null,
    bool? MustChangePassword = null);

public sealed record RoleDto(
    int Id,
    string Name,
    string? Description,
    bool IsBuiltin,
    bool IsSystem,
    string Source,
    IReadOnlyList<string> Permissions,
    int UserCount);

public sealed record CreateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(
    string? Description,
    IReadOnlyList<string>? Permissions);

public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    DateTime AccessExpires,
    DateTime RefreshExpires,
    UserDto User);

public sealed record ApiTokenIssued(
    Guid Id,
    string Name,
    string PlaintextToken,
    string Prefix,
    IReadOnlyList<string>? Scope,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

public sealed record ApiTokenDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string>? Scope,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt);

public sealed record AuditEventDto(
    long Id,
    DateTime OccurredAt,
    int? ActorUserId,
    string? ActorUsername,
    string ActorKind,
    string? Ip,
    string? UserAgent,
    string Action,
    string? TargetKind,
    string? TargetId,
    string Outcome,
    string? Detail);

public sealed record MeResponse(
    UserDto User,
    IReadOnlyList<string> Permissions);
