using System.Text.Json;

namespace Cove.Core.Entities;

public enum InteractionHostType
{
    Scene = 1,
    Image = 2,
    Performer = 3,
    Tag = 4,
    Face = 5,
    Segment = 6,
    Studio = 7,
    Gallery = 8,
    Group = 9,
    Search = 10,
    Collection = 11,
}

public enum InteractionKind
{
    // 1-3 reserved (formerly PlayStart, PlayProgress, PlayEnd — replaced by PlaybackInterval)
    Pause = 4,
    Seek = 5,
    Like = 6,
    Dislike = 7,
    LikeCount = 8,
    Share = 9,
    Hide = 10,
    OpenDetail = 11,
    OpenLightbox = 12,
    CloseLightbox = 13,
    Navigate = 14,
    Zoom = 15,
    SearchQuery = 16,
    SearchSelect = 17,
    FilterApply = 18,
    FilterClear = 19,
    PageVisit = 20,
    DerivedLike = 21,
}

/// <summary>Non-playback engagement event (search, filter, image open, etc.). Playback is tracked in PlaybackSession/PlaybackInterval.</summary>
public class Interaction : BaseEntity
{
    public int UserId { get; set; }
    public InteractionHostType HostType { get; set; }
    public int HostId { get; set; }
    public InteractionKind Kind { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public JsonDocument? Meta { get; set; }
}