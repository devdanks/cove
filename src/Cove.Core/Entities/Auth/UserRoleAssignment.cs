namespace Cove.Core.Entities.Auth;

/// <summary>Pivot row between a User and a Role.</summary>
public class UserRoleAssignment
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public int? GrantedByUserId { get; set; }

    public User? User { get; set; }
    public Role? Role { get; set; }
    public User? GrantedBy { get; set; }
}
