namespace Cove.Core.Entities.Auth;

/// <summary>
/// Per-entity allow/deny override for a specific role. Used both for one-off exceptions
/// and as the storage backing for share-link "explicit selection" semantics.
/// </summary>
public class RoleEntityOverride : BaseEntity
{
    public int RoleId { get; set; }
    public string EntityKind { get; set; } = string.Empty;

    /// <summary>Text to accept any PK type (int, uuid, composite).</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>'allow' | 'deny'</summary>
    public string Effect { get; set; } = "allow";

    /// <summary>'read' | 'write' | 'delete' | 'all'</summary>
    public string AppliesTo { get; set; } = "all";

    public Role? Role { get; set; }
}
