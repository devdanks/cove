using Cove.Core.Entities;

namespace Cove.Data.Services;

public sealed class EffectiveHostTagRow
{
    public AffinityHostType HostType { get; set; }
    public int HostId { get; set; }
    public int TagId { get; set; }
    public bool IsManual { get; set; }
    public bool IsDerived { get; set; }
    public double? TotalDurationSec { get; set; }
    public double? HostDurationSec { get; set; }
}

public static class EffectiveHostTagQuery
{
    public static IQueryable<EffectiveHostTagRow> ForHostType(CoveContext db, AffinityHostType hostType)
        => ManualForHostType(db, hostType).Concat(DerivedForHostType(db, hostType));

    public static IQueryable<EffectiveHostTagRow> ManualForHostType(CoveContext db, AffinityHostType hostType)
        => hostType switch
        {
            AffinityHostType.Video => db.Set<VideoTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Video,
                HostId = link.VideoId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            AffinityHostType.Image => db.Set<ImageTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Image,
                HostId = link.ImageId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            AffinityHostType.Performer => db.Set<PerformerTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Performer,
                HostId = link.PerformerId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            AffinityHostType.Studio => db.Set<StudioTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Studio,
                HostId = link.StudioId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            AffinityHostType.Gallery => db.Set<GalleryTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Gallery,
                HostId = link.GalleryId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            AffinityHostType.Group => db.Set<GroupTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Group,
                HostId = link.GroupId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            AffinityHostType.Audio => db.Set<AudioTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Audio,
                HostId = link.AudioId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            AffinityHostType.Text => db.Set<TextTag>().Select(link => new EffectiveHostTagRow
            {
                HostType = AffinityHostType.Text,
                HostId = link.TextDocumentId,
                TagId = link.TagId,
                IsManual = true,
                IsDerived = false,
                TotalDurationSec = null,
                HostDurationSec = null,
            }),
            _ => throw new NotSupportedException($"Effective tags are not supported for host type '{hostType}'."),
        };

    public static IQueryable<EffectiveHostTagRow> DerivedForHostType(CoveContext db, AffinityHostType hostType)
        => from application in db.TagApplications
           join tag in db.Tags on application.TagId equals tag.Id
           where application.HostType == hostType
               && application.ContextType == null
               && application.ContextId == null
               && ((tag.MinOccurrenceSec == null && tag.MinOccurrencePercent == null)
                   || (tag.MinOccurrenceSec != null
                       && application.TotalDurationSec != null
                       && application.TotalDurationSec.Value >= tag.MinOccurrenceSec.Value)
                   || (tag.MinOccurrencePercent != null
                       && application.TotalDurationSec != null
                       && application.HostDurationSec != null
                       && application.HostDurationSec.Value > 0d
                       && application.TotalDurationSec.Value * 100d / application.HostDurationSec.Value >= tag.MinOccurrencePercent.Value))
           select new EffectiveHostTagRow
           {
               HostType = hostType,
               HostId = application.HostId,
               TagId = application.TagId,
               IsManual = false,
               IsDerived = true,
               TotalDurationSec = application.TotalDurationSec,
               HostDurationSec = application.HostDurationSec,
           };
}

