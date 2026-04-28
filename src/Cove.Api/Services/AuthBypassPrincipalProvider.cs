using Cove.Core.Auth;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public sealed class AuthBypassPrincipalProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthBypassPrincipalProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int? _cachedUserId;
    private string _cachedUsername = "owner";

    public AuthBypassPrincipalProvider(IServiceScopeFactory scopeFactory, ILogger<AuthBypassPrincipalProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async ValueTask<CovePrincipal> GetAsync(string? ip, string? userAgent, CancellationToken ct)
    {
        if (_cachedUserId is int cachedUserId)
            return CreatePrincipal(cachedUserId, _cachedUsername, ip, userAgent);

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedUserId is null)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
                var systemUser = await db.Users.AsNoTracking()
                    .Where(user => user.IsSystem && user.IsActive && !user.IsLocked)
                    .OrderBy(user => user.Id)
                    .Select(user => new { user.Id, user.Username })
                    .FirstOrDefaultAsync(ct);

                if (systemUser is not null)
                {
                    _cachedUserId = systemUser.Id;
                    _cachedUsername = systemUser.Username;
                }
                else
                {
                    _logger.LogWarning("Authentication bypass requested with auth disabled, but no active system user was found");
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return _cachedUserId is int resolvedUserId
            ? CreatePrincipal(resolvedUserId, _cachedUsername, ip, userAgent)
            : CreateFallbackPrincipal(ip, userAgent);
    }

    private static CovePrincipal CreatePrincipal(int userId, string username, string? ip, string? userAgent) => new()
    {
        UserId = userId,
        Username = username,
        Kind = PrincipalKind.System,
        Roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Owner" },
        Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" },
        Ip = ip,
        UserAgent = userAgent,
    };

    private static CovePrincipal CreateFallbackPrincipal(string? ip, string? userAgent) => new()
    {
        UserId = null,
        Username = "system",
        Kind = PrincipalKind.System,
        Roles = new HashSet<string>(),
        Permissions = new HashSet<string> { "*" },
        Ip = ip,
        UserAgent = userAgent,
    };
}