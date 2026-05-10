namespace Cove.Core.Entities;

public class UserBookmark
{
    public int UserId { get; set; }
    public AffinityHostType HostType { get; set; }
    public int HostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}