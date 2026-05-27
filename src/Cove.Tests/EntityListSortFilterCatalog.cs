using System.Reflection;
using Cove.Core.Interfaces;

namespace Cove.Tests;

public sealed record EntityListSortDefinition(string Entity, string Key, string Label, string? KnownBrokenReason = null)
{
    public string RowId => $"sort:{Entity}:{Key}";
}

public sealed record EntityListFilterDefinition(string Entity, string Key, string CriterionType, IReadOnlyList<string> Operators, string? KnownBrokenReason = null)
{
    public string RowId => $"filter:{Entity}:{Key}";
}

public static class EntityListSortFilterCatalog
{
    private static readonly IReadOnlyDictionary<string, Type> FilterTypesByEntity = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["scenes"] = typeof(SceneFilter),
        ["images"] = typeof(ImageFilter),
        ["audios"] = typeof(AudioFilter),
        ["texts"] = typeof(TextDocumentFilter),
        ["galleries"] = typeof(GalleryFilter),
        ["groups"] = typeof(GroupFilter),
        ["performers"] = typeof(PerformerFilter),
        ["studios"] = typeof(StudioFilter),
        ["tags"] = typeof(TagFilter),
    };

    public static IReadOnlyList<string> Entities { get; } =
    [
        "scenes",
        "images",
        "audios",
        "texts",
        "galleries",
        "groups",
        "segments",
        "performers",
        "studios",
        "tags",
        "faces",
    ];

    public static IReadOnlyList<EntityListSortDefinition> Sorts { get; } =
    [
        // Scenes
        new("scenes", "updated_at", "Updated At"),
        new("scenes", "created_at", "Created At"),
        new("scenes", "title", "Title"),
        new("scenes", "date", "Date"),
        new("scenes", "rating", "Rating"),
        new("scenes", "play_count", "Play Count"),
        new("scenes", "like_counter", "Likes"),
        new("scenes", "last_like_at", "Last Like Date"),
        new("scenes", "duration", "Duration"),
        new("scenes", "file_size", "File Size"),
        new("scenes", "file_mod_time", "File Modification Time"),
        new("scenes", "file_count", "File Count"),
        new("scenes", "path", "Path"),
        new("scenes", "resolution", "Resolution"),
        new("scenes", "framerate", "Frame Rate"),
        new("scenes", "bitrate", "Bitrate"),
        new("scenes", "phash", "pHash"),
        new("scenes", "tag_count", "Tag Count"),
        new("scenes", "performer_count", "Performer Count"),
        new("scenes", "performer_age", "Performer Age"),
        new("scenes", "studio", "Studio"),
        new("scenes", "code", "Studio Code"),
        new("scenes", "last_played_at", "Last Played"),
        new("scenes", "play_duration", "Play Duration"),
        new("scenes", "resume_time", "Resume Time"),
        new("scenes", "organized", "Organized"),
        new("scenes", "random", "Random"),

        // Images
        new("images", "updated_at", "Updated At"),
        new("images", "created_at", "Created At"),
        new("images", "date", "Date"),
        new("images", "file_mod_time", "File Modification Time"),
        new("images", "file_size", "File Size"),
        new("images", "resolution", "Resolution"),
        new("images", "path", "Path"),
        new("images", "title", "Title"),
        new("images", "rating", "Rating"),
        new("images", "like_counter", "Likes"),
        new("images", "performer_count", "Performer Count"),
        new("images", "tag_count", "Tag Count"),
        new("images", "random", "Random"),
        new("images", "visual_match", "Visual Match"),

        // Audios
        new("audios", "updatedAt", "Updated At"),
        new("audios", "createdAt", "Created At"),
        new("audios", "date", "Date"),
        new("audios", "duration", "Duration"),
        new("audios", "rating", "Rating"),
        new("audios", "play_count", "Play Count"),
        new("audios", "like_counter", "Likes"),
        new("audios", "play_duration", "Play Duration"),
        new("audios", "last_played_at", "Last Played"),
        new("audios", "file_size", "File Size"),
        new("audios", "file_mod_time", "File Modification Time"),
        new("audios", "file_count", "File Count"),
        new("audios", "path", "Path"),
        new("audios", "bitrate", "Bitrate"),
        new("audios", "has_video_files", "Has Video Files"),
        new("audios", "track_count", "Track Count"),
        new("audios", "tag_count", "Tag Count"),
        new("audios", "performer_count", "Performer Count"),
        new("audios", "title", "Title"),

        // Texts
        new("texts", "updatedAt", "Updated At"),
        new("texts", "createdAt", "Created At"),
        new("texts", "date", "Date"),
        new("texts", "words", "Words"),
        new("texts", "pages", "Pages"),
        new("texts", "rating", "Rating"),
        new("texts", "read_count", "Read Count"),
        new("texts", "like_counter", "Likes"),
        new("texts", "read_duration", "Read Duration"),
        new("texts", "last_read_at", "Last Read"),
        new("texts", "file_size", "File Size"),
        new("texts", "file_mod_time", "File Modification Time"),
        new("texts", "file_count", "File Count"),
        new("texts", "path", "Path"),
        new("texts", "tag_count", "Tag Count"),
        new("texts", "performer_count", "Performer Count"),
        new("texts", "title", "Title"),

        // Galleries
        new("galleries", "updated_at", "Updated At"),
        new("galleries", "created_at", "Created At"),
        new("galleries", "date", "Date"),
        new("galleries", "file_mod_time", "File Modification Time"),
        new("galleries", "path", "Path"),
        new("galleries", "title", "Title"),
        new("galleries", "rating", "Rating"),
        new("galleries", "image_count", "Image Count"),
        new("galleries", "performer_count", "Performer Count"),
        new("galleries", "tag_count", "Tag Count"),
        new("galleries", "random", "Random"),

        // Groups
        new("groups", "sort_order", "Manual Order"),
        new("groups", "name", "Name"),
        new("groups", "date", "Date"),
        new("groups", "rating", "Rating"),
        new("groups", "random", "Random"),
        new("groups", "created_at", "Created At"),
        new("groups", "updated_at", "Updated At"),
        new("groups", "item_count", "Item Count"),
        new("groups", "scene_count", "Scene Count"),
        new("groups", "image_count", "Image Count"),
        new("groups", "audio_count", "Audio Count"),
        new("groups", "text_count", "Text Count"),
        new("groups", "gallery_count", "Gallery Count"),
        new("groups", "performer_count", "Performer Item Count"),
        new("groups", "studio_count", "Studio Item Count"),
        new("groups", "tag_item_count", "Tag Item Count"),
        new("groups", "tag_count", "Tag Count"),
        new("groups", "face_count", "Face Count"),
        new("groups", "segment_count", "Segment Count"),
        new("groups", "subgroup_count", "Subgroup Count"),
        new("groups", "containing_group_count", "Containing Group Count"),
        new("groups", "cached_item_count", "Cached Item Count"),
        new("groups", "last_resolved_at", "Last Resolved"),
        new("groups", "query_source_key", "Query Source Key"),
        new("groups", "show_in_scene_lists", "Show In Scene Lists"),
        new("groups", "aliases", "Aliases"),

        // Segments
        new("segments", "updated_at", "Updated At"),
        new("segments", "created_at", "Created At"),
        new("segments", "start_sec", "Start Time"),
        new("segments", "end_sec", "End Time"),
        new("segments", "duration", "Duration"),
        new("segments", "confidence", "Confidence"),
        new("segments", "title", "Title"),
        new("segments", "scene_title", "Scene Title"),
        new("segments", "kind", "Kind"),
        new("segments", "source_key", "Source Key"),
        new("segments", "tag_name", "Tag Name"),
        new("segments", "performer", "Performer"),
        new("segments", "ref", "Reference"),

        // Performers
        new("performers", "name", "Name"),
        new("performers", "rating", "Rating"),
        new("performers", "scene_count", "Scene Count"),
        new("performers", "image_count", "Image Count"),
        new("performers", "gallery_count", "Gallery Count"),
        new("performers", "latest_scene_date", "Latest Scene Date"),
        new("performers", "total_file_size", "Total File Size"),
        new("performers", "tag_count", "Tag Count"),
        new("performers", "career_length", "Career Length"),
        new("performers", "last_like_at", "Last Like At"),
        new("performers", "last_played_at", "Last Played At"),
        new("performers", "measurements", "Measurements"),
        new("performers", "like_counter", "Likes"),
        new("performers", "play_count", "Play Count"),
        new("performers", "birthdate", "Birthdate"),
        new("performers", "height", "Height"),
        new("performers", "weight", "Weight"),
        new("performers", "created_at", "Created At"),
        new("performers", "updated_at", "Updated At"),
        new("performers", "random", "Random"),

        // Studios
        new("studios", "name", "Name"),
        new("studios", "rating", "Rating"),
        new("studios", "scene_count", "Scene Count"),
        new("studios", "gallery_count", "Gallery Count"),
        new("studios", "image_count", "Image Count"),
        new("studios", "latest_scene_date", "Latest Scene Date"),
        new("studios", "total_file_size", "Total File Size"),
        new("studios", "parent_count", "Parent Studio Count"),
        new("studios", "child_count", "Substudios Count"),
        new("studios", "tag_count", "Tag Count"),
        new("studios", "updated_at", "Updated At"),
        new("studios", "random", "Random"),
        new("studios", "created_at", "Created At"),

        // Tags
        new("tags", "name", "Name"),
        new("tags", "tag_group", "Tag Group"),
        new("tags", "scene_count", "Scene Count"),
        new("tags", "gallery_count", "Gallery Count"),
        new("tags", "group_count", "Group Count"),
        new("tags", "image_count", "Image Count"),
        new("tags", "performer_count", "Performer Count"),
        new("tags", "studio_count", "Studio Count"),
        new("tags", "latest_scene_date", "Latest Scene Date"),
        new("tags", "total_file_size", "Total File Size"),
        new("tags", "random", "Random"),
        new("tags", "created_at", "Created At"),
        new("tags", "updated_at", "Updated At"),

        // Faces
        new("faces", "suggestion_confidence", "Suggested match confidence"),
        new("faces", "updated_desc", "Recently updated"),
        new("faces", "created_desc", "Recently created"),
        new("faces", "appearance_desc", "Most appearances"),
        new("faces", "scene_count_desc", "Most scenes"),
        new("faces", "image_count_desc", "Most images"),
        new("faces", "label_asc", "Label A-Z"),
        new("faces", "label_desc", "Label Z-A"),
        new("faces", "performer_name_asc", "Performer A-Z"),
        new("faces", "performer_name_desc", "Performer Z-A"),
        new("faces", "primary_source_key_asc", "Primary Source A-Z"),
        new("faces", "primary_source_key_desc", "Primary Source Z-A"),
        new("faces", "ignored_asc", "Ignored Last"),
        new("faces", "ignored_desc", "Ignored First"),
        new("faces", "merged_asc", "Merged Last"),
        new("faces", "merged_desc", "Merged First"),
        new("faces", "cover_present_asc", "Missing Cover First"),
        new("faces", "cover_present_desc", "Has Cover First"),
        new("faces", "detection_count_asc", "Detection Count Asc"),
        new("faces", "detection_count_desc", "Detection Count Desc"),
        new("faces", "appearance_count_asc", "Appearance Count Asc"),
        new("faces", "appearance_count_desc", "Appearance Count Desc"),
        new("faces", "frame_sample_count_asc", "Frame Sample Count Asc"),
        new("faces", "frame_sample_count_desc", "Frame Sample Count Desc"),
        new("faces", "scene_count_asc", "Scene Count Asc"),
        new("faces", "image_count_asc", "Image Count Asc"),
    ];

    public static IReadOnlyList<EntityListFilterDefinition> Filters { get; } = BuildFilters();

    public static Type? GetFilterType(string entity)
        => FilterTypesByEntity.TryGetValue(entity, out var filterType) ? filterType : null;

    private static IReadOnlyList<EntityListFilterDefinition> BuildFilters()
    {
        var rows = new List<EntityListFilterDefinition>();
        foreach (var (entity, filterType) in FilterTypesByEntity)
        {
            foreach (var property in filterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name is nameof(SceneFilter.CustomFieldCriteria) or nameof(SceneFilter.CustomFieldCriterion))
                    continue;

                var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (!TryGetCriterionOperators(propertyType, out var criterionType, out var operators))
                    continue;

                rows.Add(new EntityListFilterDefinition(entity, property.Name, criterionType, operators));
            }
        }

        rows.AddRange([
            new EntityListFilterDefinition("faces", "performerId", "query-param:int", ["equals"]),
            new EntityListFilterDefinition("faces", "linked", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("faces", "ignored", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("faces", "merged", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("faces", "mergedIntoFaceId", "query-param:int", ["equals"]),
            new EntityListFilterDefinition("faces", "label", "query-param:string", ["equals", "not_equals", "includes", "excludes", "matches_regex", "not_matches_regex", "is_null", "not_null"]),
            new EntityListFilterDefinition("faces", "primarySourceKey", "query-param:string", ["equals", "not_equals", "includes", "excludes", "matches_regex", "not_matches_regex", "is_null", "not_null"]),
            new EntityListFilterDefinition("faces", "hasCover", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("faces", "detectionCount", "query-param:int", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("faces", "appearanceCount", "query-param:int", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("faces", "frameSampleCount", "query-param:int", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("faces", "sceneCount", "query-param:int", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("faces", "imageCount", "query-param:int", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("segments", "ids", "query-param:int-list", ["includes"]),
            new EntityListFilterDefinition("segments", "sceneId", "query-param:int", ["equals"]),
            new EntityListFilterDefinition("segments", "sceneIds", "query-param:int-list", ["includes"]),
            new EntityListFilterDefinition("segments", "excludeSceneIds", "query-param:int-list", ["excludes"]),
            new EntityListFilterDefinition("segments", "sceneTitle", "query-param:string", ["includes"]),
            new EntityListFilterDefinition("segments", "tagId", "query-param:int", ["equals"]),
            new EntityListFilterDefinition("segments", "tagIds", "query-param:int-list", ["includes"]),
            new EntityListFilterDefinition("segments", "kind", "query-param:string", ["includes"]),
            new EntityListFilterDefinition("segments", "sourceKey", "query-param:string", ["includes"]),
            new EntityListFilterDefinition("segments", "title", "query-param:string", ["equals", "not_equals", "includes", "excludes", "is_null", "not_null"]),
            new EntityListFilterDefinition("segments", "hostType", "query-param:enum", ["equals"]),
            new EntityListFilterDefinition("segments", "sourceRunId", "query-param:string", ["equals", "not_equals", "includes", "excludes", "is_null", "not_null"]),
            new EntityListFilterDefinition("segments", "colorHint", "query-param:string", ["equals", "not_equals", "includes", "excludes", "is_null", "not_null"]),
            new EntityListFilterDefinition("segments", "hasImage", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("segments", "hasPayload", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("segments", "startSec", "query-param:number", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("segments", "endSec", "query-param:number", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("segments", "createdAt", "query-param:timestamp", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("segments", "updatedAt", "query-param:timestamp", ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"]),
            new EntityListFilterDefinition("segments", "tagged", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("segments", "minConfidence", "query-param:number", ["greater_than_or_equal"]),
            new EntityListFilterDefinition("segments", "minDurationSec", "query-param:number", ["greater_than_or_equal"]),
        ]);

        return rows
            .OrderBy(row => EntityOrder(row.Entity))
            .ThenBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int EntityOrder(string entity)
    {
        for (var index = 0; index < Entities.Count; index++)
        {
            if (Entities[index].Equals(entity, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return int.MaxValue;
    }

    private static bool TryGetCriterionOperators(Type type, out string criterionType, out IReadOnlyList<string> operators)
    {
        if (type == typeof(IntCriterion))
        {
            criterionType = nameof(IntCriterion);
            operators = ["equals", "not_equals", "greater_than", "less_than", "between", "not_between"];
            return true;
        }

        if (type == typeof(StringCriterion))
        {
            criterionType = nameof(StringCriterion);
            operators = ["equals", "not_equals", "includes", "excludes", "matches_regex", "not_matches_regex", "is_null", "not_null"];
            return true;
        }

        if (type == typeof(BoolCriterion))
        {
            criterionType = nameof(BoolCriterion);
            operators = ["true", "false"];
            return true;
        }

        if (type == typeof(MultiIdCriterion))
        {
            criterionType = nameof(MultiIdCriterion);
            operators = ["includes", "excludes", "includes_all", "excludes_all", "is_null", "not_null"];
            return true;
        }

        if (type == typeof(DateCriterion))
        {
            criterionType = nameof(DateCriterion);
            operators = ["equals", "not_equals", "greater_than", "less_than", "between", "not_between", "is_null", "not_null"];
            return true;
        }

        if (type == typeof(TimestampCriterion))
        {
            criterionType = nameof(TimestampCriterion);
            operators = ["equals", "not_equals", "greater_than", "less_than", "between", "not_between", "is_null", "not_null"];
            return true;
        }

        if (type == typeof(FingerprintCriterion))
        {
            criterionType = nameof(FingerprintCriterion);
            operators = ["equals", "not_equals", "includes", "excludes", "is_null", "not_null"];
            return true;
        }

        if (type == typeof(TagDurationCriterion))
        {
            criterionType = nameof(TagDurationCriterion);
            operators = ["greater_than", "less_than", "between", "not_between"];
            return true;
        }

        criterionType = string.Empty;
        operators = [];
        return false;
    }
}