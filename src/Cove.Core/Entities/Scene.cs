namespace Cove.Core.Entities;

public class Scene : BaseEntity
{
    public string? Title { get; set; }
    public string? Code { get; set; }
    public string? Details { get; set; }
    public string? Director { get; set; }
    public DateOnly? Date { get; set; }
    public bool Organized { get; set; }
    public int? StudioId { get; set; }
    public string? Captions { get; set; }
    public int? InteractiveSpeed { get; set; }
    public string? ImageBlobId { get; set; }
    public int? ParentSceneId { get; set; }
    public double? ClipStartSec { get; set; }
    public double? ClipEndSec { get; set; }

    // Denormalized M2M id sets, GIN-indexed. Maintained from SceneTags/ScenePerformers
    // by CoveContext on save. Lets tag/performer combo filters use a single index-only
    // array containment scan (e.g. WHERE tag_ids @> ARRAY[1,2,3]) instead of N joins.
    public int[] TagIds { get; set; } = [];
    public int[] PerformerIds { get; set; } = [];

    // Denormalized file summaries for hot list filters and sorts.
    public int FileCount { get; set; }
    public double MaxDuration { get; set; }
    public int MaxResolution { get; set; }
    public int MaxHeight { get; set; }
    public double MaxFrameRate { get; set; }
    public long MaxBitRate { get; set; }
    public long MaxFileSize { get; set; }
    public DateTime? MaxFileModTime { get; set; }
    public string? MinPath { get; set; }
    public string? MaxPath { get; set; }
    public string? FileSearchText { get; set; }
    public bool HasDimensionData { get; set; }
    public bool HasLandscapeFiles { get; set; }
    public bool HasPortraitFiles { get; set; }
    public bool HasSquareFiles { get; set; }
    public bool HasInteractiveFiles { get; set; }
    public bool HasNonInteractiveFiles { get; set; }

    // Navigation properties
    public Studio? Studio { get; set; }
    public Scene? ParentScene { get; set; }
    public ICollection<Scene> ChildScenes { get; set; } = [];
    public ICollection<SceneUrl> Urls { get; set; } = [];
    public ICollection<VideoFile> Files { get; set; } = [];
    public ICollection<SceneMarker> SceneMarkers { get; set; } = [];
    public ICollection<SceneTag> SceneTags { get; set; } = [];
    public ICollection<ScenePerformer> ScenePerformers { get; set; } = [];
    public ICollection<SceneGallery> SceneGalleries { get; set; } = [];
    public ICollection<GroupItem> GroupItems { get; set; } = [];
    public ICollection<SceneRemoteId> RemoteIds { get; set; } = [];
    public ICollection<ScenePlayHistory> PlayHistory { get; set; } = [];
    public ICollection<SceneLikeHistory> LikeHistory { get; set; } = [];
}

public class SceneUrl
{
    public int Id { get; set; }
    public int SceneId { get; set; }
    public string Url { get; set; } = string.Empty;
    public Scene? Scene { get; set; }
}

public class SceneTag
{
    public int SceneId { get; set; }
    public int TagId { get; set; }
    public Scene? Scene { get; set; }
    public Tag? Tag { get; set; }
}

public class ScenePerformer
{
    public int SceneId { get; set; }
    public int PerformerId { get; set; }
    public Scene? Scene { get; set; }
    public Performer? Performer { get; set; }
}

public class SceneGallery
{
    public int SceneId { get; set; }
    public int GalleryId { get; set; }
    public Scene? Scene { get; set; }
    public Gallery? Gallery { get; set; }
}

public class SceneRemoteId
{
    public int Id { get; set; }
    public int SceneId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string RemoteId { get; set; } = string.Empty;
    public Scene? Scene { get; set; }
}

public class ScenePlayHistory
{
    public int Id { get; set; }
    public int SceneId { get; set; }
    public DateTime PlayedAt { get; set; }
    public Scene? Scene { get; set; }
}

public class SceneLikeHistory
{
    public int Id { get; set; }
    public int SceneId { get; set; }
    public DateTime OccurredAt { get; set; }
    public Scene? Scene { get; set; }
}
