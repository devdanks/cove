using System.Text.Json;

namespace Cove.Core.Entities;

public enum FaceAppearanceHostType
{
    Scene = 1,
    Image = 2,
}

public class FaceAppearance : BaseEntity
{
    public int FaceId { get; set; }
    public FaceAppearanceHostType HostType { get; set; }
    public int HostId { get; set; }

    public double? FirstSeenAtSec { get; set; }
    public double? LastSeenAtSec { get; set; }
    public int SampleCount { get; set; }
    public int RetainedSpatialSampleCount { get; set; }
    public int SegmentCount { get; set; }
    public double? RepresentativeFrameSec { get; set; }
    public float? TopConfidence { get; set; }
    public string? GroupKey { get; set; }
    public JsonDocument? Payload { get; set; }

    public string SourceKey { get; set; } = string.Empty;
    public string? SourceRunId { get; set; }

    public Face? Face { get; set; }
}