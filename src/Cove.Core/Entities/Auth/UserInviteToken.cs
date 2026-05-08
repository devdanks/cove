namespace Cove.Core.Entities.Auth;

public class UserInviteToken : BaseEntity
{
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Purpose { get; set; } = "invite";
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? RolesJson { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
}