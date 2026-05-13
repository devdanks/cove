namespace Cove.Core.Entities;

public class Face : BaseEntity
{
    public string? Label { get; set; }
    public int? PerformerId { get; set; }
    public string? CoverBlobId { get; set; }
    public bool Ignored { get; set; }
    public int? MergedIntoFaceId { get; set; }
    public int DetectionCount { get; set; }
    public int AppearanceCount { get; set; }
    public int FrameSampleCount { get; set; }
    public int SceneCount { get; set; }
    public int ImageCount { get; set; }
    public string? PrimarySourceKey { get; set; }
    public string? SearchText { get; set; }

    public Performer? Performer { get; set; }
    public Face? MergedIntoFace { get; set; }
    public ICollection<Face> MergedFaces { get; set; } = [];
    public ICollection<FaceAppearance> Appearances { get; set; } = [];
}