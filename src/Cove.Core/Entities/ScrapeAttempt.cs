namespace Cove.Core.Entities;

public class ScrapeAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ScraperId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string InputKind { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? CandidateResultsJson { get; set; }
    public string? EntitySnapshotJson { get; set; }
    public string Status { get; set; } = "Success";
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAt { get; set; }
    public string? AppliedByUser { get; set; }
}