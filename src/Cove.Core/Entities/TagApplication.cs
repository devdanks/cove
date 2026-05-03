namespace Cove.Core.Entities;

public class TagApplication : BaseEntity
{
    public AffinityHostType HostType { get; set; }

    public int HostId { get; set; }

    public int TagId { get; set; }

    public string SourceKey { get; set; } = "user";

    public string SourceRunId { get; set; } = string.Empty;

    public string ModelKey { get; set; } = string.Empty;

    public float? Confidence { get; set; }

    public Tag? Tag { get; set; }
}