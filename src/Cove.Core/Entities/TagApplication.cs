namespace Cove.Core.Entities;

public class TagApplication : BaseEntity
{
    public AffinityHostType HostType { get; set; }

    public int HostId { get; set; }

    public string? ContextType { get; set; }

    public int? ContextId { get; set; }

    public int TagId { get; set; }

    public string SourceKey { get; set; } = "user";

    public string SourceRunId { get; set; } = string.Empty;

    public string ModelKey { get; set; } = string.Empty;

    public float? Confidence { get; set; }

    public double? TotalDurationSec { get; set; }

    public double? HostDurationSec { get; set; }

    public Tag? Tag { get; set; }
}