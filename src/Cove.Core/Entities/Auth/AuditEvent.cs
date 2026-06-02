namespace Cove.Core.Entities.Auth;

/// <summary>
/// Append-only security audit log. Distinct from the entity-level event tables.
/// One row per privileged or noteworthy action (login, permission grant, delete, wipe, etc.).
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public int? ActorUserId { get; set; }

    /// <summary>'user' | 'api_token' | 'share_link' | 'system' | 'anonymous'</summary>
    public string ActorKind { get; set; } = "system";

    public string? Ip { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Dotted action key, e.g. "login.success", "video.delete", "permission.grant".</summary>
    public string Action { get; set; } = string.Empty;

    public string? TargetKind { get; set; }
    public string? TargetId { get; set; }

    /// <summary>'allow' | 'deny' | 'error' | 'success' | 'fail'</summary>
    public string Outcome { get; set; } = "success";

    /// <summary>JSON-encoded structured detail.</summary>
    public string? Detail { get; set; }
}

