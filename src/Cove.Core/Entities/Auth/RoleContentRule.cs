namespace Cove.Core.Entities.Auth;

/// <summary>
/// Content visibility rule: scopes whether a role can see/write/delete entities of a given kind,
/// optionally narrowed by tag/studio/identifier/attribute predicates.
/// Deny-overrides-allow: any matching DENY blocks the action regardless of ALLOWs.
/// </summary>
public class RoleContentRule : BaseEntity
{
    public int RoleId { get; set; }

    /// <summary>'scene' | 'performer' | 'tag' | 'studio' | 'gallery' | 'image' | 'group' | ...</summary>
    public string EntityKind { get; set; } = string.Empty;

    /// <summary>'allow' | 'deny'</summary>
    public string Effect { get; set; } = "deny";

    /// <summary>'all' | 'tag' | 'studio' | 'identifier' | 'attribute' | 'expression'</summary>
    public string ScopeKind { get; set; } = "all";

    /// <summary>JSON-encoded scope payload, interpretation depends on ScopeKind.</summary>
    public string ScopeValue { get; set; } = "{}";

    /// <summary>'read' | 'write' | 'delete' | 'all'</summary>
    public string AppliesTo { get; set; } = "all";

    public Role? Role { get; set; }
}
