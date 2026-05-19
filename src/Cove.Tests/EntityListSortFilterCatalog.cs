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
    public const string GroupRatingFilterTracking = "broken; P1.B harness exposed GroupFilter.RatingCriterion returning no group rows because GroupRepository applies Gallery rating host type";

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
        new("audios", "title", "Title"),

        // Texts
        new("texts", "updatedAt", "Updated At"),
        new("texts", "createdAt", "Created At"),
        new("texts", "date", "Date"),
        new("texts", "words", "Words"),
        new("texts", "pages", "Pages"),
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

                var knownBroken = (entity, property.Name) switch
                {
                    ("groups", nameof(GroupFilter.RatingCriterion)) => GroupRatingFilterTracking,
                    _ => null,
                };
                rows.Add(new EntityListFilterDefinition(entity, property.Name, criterionType, operators, knownBroken));
            }
        }

        rows.AddRange([
            new EntityListFilterDefinition("faces", "performerId", "query-param:int", ["equals"]),
            new EntityListFilterDefinition("faces", "linked", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("faces", "ignored", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("faces", "merged", "query-param:bool", ["true", "false"]),
            new EntityListFilterDefinition("segments", "ids", "query-param:int-list", ["includes"]),
            new EntityListFilterDefinition("segments", "sceneId", "query-param:int", ["equals"]),
            new EntityListFilterDefinition("segments", "sceneIds", "query-param:int-list", ["includes"]),
            new EntityListFilterDefinition("segments", "excludeSceneIds", "query-param:int-list", ["excludes"]),
            new EntityListFilterDefinition("segments", "sceneTitle", "query-param:string", ["includes"]),
            new EntityListFilterDefinition("segments", "tagId", "query-param:int", ["equals"]),
            new EntityListFilterDefinition("segments", "tagIds", "query-param:int-list", ["includes"]),
            new EntityListFilterDefinition("segments", "kind", "query-param:string", ["includes"]),
            new EntityListFilterDefinition("segments", "sourceKey", "query-param:string", ["includes"]),
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