namespace Cove.Core.Entities;

public class VideoMarker : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public double Seconds { get; set; }
    public double? EndSeconds { get; set; }
    public int PrimaryTagId { get; set; }
    public int VideoId { get; set; }

    // Navigation
    public Tag? PrimaryTag { get; set; }
    public Video? Video { get; set; }
    public ICollection<VideoMarkerTag> VideoMarkerTags { get; set; } = [];
}

public class VideoMarkerTag
{
    public int VideoMarkerId { get; set; }
    public int TagId { get; set; }
    public VideoMarker? VideoMarker { get; set; }
    public Tag? Tag { get; set; }
}

