namespace Cove.Core.Entities.Auth;

/// <summary>
/// Catalog row for a single permission. Permissions are declared in code by core or
/// extensions; this table is upserted from the in-memory registry on startup.
/// The primary key is the permission key itself (e.g. "scenes.delete").
/// </summary>
public class Permission
{
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>"core" or "extension:&lt;extensionId&gt;".</summary>
    public string Source { get; set; } = "core";

    /// <summary>Flagged in UI; requires explicit confirmation to grant.</summary>
    public bool Dangerous { get; set; }

    /// <summary>JSON array of permission keys auto-granted alongside this one.</summary>
    public string Implies { get; set; } = "[]";

    /// <summary>True if a permission was previously declared but is no longer in the live registry.</summary>
    public bool IsOrphaned { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
