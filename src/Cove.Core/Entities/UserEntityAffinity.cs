namespace Cove.Core.Entities;

public enum AffinityHostType
{
    Scene = 1,
    Image = 2,
    Performer = 3,
    Face = 4,
    Tag = 5,
    Studio = 6,
    Gallery = 7,
    Group = 8,
    Audio = 9,
    Text = 10,
}

public class UserEntityAffinity : BaseEntity
{
    public int UserId { get; set; }
    public AffinityHostType HostType { get; set; }
    public int HostId { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? FavoritedAt { get; set; }
    public int ViewCount { get; set; }
    public int CompleteCount { get; set; }
    public double TotalConsumedSec { get; set; }
    public double? LastPositionSec { get; set; }
    public DateTime? LastConsumedAt { get; set; }
    public int LikeCount { get; set; }
    public int DerivedLikeCount { get; set; }
    public int PageVisitCount { get; set; }
}