namespace Cove.Core.Entities;

public class FieldProvenance : BaseEntity
{
    public AffinityHostType HostType { get; set; }

    public int HostId { get; set; }

    public string FieldKey { get; set; } = string.Empty;

    public string? ValueJson { get; set; }

    public string SourceKey { get; set; } = "user";

    public string SourceRunId { get; set; } = string.Empty;

    public string ModelKey { get; set; } = string.Empty;

    public float? Confidence { get; set; }
}