using System.Text.Json;

namespace Cove.Core.Entities;

public enum SegmentHostType
{
    Scene = 1,
    Image = 2,
    Audio = 3
}

public enum DetectionHostType
{
    Scene = 1,
    Image = 2
}

public class Segment : BaseEntity
{
    public SegmentHostType HostType { get; set; }
    public int HostId { get; set; }

    public double StartSec { get; set; }
    public double? EndSec { get; set; }

    public int? TagId { get; set; }
    public string? Kind { get; set; }
    public long? RefId { get; set; }
    public JsonDocument? Payload { get; set; }

    public string SourceKey { get; set; } = "user";
    public string? SourceRunId { get; set; }
    public float? Confidence { get; set; }

    public string? Title { get; set; }
    public string? ColorHint { get; set; }
    public string? ImageBlobId { get; set; }

    public Tag? Tag { get; set; }
}

public class SegmentDisplayProfile : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? UserId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsDefault { get; set; }
    public int Version { get; set; }

    public ICollection<SegmentDisplayRule> Rules { get; set; } = [];
}

public class SegmentDisplayRule : BaseEntity
{
    public int ProfileId { get; set; }
    public string? SourceKey { get; set; }
    public string? Kind { get; set; }
    public int? TagId { get; set; }
    public string? TagCategory { get; set; }
    public SegmentHostType? HostType { get; set; }

    public bool Visible { get; set; } = true;
    public float? MinConfidence { get; set; }
    public double? MinDurationSec { get; set; }
    public double? MergeGapSec { get; set; }
    public bool CollapseToInstant { get; set; }

    public string? ColorOverride { get; set; }
    public int? Lane { get; set; }
    public int? Priority { get; set; }

    public int? UserId { get; set; }

    public SegmentDisplayProfile? Profile { get; set; }
    public Tag? Tag { get; set; }
}

public class Detection : BaseEntity
{
    public DetectionHostType HostType { get; set; }
    public int HostId { get; set; }

    public double? ObservedAtSec { get; set; }
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }

    public string Class { get; set; } = string.Empty;
    public float Score { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public JsonDocument? Extra { get; set; }

    public string? RefKind { get; set; }
    public long? RefId { get; set; }
    public string? GroupKey { get; set; }

    public string SourceKey { get; set; } = string.Empty;
    public string? SourceRunId { get; set; }
}