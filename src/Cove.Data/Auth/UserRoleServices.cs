using Cove.Core.Auth;
using Cove.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Cove.Data.Auth;

public sealed class UserService : IUserService
{
    public const int MaxFailedLogins = 8;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions UiPreferencesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly CoveContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<UserService> _log;

    public UserService(CoveContext db, IAuditService audit, ILogger<UserService> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task<UserDto?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Roles).ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), ct);
        return user is null ? null : Map(user);
    }

    public async Task<UserDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Roles).ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : Map(user);
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Roles).ThenInclude(r => r.Role)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);
        return users.Select(Map).ToList();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest req, CovePrincipal? actor, CancellationToken ct = default)
    {
        Validation.Username(req.Username);
        Validation.Password(req.Password);

        var exists = await _db.Users.AnyAsync(u => u.Username.ToLower() == req.Username.ToLower(), ct);
        if (exists) throw new InvalidOperationException("Username already in use.");

        var user = new User
        {
            Username = req.Username,
            DisplayName = req.DisplayName,
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email,
            PasswordHash = PasswordHasher.HashPassword(req.Password),
            PasswordAlgo = PasswordHasher.Algorithm,
            IsActive = true,
            MustChangePassword = req.MustChangePassword,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        if (req.Roles is { Count: > 0 })
            await SetRolesAsync(user.Id, req.Roles, actor, ct);

        await _audit.LogAsync(AuditActions.UserCreate, AuditOutcomes.Success, actor,
            "user", user.Id.ToString(), new { user.Username }, ct);

        return (await GetAsync(user.Id, ct))!;
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest req, CovePrincipal? actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("User not found.");
        if (req.DisplayName is not null) user.DisplayName = req.DisplayName;
        if (req.Email is not null) user.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email;
        if (req.IsActive is { } active)
        {
            if (user.IsSystem && !active) throw new InvalidOperationException("Cannot disable the Owner account.");
            user.IsActive = active;
        }
        if (req.MustChangePassword is { } mcp) user.MustChangePassword = mcp;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditActions.UserUpdate, AuditOutcomes.Success, actor,
            "user", user.Id.ToString(), new { user.Username }, ct);

        return (await GetAsync(user.Id, ct))!;
    }

    public async Task DeleteAsync(int id, CovePrincipal? actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("User not found.");
        if (user.IsSystem) throw new InvalidOperationException("Cannot delete the Owner account.");
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserDelete, AuditOutcomes.Success, actor,
            "user", id.ToString(), new { user.Username }, ct);
    }

    public async Task ChangePasswordAsync(int userId, string newPassword, CovePrincipal? actor, CancellationToken ct = default)
    {
        Validation.Password(newPassword);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("User not found.");
        user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        user.PasswordAlgo = PasswordHasher.Algorithm;
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.PasswordChange, AuditOutcomes.Success, actor,
            "user", user.Id.ToString(), null, ct);
    }

    public async Task<bool> VerifyPasswordAsync(int userId, string password, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        var verified = PasswordHasher.Verify(password, user.PasswordHash, user.PasswordAlgo);
        if (!verified) return false;

        if (PasswordHasher.NeedsRehash(user.PasswordHash, user.PasswordAlgo))
        {
            user.PasswordHash = PasswordHasher.HashPassword(password);
            user.PasswordAlgo = PasswordHasher.Algorithm;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task SetRolesAsync(int userId, IEnumerable<string> roleNames, CovePrincipal? actor, CancellationToken ct = default)
    {
        var nameList = roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var roles = await _db.Roles.Where(r => nameList.Contains(r.Name)).ToListAsync(ct);
        if (roles.Count != nameList.Count)
        {
            var missing = nameList.Except(roles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException($"Unknown role(s): {string.Join(", ", missing)}");
        }

        await _db.UserRoleAssignments.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        foreach (var r in roles)
            _db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                UserId = userId,
                RoleId = r.Id,
                GrantedAt = DateTime.UtcNow,
                GrantedByUserId = actor?.UserId,
            });
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.RoleGrant, AuditOutcomes.Success, actor,
            "user", userId.ToString(), new { roles = nameList }, ct);
    }

    public async Task<UserUiPreferencesDto?> UpdateUiPreferencesAsync(int userId, UserUiPreferencesDto preferences, CovePrincipal? actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("User not found.");

        var normalized = NormalizeUiPreferences(preferences);
        user.UiPreferencesJson = SerializeUiPreferences(normalized);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditActions.UserUpdate, AuditOutcomes.Success, actor,
            "user", user.Id.ToString(), new { user.Username, field = "ui_preferences" }, ct);

        return normalized;
    }

    public async Task RecordLoginSuccessAsync(int userId, string? ip, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.LastLoginAt, now)
            .SetProperty(u => u.LastLoginIp, ip is null ? null : (ip.Length > 64 ? ip[..64] : ip))
            .SetProperty(u => u.FailedLoginCount, 0)
            .SetProperty(u => u.IsLocked, false)
            .SetProperty(u => u.LockedUntil, (DateTime?)null), ct);
    }

    public async Task RecordLoginFailureAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= MaxFailedLogins)
        {
            user.IsLocked = true;
            user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnlockAsync(int userId, CovePrincipal? actor, CancellationToken ct = default)
    {
        await _db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.IsLocked, false)
            .SetProperty(u => u.FailedLoginCount, 0)
            .SetProperty(u => u.LockedUntil, (DateTime?)null), ct);
        await _audit.LogAsync(AuditActions.UserUnlock, AuditOutcomes.Success, actor,
            "user", userId.ToString(), null, ct);
    }

    private static UserDto Map(User u) => new(
        u.Id, u.Username, u.DisplayName, u.Email,
        u.IsActive, u.IsLocked, u.IsSystem, u.MustChangePassword,
        u.LastLoginAt, u.LastLoginIp, u.CreatedAt,
        u.Roles.Select(r => r.Role!.Name).ToList(),
        ParseUiPreferences(u.UiPreferencesJson));

    public static UserUiPreferencesDto? ParseUiPreferences(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return NormalizeUiPreferences(JsonSerializer.Deserialize<UserUiPreferencesDto>(raw, UiPreferencesJsonOptions));
        }
        catch
        {
            return null;
        }
    }

    public static string? SerializeUiPreferences(UserUiPreferencesDto? preferences)
    {
        var normalized = NormalizeUiPreferences(preferences);
        if (normalized is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(normalized, UiPreferencesJsonOptions);
    }

    private static UserUiPreferencesDto? NormalizeUiPreferences(UserUiPreferencesDto? preferences)
    {
        if (preferences is null)
        {
            return null;
        }

        var theme = NormalizeThemePreferences(preferences.Theme);
        var ratingSystemOptions = NormalizeRatingSystemOptions(preferences.RatingSystemOptions);
        var recordPlaybackHistory = preferences.RecordPlaybackHistory;
        if (theme is null && ratingSystemOptions is null && recordPlaybackHistory is null)
        {
            return null;
        }

        return new UserUiPreferencesDto(theme, ratingSystemOptions, recordPlaybackHistory);
    }

    private static UserThemePreferencesDto? NormalizeThemePreferences(UserThemePreferencesDto? theme)
    {
        if (theme is null)
        {
            return null;
        }

        var activeThemeId = string.IsNullOrWhiteSpace(theme.ActiveThemeId) ? null : theme.ActiveThemeId.Trim();
        var activeComponentStyles = theme.ActiveComponentStyles?
            .Select(style => style?.Trim())
            .Where(style => !string.IsNullOrWhiteSpace(style))
            .Select(style => style!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeLayoutStyle = string.IsNullOrWhiteSpace(theme.ActiveLayoutStyle) ? null : theme.ActiveLayoutStyle.Trim();

        Dictionary<string, string>? customThemeColors = null;
        if (theme.CustomThemeColors is { Count: > 0 })
        {
            customThemeColors = theme.CustomThemeColors
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                .ToDictionary(entry => entry.Key.Trim(), entry => entry.Value.Trim(), StringComparer.OrdinalIgnoreCase);
            if (customThemeColors.Count == 0)
            {
                customThemeColors = null;
            }
        }

        Dictionary<string, Dictionary<string, string>>? styleOptions = null;
        if (theme.StyleOptions is { Count: > 0 })
        {
            styleOptions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (styleId, options) in theme.StyleOptions)
            {
                if (string.IsNullOrWhiteSpace(styleId) || options is null || options.Count == 0)
                {
                    continue;
                }

                var normalizedOptions = options
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    .ToDictionary(entry => entry.Key.Trim(), entry => entry.Value.Trim(), StringComparer.OrdinalIgnoreCase);
                if (normalizedOptions.Count > 0)
                {
                    styleOptions[styleId.Trim()] = normalizedOptions;
                }
            }

            if (styleOptions.Count == 0)
            {
                styleOptions = null;
            }
        }

        if (activeThemeId is null
            && (activeComponentStyles is null || activeComponentStyles.Length == 0)
            && activeLayoutStyle is null
            && customThemeColors is null
            && styleOptions is null)
        {
            return null;
        }

        return new UserThemePreferencesDto(activeThemeId, activeComponentStyles, activeLayoutStyle, customThemeColors, styleOptions);
    }

    private static UserRatingSystemOptionsDto? NormalizeRatingSystemOptions(UserRatingSystemOptionsDto? ratingSystemOptions)
    {
        if (ratingSystemOptions is null)
        {
            return null;
        }

        var type = ratingSystemOptions.Type?.Trim().ToLowerInvariant() switch
        {
            "stars" => "stars",
            "decimal" => "decimal",
            _ => null,
        };
        if (type is null)
        {
            return null;
        }

        var starPrecision = ratingSystemOptions.StarPrecision?.Trim().ToLowerInvariant() switch
        {
            "full" => "full",
            "half" => "half",
            "quarter" => "quarter",
            "tenth" => "tenth",
            _ => "full",
        };

        return new UserRatingSystemOptionsDto(type, starPrecision);
    }

    public static class Validation
    {
        public static void Username(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length < 2 || username.Length > 64)
                throw new InvalidOperationException("Username must be 2-64 characters.");
            foreach (var c in username)
                if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
                    throw new InvalidOperationException("Username may only contain letters, digits, '_', '-', '.'.");
        }
        public static void Password(string pw)
        {
            if (string.IsNullOrEmpty(pw) || pw.Length < 8 || pw.Length > 200)
                throw new InvalidOperationException("Password must be 8-200 characters.");
        }
    }
}

public sealed class RoleService : IRoleService
{
    private readonly CoveContext _db;
    private readonly IPermissionRegistry _registry;
    private readonly IAuditService _audit;

    public RoleService(CoveContext db, IPermissionRegistry registry, IAuditService audit)
    {
        _db = db;
        _registry = registry;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var roles = await _db.Roles.AsNoTracking()
            .Include(r => r.Permissions)
            .Include(r => r.Users)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);
        return roles.Select(Map).ToList();
    }

    public async Task<RoleDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var r = await _db.Roles.AsNoTracking()
            .Include(x => x.Permissions)
            .Include(x => x.Users)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? null : Map(r);
    }

    public async Task<RoleDto?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var r = await _db.Roles.AsNoTracking()
            .Include(x => x.Permissions)
            .Include(x => x.Users)
            .FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), ct);
        return r is null ? null : Map(r);
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest req, CovePrincipal? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new InvalidOperationException("Role name is required.");
        var exists = await _db.Roles.AnyAsync(r => r.Name.ToLower() == req.Name.ToLower(), ct);
        if (exists) throw new InvalidOperationException("Role name already in use.");
        ValidatePermissions(req.Permissions);

        var role = new Role
        {
            Name = req.Name,
            Description = req.Description,
            IsBuiltin = false,
            IsSystem = false,
            Source = "core",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);

        foreach (var p in req.Permissions.Distinct())
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = p });
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditActions.RoleCreate, AuditOutcomes.Success, actor,
            "role", role.Id.ToString(), new { role.Name }, ct);

        return (await GetAsync(role.Id, ct))!;
    }

    public async Task<RoleDto> UpdateAsync(int id, UpdateRoleRequest req, CovePrincipal? actor, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("Role not found.");

        if (req.Description is not null) role.Description = req.Description;
        if (req.Permissions is { } permissions)
        {
            ValidatePermissions(permissions);
            if (role.IsSystem)
            {
                // Owner role: must always include "*"
                if (!permissions.Contains("*"))
                    throw new InvalidOperationException("Cannot remove '*' from the Owner role.");
            }
            await SetPermissionsAsync(id, permissions, actor, ct);
        }
        role.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditActions.RoleUpdate, AuditOutcomes.Success, actor,
            "role", role.Id.ToString(), new { role.Name }, ct);

        return (await GetAsync(role.Id, ct))!;
    }

    public async Task DeleteAsync(int id, CovePrincipal? actor, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsBuiltin) throw new InvalidOperationException("Built-in roles cannot be deleted.");
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.RoleDelete, AuditOutcomes.Success, actor,
            "role", id.ToString(), new { role.Name }, ct);
    }

    public async Task SetPermissionsAsync(int roleId, IEnumerable<string> permissions, CovePrincipal? actor, CancellationToken ct = default)
    {
        var list = permissions.Distinct(StringComparer.Ordinal).ToList();
        ValidatePermissions(list);
        await _db.RolePermissions.Where(r => r.RoleId == roleId).ExecuteDeleteAsync(ct);
        foreach (var p in list)
            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionKey = p });
        await _db.SaveChangesAsync(ct);
    }

    private void ValidatePermissions(IEnumerable<string> permissions)
    {
        foreach (var p in permissions)
        {
            if (string.IsNullOrWhiteSpace(p))
                throw new InvalidOperationException("Empty permission key.");
            if (p == "*") continue;
            if (p.EndsWith(".*", StringComparison.Ordinal))
            {
                var resource = p[..^2];
                if (string.IsNullOrEmpty(resource)) throw new InvalidOperationException($"Invalid permission '{p}'.");
                continue;
            }
            if (p.StartsWith("*.", StringComparison.Ordinal)) continue;
            if (!_registry.IsKnown(p))
                throw new InvalidOperationException($"Unknown permission '{p}'.");
        }
    }

    private static RoleDto Map(Role r) => new(
        r.Id, r.Name, r.Description, r.IsBuiltin, r.IsSystem, r.Source,
        r.Permissions.Select(p => p.PermissionKey).OrderBy(x => x).ToList(),
        r.Users.Count);
}
