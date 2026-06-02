namespace Cove.Core.Entities;

public enum AffinityHostType
{
    Video = 1,
    Image = 2,
    Performer = 3,
    Face = 4,
    Tag = 5,
    Studio = 6,
    Gallery = 7,
    Group = 8,
    Audio = 9,
    Text = 10,
    Segment = 11,
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
    public int InteractionCount { get; set; }
    public DateTime? LastInteractedAt { get; set; }
    public int OpenDetailCount { get; set; }
    public int OpenLightboxCount { get; set; }
    public int NavigateCount { get; set; }
    public int PauseCount { get; set; }
    public int SeekCount { get; set; }
    public int PlayerControlCount { get; set; }
    public int SearchInteractionCount { get; set; }
    public int FilterInteractionCount { get; set; }
    public int ShareCount { get; set; }
    public int HideCount { get; set; }
    public int ZoomCount { get; set; }
}
