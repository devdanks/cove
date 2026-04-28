namespace Cove.Core.Entities.Auth;

/// <summary>
/// User-scoped long-lived API token. Optionally scoped to a subset of the user's permissions
/// (never expansive). Plaintext is shown to the user once at creation; only a BCrypt hash
/// and a short prefix for identification are persisted.
/// </summary>
public class ApiToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the secret. Plaintext is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>First 4 chars of the secret (for UI identification).</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>JSON array of permission keys; null = full user permission set.</summary>
    public string? ScopePermissions { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public User? User { get; set; }
}
