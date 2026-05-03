using System.Text.Json;

namespace Cove.Core.Entities;

public enum AiRunTargetType
{
    Scene = 1,
    Image = 2,
    Performer = 3,
    Face = 4,
}

public enum AiRunStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
}

public class AiRun : BaseEntity
{
    public string RunKey { get; set; } = Guid.NewGuid().ToString("n");
    public string SourceKey { get; set; } = string.Empty;
    public AiRunTargetType TargetType { get; set; }
    public int TargetId { get; set; }
    public string? Trigger { get; set; }
    public string? JobId { get; set; }
    public AiRunStatus Status { get; set; } = AiRunStatus.Pending;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? LoadPolicy { get; set; }
    public double? FrameIntervalSec { get; set; }
    public bool? Vr { get; set; }
    public JsonDocument? Request { get; set; }
    public JsonDocument? Models { get; set; }
    public JsonDocument? Summary { get; set; }
    public string? Error { get; set; }
}