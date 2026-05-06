using System.Globalization;
using System.Text.Json;

using Cove.Core.Entities;

using Microsoft.EntityFrameworkCore;

namespace Cove.Data.Services;

public sealed class FacePerformerPropagationService(CoveContext db)
{
    private const string ExtensionId = "cove.ai.faces";
    private const string AssignmentKeyPrefix = "performer-assignment:";

    private readonly CoveContext _db = db;

    public async Task ApplyLinkChangeAsync(int faceId, int? oldPerformerId, int? newPerformerId, CancellationToken cancellationToken = default)
    {
        if (oldPerformerId == newPerformerId)
        {
            return;
        }

        if (oldPerformerId.HasValue)
        {
            await RemoveAssignmentsAsync(faceId, oldPerformerId.Value, cancellationToken);
        }

        if (newPerformerId.HasValue)
        {
            await AddAssignmentsAsync(faceId, newPerformerId.Value, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<FaceHostRef>> LoadFaceHostsAsync(int faceId, CancellationToken cancellationToken = default)
    {
        var appearances = await _db.FaceAppearances
            .AsNoTracking()
            .Where(appearance => appearance.FaceId == faceId)
            .Select(appearance => new FaceHostRef(
                appearance.HostType == FaceAppearanceHostType.Scene ? FaceHostKind.Scene : FaceHostKind.Image,
                appearance.HostId,
                appearance.FirstSeenAtSec,
                appearance.LastSeenAtSec))
            .ToListAsync(cancellationToken);

        if (appearances.Count > 0)
        {
            return appearances
                .DistinctBy(static item => (item.Kind, item.HostId))
                .ToArray();
        }

        var detections = await _db.Detections
            .AsNoTracking()
            .Where(detection =>
                detection.RefId == faceId
                && detection.RefKind != null
                && detection.RefKind.ToLower() == "face")
            .GroupBy(detection => new { detection.HostType, detection.HostId })
            .Select(group => new FaceHostRef(
                group.Key.HostType == DetectionHostType.Scene ? FaceHostKind.Scene : FaceHostKind.Image,
                group.Key.HostId,
                group.Min(item => item.ObservedAtSec),
                group.Max(item => item.ObservedAtSec)))
            .ToListAsync(cancellationToken);

        return detections;
    }

    private async Task AddAssignmentsAsync(int faceId, int performerId, CancellationToken cancellationToken)
    {
        var hosts = await LoadFaceHostsAsync(faceId, cancellationToken);
        foreach (var host in hosts)
        {
            var added = host.Kind switch
            {
                FaceHostKind.Scene => await AddScenePerformerAsync(host.HostId, performerId, cancellationToken),
                FaceHostKind.Image => await AddImagePerformerAsync(host.HostId, performerId, cancellationToken),
                _ => false,
            };

            if (added || await HasOwnedHostAssignmentAsync(performerId, host, cancellationToken))
            {
                await UpsertAssignmentAsync(faceId, performerId, host, cancellationToken);
            }
        }
    }

    private async Task RemoveAssignmentsAsync(int faceId, int performerId, CancellationToken cancellationToken)
    {
        var assignments = await _db.ExtensionData
            .Where(item => item.ExtensionId == ExtensionId && item.Key.StartsWith(AssignmentKeyPrefix))
            .ToListAsync(cancellationToken);

        var parsedAssignments = assignments
            .Select(item => (Row: item, Assignment: TryParseAssignment(item.Key)))
            .Where(item => item.Assignment is not null)
            .Select(item => (item.Row, Assignment: item.Assignment!.Value))
            .ToArray();

        var ownedAssignments = parsedAssignments
            .Where(item => item.Assignment.FaceId == faceId && item.Assignment.PerformerId == performerId)
            .ToArray();

        foreach (var owned in ownedAssignments)
        {
            var hasOtherAssignment = parsedAssignments.Any(item =>
                item.Row != owned.Row
                && item.Assignment.FaceId != faceId
                && item.Assignment.PerformerId == performerId
                && item.Assignment.Kind == owned.Assignment.Kind
                && item.Assignment.HostId == owned.Assignment.HostId);

            if (!hasOtherAssignment)
            {
                await RemoveHostPerformerAsync(owned.Assignment.Kind, owned.Assignment.HostId, performerId, cancellationToken);
            }

            _db.ExtensionData.Remove(owned.Row);
        }
    }

    private async Task<bool> AddScenePerformerAsync(int sceneId, int performerId, CancellationToken cancellationToken)
    {
        var exists = await _db.Set<ScenePerformer>()
            .AnyAsync(item => item.SceneId == sceneId && item.PerformerId == performerId, cancellationToken);
        if (exists)
        {
            return false;
        }

        _db.Set<ScenePerformer>().Add(new ScenePerformer { SceneId = sceneId, PerformerId = performerId });
        return true;
    }

    private async Task<bool> AddImagePerformerAsync(int imageId, int performerId, CancellationToken cancellationToken)
    {
        var exists = await _db.Set<ImagePerformer>()
            .AnyAsync(item => item.ImageId == imageId && item.PerformerId == performerId, cancellationToken);
        if (exists)
        {
            return false;
        }

        _db.Set<ImagePerformer>().Add(new ImagePerformer { ImageId = imageId, PerformerId = performerId });
        return true;
    }

    private async Task RemoveHostPerformerAsync(FaceHostKind kind, int hostId, int performerId, CancellationToken cancellationToken)
    {
        if (kind == FaceHostKind.Scene)
        {
            var link = await _db.Set<ScenePerformer>()
                .FirstOrDefaultAsync(item => item.SceneId == hostId && item.PerformerId == performerId, cancellationToken);
            if (link is not null)
            {
                _db.Set<ScenePerformer>().Remove(link);
            }
            return;
        }

        var imageLink = await _db.Set<ImagePerformer>()
            .FirstOrDefaultAsync(item => item.ImageId == hostId && item.PerformerId == performerId, cancellationToken);
        if (imageLink is not null)
        {
            _db.Set<ImagePerformer>().Remove(imageLink);
        }
    }

    private async Task UpsertAssignmentAsync(int faceId, int performerId, FaceHostRef host, CancellationToken cancellationToken)
    {
        var key = BuildAssignmentKey(faceId, performerId, host.Kind, host.HostId);
        var value = JsonSerializer.Serialize(new
        {
            faceId,
            performerId,
            hostType = FormatHostKind(host.Kind),
            hostId = host.HostId,
            assignedAt = DateTime.UtcNow,
        });

        var existing = await _db.ExtensionData.FindAsync([ExtensionId, key], cancellationToken);
        if (existing is null)
        {
            _db.ExtensionData.Add(new ExtensionData
            {
                ExtensionId = ExtensionId,
                Key = key,
                Value = value,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task<bool> HasOwnedHostAssignmentAsync(int performerId, FaceHostRef host, CancellationToken cancellationToken)
    {
        var hostKind = FormatHostKind(host.Kind);
        var suffix = string.Create(CultureInfo.InvariantCulture, $":{performerId}:{hostKind}:{host.HostId}");
        return await _db.ExtensionData.AnyAsync(
            item => item.ExtensionId == ExtensionId
                    && item.Key.StartsWith(AssignmentKeyPrefix)
                    && item.Key.EndsWith(suffix),
            cancellationToken);
    }

    private static string BuildAssignmentKey(int faceId, int performerId, FaceHostKind kind, int hostId)
        => string.Create(CultureInfo.InvariantCulture, $"{AssignmentKeyPrefix}{faceId}:{performerId}:{FormatHostKind(kind)}:{hostId}");

    private static FaceAssignment? TryParseAssignment(string key)
    {
        if (!key.StartsWith(AssignmentKeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = key[AssignmentKeyPrefix.Length..].Split(':');
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var faceId)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var performerId)
            || !TryParseHostKind(parts[2], out var kind)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostId))
        {
            return null;
        }

        return new FaceAssignment(faceId, performerId, kind, hostId);
    }

    private static string FormatHostKind(FaceHostKind kind) => kind == FaceHostKind.Scene ? "scene" : "image";

    private static bool TryParseHostKind(string value, out FaceHostKind kind)
    {
        if (string.Equals(value, "scene", StringComparison.OrdinalIgnoreCase))
        {
            kind = FaceHostKind.Scene;
            return true;
        }

        if (string.Equals(value, "image", StringComparison.OrdinalIgnoreCase))
        {
            kind = FaceHostKind.Image;
            return true;
        }

        kind = default;
        return false;
    }

    private readonly record struct FaceAssignment(int FaceId, int PerformerId, FaceHostKind Kind, int HostId);
}

public readonly record struct FaceHostRef(FaceHostKind Kind, int HostId, double? FirstSeenAtSec = null, double? LastSeenAtSec = null);

public enum FaceHostKind
{
    Scene,
    Image,
}