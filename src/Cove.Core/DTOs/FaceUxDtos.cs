namespace Cove.Core.DTOs;

public record FaceOverviewDto(
    int Id,
    string? Label,
    int? PerformerId,
    string? PerformerName,
    string? CoverImageUrl,
    bool Ignored,
    int? MergedIntoFaceId,
    int DetectionCount,
    int AppearanceCount,
    int FrameSampleCount,
    int SceneCount,
    int ImageCount,
    FaceTopSuggestionDto? TopSuggestion,
    string? PrimarySourceKey,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record FaceTopSuggestionDto(
    int PerformerId,
    string PerformerName,
    string? CoverImageUrl,
    float Confidence,
    int? LocalPerformerId = null,
    string? ExternalUrl = null,
    bool LocalPerformerHasImage = false,
    bool LocalPerformerIsLocalOnly = false);

public record FaceAppearanceDto(
    int AppearanceId,
    string HostType,
    int HostId,
    string Title,
    string ThumbnailUrl,
    int FrameSampleCount,
    int RetainedSpatialSampleCount,
    int SegmentCount,
    double? FirstSeenAtSec,
    double? LastSeenAtSec,
    float? TopConfidence);

public record FaceAppearancesResponseDto(
    IReadOnlyList<FaceAppearanceDto> Items,
    int TotalScenes,
    int TotalImages);