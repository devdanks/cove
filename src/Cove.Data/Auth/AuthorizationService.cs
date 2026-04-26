using Cove.Core.Auth;
using Cove.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cove.Data.Auth;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly CoveContext _db;
    private readonly ILogger<AuthorizationService> _log;

    public AuthorizationService(CoveContext db, ILogger<AuthorizationService> log)
    {
        _db = db;
        _log = log;
    }

    public bool Has(CovePrincipal? principal, string permission)
    {
        if (principal is null) return false;
        return principal.Has(permission);
    }

    public AuthorizationResult Authorize(CovePrincipal? principal, string permission, EntityRef? entity = null)
    {
        // Synchronous path — used by attribute filters that don't have async context.
        if (principal is null)
            return AuthorizationResult.Deny("Not authenticated.", permission);
        if (principal.Kind == PrincipalKind.Anonymous)
            return AuthorizationResult.Deny("Anonymous principal.", permission);
        if (!principal.Has(permission))
            return AuthorizationResult.Deny($"Missing permission '{permission}'.", permission);
        if (entity is { } e && HasOverrideDeny(principal, e))
            return AuthorizationResult.Deny($"Entity-level deny for {e.Kind}:{e.Id}.", permission);
        return AuthorizationResult.Allow();
    }

    public async Task<AuthorizationResult> AuthorizeAsync(CovePrincipal? principal, string permission, EntityRef? entity, CancellationToken ct)
    {
        if (principal is null)
            return AuthorizationResult.Deny("Not authenticated.", permission);
        if (principal.Kind == PrincipalKind.Anonymous)
            return AuthorizationResult.Deny("Anonymous principal.", permission);
        if (!principal.Has(permission))
            return AuthorizationResult.Deny($"Missing permission '{permission}'.", permission);
        if (entity is { } e && await HasOverrideDenyAsync(principal, e, ct))
            return AuthorizationResult.Deny($"Entity-level deny for {e.Kind}:{e.Id}.", permission);
        return AuthorizationResult.Allow();
    }

    public void Require(CovePrincipal? principal, string permission, EntityRef? entity = null)
    {
        var result = Authorize(principal, permission, entity);
        if (!result.Allowed)
            throw new ForbiddenException(result.Reason ?? "Forbidden.", permission, entity);
    }

    private bool HasOverrideDeny(CovePrincipal principal, EntityRef e)
    {
        if (principal.Roles.Count == 0) return false;
        var roleNames = principal.Roles.ToArray();
        return _db.RoleEntityOverrides
            .Where(o => roleNames.Contains(o.Role!.Name))
            .Where(o => o.EntityKind == e.Kind && o.EntityId == e.Id)
            .Where(o => o.Effect == "deny" && (o.AppliesTo == "all" || o.AppliesTo == "read"))
            .Any();
    }

    private Task<bool> HasOverrideDenyAsync(CovePrincipal principal, EntityRef e, CancellationToken ct)
    {
        if (principal.Roles.Count == 0) return Task.FromResult(false);
        var roleNames = principal.Roles.ToArray();
        return _db.RoleEntityOverrides
            .Where(o => roleNames.Contains(o.Role!.Name))
            .Where(o => o.EntityKind == e.Kind && o.EntityId == e.Id)
            .Where(o => o.Effect == "deny" && (o.AppliesTo == "all" || o.AppliesTo == "read"))
            .AnyAsync(ct);
    }
}
