namespace Cove.Core.Auth;

public enum PermissionMode
{
    /// <summary>The caller must hold every listed permission.</summary>
    All,
    /// <summary>The caller must hold at least one of the listed permissions.</summary>
    Any,
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute : Attribute
{
    public string[] Permissions { get; }
    public PermissionMode Mode { get; init; } = PermissionMode.All;

    public RequiresPermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
    }
}

/// <summary>
/// Marks a controller or action as exempt from the global default-deny filter.
/// The action still goes through standard authentication, but no permission check
/// is required (e.g. /api/auth/login, /api/system/status).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class AllowWithoutPermissionAttribute : Attribute
{
}
