using System.Text.Json;
using Pgvector;

namespace Cove.Core.Entities;

public enum EmbeddingHostType
{
    Scene = 1,
    Image = 2,
    Performer = 3,
    Face = 4,
    Segment = 5,
}

public enum EmbeddingModality
{
    Visual = 1,
    Audio = 2,
    Face = 3,
    Text = 4,
    Other = 5,
}

public class Embedding : BaseEntity
{
    public EmbeddingHostType HostType { get; set; }
    public int HostId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? KindFamily { get; set; }
    public EmbeddingModality Modality { get; set; }
    public bool IsSemantic { get; set; }
    public int Dim { get; set; }
    public Vector Vector { get; set; } = new(Array.Empty<float>());
    public int SectionIndex { get; set; }
    public double? StartSec { get; set; }
    public double? EndSec { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string? SourceRunId { get; set; }
    public JsonDocument? Meta { get; set; }
}