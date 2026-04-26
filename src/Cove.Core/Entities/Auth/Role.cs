namespace Cove.Core.Entities.Auth;

/// <summary>
/// A named bundle of permissions assigned to users.
/// </summary>
public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>True for the seeded Owner/Admin/Member/Viewer/Guest roles. Cannot be deleted.</summary>
    public bool IsBuiltin { get; set; }

    /// <summary>True only for the Owner role. Cannot have permissions removed.</summary>
    public bool IsSystem { get; set; }

    /// <summary>"core" or "extension:&lt;extensionId&gt;".</summary>
    public string Source { get; set; } = "core";

    public ICollection<RolePermission> Permissions { get; set; } = [];
    public ICollection<UserRoleAssignment> Users { get; set; } = [];
    public ICollection<RoleContentRule> ContentRules { get; set; } = [];
    public ICollection<RoleEntityOverride> EntityOverrides { get; set; } = [];
}

public static class BuiltinRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";
    public const string Viewer = "Viewer";
    public const string Guest = "Guest";

    public static readonly string[] All = [Owner, Admin, Member, Viewer, Guest];
}
