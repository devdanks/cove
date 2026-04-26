using System.Security.Claims;

namespace Cove.Core.Auth;

/// <summary>
/// Scoped per-request accessor for the resolved Cove principal (user + roles + permissions).
/// Populated by middleware after token validation.
/// </summary>
public interface ICurrentPrincipalAccessor
{
    CovePrincipal? Current { get; }
    void Set(CovePrincipal? principal);
}

public sealed class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    public CovePrincipal? Current { get; private set; }
    public void Set(CovePrincipal? principal) => Current = principal;
}

public sealed class CovePrincipal
{
    public required int? UserId { get; init; }
    public required string Username { get; init; }
    public required PrincipalKind Kind { get; init; }
    public required IReadOnlySet<string> Roles { get; init; }
    /// <summary>Resolved permission set with wildcards expanded.</summary>
    public required IReadOnlySet<string> Permissions { get; init; }
    public ClaimsPrincipal? ClaimsPrincipal { get; init; }
    /// <summary>For api_token / share_link principals: the originating token id.</summary>
    public Guid? TokenId { get; init; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }

    public static CovePrincipal Anonymous(string? ip = null, string? userAgent = null) => new()
    {
        UserId = null,
        Username = "anonymous",
        Kind = PrincipalKind.Anonymous,
        Roles = new HashSet<string>(),
        Permissions = new HashSet<string>(),
        Ip = ip,
        UserAgent = userAgent,
    };

    public static CovePrincipal System() => new()
    {
        UserId = null,
        Username = "system",
        Kind = PrincipalKind.System,
        Roles = new HashSet<string>(),
        Permissions = new HashSet<string> { Permissions_All },
    };

    private const string Permissions_All = "*";

    public bool Has(string permission)
    {
        if (Permissions.Contains("*")) return true;
        if (Permissions.Contains(permission)) return true;
        // wildcard "<resource>.*" or "*.read" support
        var dot = permission.IndexOf('.');
        if (dot < 0) return false;
        var resource = permission[..dot];
        var verb = permission[(dot + 1)..];
        if (Permissions.Contains(resource + ".*")) return true;
        if (Permissions.Contains("*." + verb)) return true;
        return false;
    }
}

public enum PrincipalKind
{
    Anonymous,
    User,
    ApiToken,
    ShareLink,
    System,
}
