using Cove.Core.Entities;

namespace Cove.Data.Services;

public static class SegmentAccessQueries
{
    public static IQueryable<Segment> VisibleSegments(this CoveContext db)
        => db.Segments.Where(segment =>
            segment.HostType == SegmentHostType.Scene && db.Scenes.Any(scene => scene.Id == segment.HostId)
            || segment.HostType == SegmentHostType.Audio && db.Audios.Any(audio => audio.Id == segment.HostId)
            || segment.HostType == SegmentHostType.Image && db.Images.Any(image => image.Id == segment.HostId));

    public static IQueryable<Detection> VisibleDetections(this CoveContext db)
        => db.Detections.Where(detection =>
            detection.HostType == DetectionHostType.Scene && db.Scenes.Any(scene => scene.Id == detection.HostId)
            || detection.HostType == DetectionHostType.Image && db.Images.Any(image => image.Id == detection.HostId));
}