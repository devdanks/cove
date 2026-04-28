namespace Cove.Core.Entities.Auth;

public class RolePermission
{
    public int RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;

    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}
