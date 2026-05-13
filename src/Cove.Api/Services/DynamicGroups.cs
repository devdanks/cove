using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public interface IDynamicGroupSource
{
    string Key { get; }
    string DisplayName { get; }
    Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct);
    Task<JsonNode> GetEditorSchemaAsync(CancellationToken ct = default);
}

public sealed record DynamicGroupResolveContext(int UserId, int Offset = 0, int Limit = 50, bool ForceRefresh = false);

public sealed record DynamicGroupResolveResult(IReadOnlyList<DynamicGroupResolvedItem> Items, int TotalCount);

public sealed record DynamicGroupResolvedItem(
    string HostType,
    int HostId,
    GroupItemKind Kind,
    string? Title,
    double SortKey,
    string? CoverPath = null,
    double? StartSec = null,
    double? EndSec = null,
    int? SceneId = null,
    int? ImageId = null,
    int? ChildGroupId = null);

public sealed class DynamicGroupResolver(CoveContext db, IEnumerable<IDynamicGroupSource> sources, ICurrentPrincipalAccessor principalAccessor)
{
    public const string FilterSourceKey = "filter";
    public const string SaveForLaterSourceKey = "save-for-later";
    public const string WatchHistorySourceKey = "watch-history";
    public const string ContinueWatchingSourceKey = "continue-watching";

    private static readonly (string Name, string SourceKey)[] BuiltInGroups =
    [
        ("Save for Later", SaveForLaterSourceKey),
        ("Watch History", WatchHistorySourceKey),
        ("Continue Watching", ContinueWatchingSourceKey),
    ];

    private readonly Dictionary<string, IDynamicGroupSource> _sources = sources.ToDictionary(source => source.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DynamicGroupSourceDto> GetSources()
        => _sources.Values
            .OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(source => new DynamicGroupSourceDto(source.Key, source.DisplayName))
            .ToList();

    public async Task EnsureBuiltInGroupsAsync(CancellationToken ct)
    {
        foreach (var (name, sourceKey) in BuiltInGroups)
        {
            var existing = await db.Groups.FirstOrDefaultAsync(group => group.QuerySourceKey == sourceKey && group.Kind == GroupKind.Dynamic, ct);
            if (existing is null)
            {
                db.Groups.Add(new Group
                {
                    Name = name,
                    Kind = GroupKind.Dynamic,
                    QuerySourceKey = sourceKey,
                    CacheTtlSec = 30,
                    AllowedHostTypes = sourceKey == ContinueWatchingSourceKey
                        ? ["scene"]
                        : ["scene", "audio", "text", "image", "performer", "studio", "tag", "gallery", "group", "face", "segment"],
                });
                continue;
            }

            existing.Name = name;
            existing.QuerySourceKey = sourceKey;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GroupItemDto>> ResolveDtosAsync(int groupId, bool forceRefresh, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(item => item.Id == groupId, ct);
        if (group is null)
            return [];

        if (group.Kind == GroupKind.Static)
        {
            var items = await db.GroupItems.AsNoTracking()
                .Include(item => item.Scene).ThenInclude(scene => scene!.Files)
                .Include(item => item.Image)
                .Include(item => item.ChildGroup)
                .Where(item => item.GroupId == groupId)
                .OrderBy(item => item.OrderIndex)
                .ThenBy(item => item.Id)
                .ToListAsync(ct);
            return items.Select(ToDto).ToList();
        }

        var resolved = await ResolveAsync(group, forceRefresh, ct);
        return resolved.Select((item, index) => ToDto(group.Id, item, index)).ToList();
    }

    public async Task<PaginatedResponse<GroupItemDto>> ResolvePageDtosAsync(int groupId, FindFilter? filter, bool forceRefresh, CancellationToken ct)
    {
        var page = Math.Max(1, filter?.Page ?? 1);
        var perPage = Math.Clamp(filter?.PerPage ?? 40, 1, 250);
        var offset = (page - 1) * perPage;
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(item => item.Id == groupId, ct);
        if (group is null)
            return new PaginatedResponse<GroupItemDto>([], 0, page, perPage);

        if (group.Kind == GroupKind.Static)
        {
            var query = db.GroupItems.AsNoTracking()
                .Include(item => item.Scene).ThenInclude(scene => scene!.Files)
                .Include(item => item.Image)
                .Include(item => item.ChildGroup)
                .Where(item => item.GroupId == groupId)
                .OrderBy(item => item.OrderIndex)
                .ThenBy(item => item.Id);
            var totalCount = await query.CountAsync(ct);
            var items = await query.Skip(offset).Take(perPage).ToListAsync(ct);
            return new PaginatedResponse<GroupItemDto>(items.Select(ToDto).ToList(), totalCount, page, perPage);
        }

        var resolved = await ResolvePageAsync(group, offset, perPage, forceRefresh, ct);
        return new PaginatedResponse<GroupItemDto>(resolved.Items.Select((item, index) => ToDto(group.Id, item, offset + index)).ToList(), resolved.TotalCount, page, perPage);
    }

    public async Task<IReadOnlyList<DynamicGroupResolvedItem>> ResolveAsync(int groupId, bool forceRefresh, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(item => item.Id == groupId, ct);
        if (group is null)
            return [];

        return group.Kind == GroupKind.Static
            ? await ResolveStaticAsync(group.Id, ct)
            : await ResolveAsync(group, forceRefresh, ct);
    }

    public async Task SnapshotAsync(int groupId, CancellationToken ct)
    {
        var group = await db.Groups.Include(item => item.GroupItems).FirstOrDefaultAsync(item => item.Id == groupId, ct);
        if (group is null || group.Kind == GroupKind.Static)
            return;

        var resolved = await ResolveAsync(group, forceRefresh: true, ct);
        db.GroupItems.RemoveRange(group.GroupItems);
        var now = DateTime.UtcNow;
        var order = 0;
        foreach (var item in resolved)
        {
            db.GroupItems.Add(new GroupItem
            {
                GroupId = group.Id,
                OrderIndex = order++,
                Kind = item.Kind,
                HostType = item.HostType,
                HostId = item.HostId,
                SceneId = item.SceneId,
                ImageId = item.ImageId,
                ChildGroupId = item.ChildGroupId,
                StartSec = item.StartSec,
                EndSec = item.EndSec,
                Title = item.Title,
                SnapshotAt = now,
            });
        }

        group.Kind = GroupKind.Static;
        group.QuerySourceKey = null;
        group.QueryJson = null;
        group.LastResolvedAt = null;
        group.CachedItemCount = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<DynamicGroupResolvedItem>> ResolveAsync(Group group, bool forceRefresh, CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is not int userId)
            return [];
        if (string.IsNullOrWhiteSpace(group.QuerySourceKey) || !_sources.TryGetValue(group.QuerySourceKey, out var source))
            return [];

        var result = await ResolveAllAsync(source, group, userId, forceRefresh, ct);
        var now = DateTime.UtcNow;
        var trackedGroup = await db.Groups.FirstOrDefaultAsync(item => item.Id == group.Id, ct);
        if (trackedGroup is not null)
        {
            trackedGroup.LastResolvedAt = now;
            trackedGroup.CachedItemCount = result.TotalCount;
            await db.SaveChangesAsync(ct);
        }
        return result.Items;
    }

    private static async Task<DynamicGroupResolveResult> ResolveAllAsync(IDynamicGroupSource source, Group group, int userId, bool forceRefresh, CancellationToken ct)
    {
        const int pageSize = 250;
        var offset = 0;
        var totalCount = 0;
        var items = new List<DynamicGroupResolvedItem>();

        while (true)
        {
            var page = await source.ResolveAsync(group, new DynamicGroupResolveContext(userId, offset, pageSize, forceRefresh), ct);
            totalCount = page.TotalCount;
            if (page.Items.Count == 0)
                break;

            items.AddRange(page.Items);
            offset += page.Items.Count;
            if (items.Count >= page.TotalCount)
                break;
        }

        return new DynamicGroupResolveResult(items, totalCount);
    }

    private async Task<DynamicGroupResolveResult> ResolvePageAsync(Group group, int offset, int limit, bool forceRefresh, CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is not int userId)
            return new DynamicGroupResolveResult([], 0);
        if (string.IsNullOrWhiteSpace(group.QuerySourceKey) || !_sources.TryGetValue(group.QuerySourceKey, out var source))
            return new DynamicGroupResolveResult([], 0);

        var result = await source.ResolveAsync(group, new DynamicGroupResolveContext(userId, offset, limit, forceRefresh), ct);
        var trackedGroup = await db.Groups.FirstOrDefaultAsync(item => item.Id == group.Id, ct);
        if (trackedGroup is not null)
        {
            trackedGroup.LastResolvedAt = DateTime.UtcNow;
            trackedGroup.CachedItemCount = result.TotalCount;
            await db.SaveChangesAsync(ct);
        }
        return result;
    }

    private async Task<IReadOnlyList<DynamicGroupResolvedItem>> ResolveStaticAsync(int groupId, CancellationToken ct)
    {
        var items = await db.GroupItems.AsNoTracking()
            .Include(item => item.Scene).ThenInclude(scene => scene!.Files)
            .Include(item => item.Image)
            .Include(item => item.ChildGroup)
            .Where(item => item.GroupId == groupId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        return items.Select(item => new DynamicGroupResolvedItem(
            item.HostType,
            item.HostId,
            item.Kind,
            item.Title ?? SceneTitle(item.Scene) ?? item.Image?.Title ?? item.ChildGroup?.Name,
            item.OrderIndex,
            SceneId: item.SceneId,
            ImageId: item.ImageId,
            ChildGroupId: item.ChildGroupId,
            StartSec: item.StartSec,
            EndSec: item.EndSec)).ToList();
    }

    private static string? SceneTitle(Scene? scene)
        => !string.IsNullOrWhiteSpace(scene?.Title)
            ? scene.Title
            : scene?.Files.OrderBy(file => file.Id).FirstOrDefault()?.Basename;

    private static GroupItemDto ToDto(GroupItem item) => new(
        item.Id,
        item.GroupId,
        item.OrderIndex,
        item.Kind,
        item.SceneId,
        SceneTitle(item.Scene),
        item.HostType,
        item.HostId,
        item.ImageId,
        item.Image?.Title,
        item.ChildGroupId,
        item.ChildGroup?.Name,
        item.StartSec,
        item.EndSec,
        item.Title,
        item.Notes,
        item.SourceSpanKey,
        item.SourceProfileId,
        item.SourceQueryJson,
        item.SnapshotAt?.ToString("o"),
        item.CreatedAt.ToString("o"),
        item.UpdatedAt.ToString("o"));

    private static GroupItemDto ToDto(int groupId, DynamicGroupResolvedItem item, int index) => new(
        -(index + 1),
        groupId,
        index,
        item.Kind,
        item.SceneId,
        item.Kind is GroupItemKind.Scene or GroupItemKind.SceneRange ? item.Title : null,
        item.HostType,
        item.HostId,
        item.ImageId,
        item.Kind == GroupItemKind.Image ? item.Title : null,
        item.ChildGroupId,
        item.Kind == GroupItemKind.Group ? item.Title : null,
        item.StartSec,
        item.EndSec,
        item.Title,
        null,
        null,
        null,
        null,
        null,
        DateTime.UtcNow.ToString("o"),
        DateTime.UtcNow.ToString("o"));
}

public abstract class UserScopedDynamicGroupSource(CoveContext db) : IDynamicGroupSource
{
    protected CoveContext Db { get; } = db;
    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    public abstract Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct);

    public Task<JsonNode> GetEditorSchemaAsync(CancellationToken ct = default)
        => Task.FromResult<JsonNode>(new JsonObject { ["type"] = "builtin", ["key"] = Key });

    protected static GroupItemKind ToKind(AffinityHostType hostType) => hostType switch
    {
        AffinityHostType.Scene => GroupItemKind.Scene,
        AffinityHostType.Audio => GroupItemKind.Audio,
        AffinityHostType.Text => GroupItemKind.Text,
        AffinityHostType.Image => GroupItemKind.Image,
        AffinityHostType.Performer => GroupItemKind.Performer,
        AffinityHostType.Face => GroupItemKind.Face,
        AffinityHostType.Tag => GroupItemKind.Tag,
        AffinityHostType.Studio => GroupItemKind.Studio,
        AffinityHostType.Gallery => GroupItemKind.Gallery,
        AffinityHostType.Group => GroupItemKind.Group,
        _ => GroupItemKind.Scene,
    };

    protected static string ToHostName(AffinityHostType hostType)
        => hostType.ToString().ToLowerInvariant();

    protected async Task<IReadOnlyList<DynamicGroupResolvedItem>> HydrateAsync(
        IReadOnlyList<(AffinityHostType HostType, int HostId, double SortKey)> rows,
        CancellationToken ct)
    {
        var sceneIds = rows.Where(row => row.HostType == AffinityHostType.Scene).Select(row => row.HostId).Distinct().ToArray();
        var audioIds = rows.Where(row => row.HostType == AffinityHostType.Audio).Select(row => row.HostId).Distinct().ToArray();
        var textIds = rows.Where(row => row.HostType == AffinityHostType.Text).Select(row => row.HostId).Distinct().ToArray();
        var imageIds = rows.Where(row => row.HostType == AffinityHostType.Image).Select(row => row.HostId).Distinct().ToArray();
        var performerIds = rows.Where(row => row.HostType == AffinityHostType.Performer).Select(row => row.HostId).Distinct().ToArray();
        var faceIds = rows.Where(row => row.HostType == AffinityHostType.Face).Select(row => row.HostId).Distinct().ToArray();
        var tagIds = rows.Where(row => row.HostType == AffinityHostType.Tag).Select(row => row.HostId).Distinct().ToArray();
        var studioIds = rows.Where(row => row.HostType == AffinityHostType.Studio).Select(row => row.HostId).Distinct().ToArray();
        var galleryIds = rows.Where(row => row.HostType == AffinityHostType.Gallery).Select(row => row.HostId).Distinct().ToArray();
        var groupIds = rows.Where(row => row.HostType == AffinityHostType.Group).Select(row => row.HostId).Distinct().ToArray();

        var scenes = await Db.Scenes.AsNoTracking().Where(item => sceneIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Title, ct);
        var sceneFileRows = await Db.VideoFiles.AsNoTracking()
            .Where(file => file.SceneId != null && sceneIds.Contains(file.SceneId.Value))
            .OrderBy(file => file.Id)
            .Select(file => new { SceneId = file.SceneId!.Value, file.Basename })
            .ToListAsync(ct);
        var sceneFileTitles = sceneFileRows
            .GroupBy(file => file.SceneId)
            .ToDictionary(group => group.Key, group => group.First().Basename);
        var audios = await Db.Audios.AsNoTracking().Where(item => audioIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => !string.IsNullOrWhiteSpace(item.Title) ? item.Title! : item.MinPath ?? $"Audio {item.Id}", ct);
        var texts = await Db.TextDocuments.AsNoTracking().Where(item => textIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => !string.IsNullOrWhiteSpace(item.Title) ? item.Title! : item.MinPath ?? $"Text {item.Id}", ct);
        var images = await Db.Images.AsNoTracking().Where(item => imageIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Title ?? $"Image {item.Id}", ct);
        var performers = await Db.Performers.AsNoTracking().Where(item => performerIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Name, ct);
        var faces = await Db.Faces.AsNoTracking().Where(item => faceIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Label ?? $"Face {item.Id}", ct);
        var tags = await Db.Tags.AsNoTracking().Where(item => tagIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Name, ct);
        var studios = await Db.Studios.AsNoTracking().Where(item => studioIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Name, ct);
        var galleries = await Db.Galleries.AsNoTracking().Where(item => galleryIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Title ?? $"Gallery {item.Id}", ct);
        var groups = await Db.Groups.AsNoTracking().Where(item => groupIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Name, ct);

        string? TitleFor(AffinityHostType hostType, int hostId) => hostType switch
        {
            AffinityHostType.Scene => !string.IsNullOrWhiteSpace(scenes.GetValueOrDefault(hostId))
                ? scenes.GetValueOrDefault(hostId)
                : sceneFileTitles.GetValueOrDefault(hostId) ?? $"Scene {hostId}",
            AffinityHostType.Audio => audios.GetValueOrDefault(hostId),
            AffinityHostType.Text => texts.GetValueOrDefault(hostId),
            AffinityHostType.Image => images.GetValueOrDefault(hostId),
            AffinityHostType.Performer => performers.GetValueOrDefault(hostId),
            AffinityHostType.Face => faces.GetValueOrDefault(hostId),
            AffinityHostType.Tag => tags.GetValueOrDefault(hostId),
            AffinityHostType.Studio => studios.GetValueOrDefault(hostId),
            AffinityHostType.Gallery => galleries.GetValueOrDefault(hostId),
            AffinityHostType.Group => groups.GetValueOrDefault(hostId),
            _ => null,
        };

        return rows
            .Select(row => new DynamicGroupResolvedItem(
                ToHostName(row.HostType),
                row.HostId,
                ToKind(row.HostType),
                TitleFor(row.HostType, row.HostId),
                row.SortKey,
                SceneId: row.HostType == AffinityHostType.Scene ? row.HostId : null,
                ImageId: row.HostType == AffinityHostType.Image ? row.HostId : null,
                ChildGroupId: row.HostType == AffinityHostType.Group ? row.HostId : null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToList();
    }
}

public sealed class FilterDynamicGroupSource(ISceneRepository sceneRepository) : IDynamicGroupSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Key => DynamicGroupResolver.FilterSourceKey;
    public string DisplayName => "Filtered Scenes";

    public async Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct)
    {
        var query = ParseQuery(group.QueryJson);
        if (!string.Equals(query.EntityType, "scene", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(query.EntityType, "scenes", StringComparison.OrdinalIgnoreCase))
        {
            return new DynamicGroupResolveResult([], 0);
        }

        var savedFindFilter = query.FindFilter ?? new FindFilter();
        var page = context.Limit > 0 ? (context.Offset / context.Limit) + 1 : 1;
        var findFilter = new FindFilter
        {
            Q = savedFindFilter.Q,
            Page = Math.Max(1, page),
            PerPage = Math.Clamp(context.Limit, 1, 250),
            Sort = string.IsNullOrWhiteSpace(savedFindFilter.Sort) ? "updated_at" : savedFindFilter.Sort,
            Direction = savedFindFilter.Direction,
            Seed = savedFindFilter.Seed,
        };

        var (scenes, totalCount) = await sceneRepository.FindAsync(query.ObjectFilter ?? new SceneFilter(), findFilter, ct);
        var items = scenes.Select((scene, index) => new DynamicGroupResolvedItem(
            "scene",
            scene.Id,
            GroupItemKind.Scene,
            SceneTitle(scene),
            context.Offset + index,
            SceneId: scene.Id)).ToList();
        return new DynamicGroupResolveResult(items, totalCount);
    }

    public Task<JsonNode> GetEditorSchemaAsync(CancellationToken ct = default)
        => Task.FromResult<JsonNode>(new JsonObject { ["type"] = "filter", ["entityType"] = "scene" });

    private static FilterDynamicGroupQuery ParseQuery(string? queryJson)
    {
        if (string.IsNullOrWhiteSpace(queryJson))
            return new FilterDynamicGroupQuery();

        try
        {
            return JsonSerializer.Deserialize<FilterDynamicGroupQuery>(queryJson, JsonOptions) ?? new FilterDynamicGroupQuery();
        }
        catch (JsonException)
        {
            return new FilterDynamicGroupQuery();
        }
    }

    private static string SceneTitle(Scene scene)
        => !string.IsNullOrWhiteSpace(scene.Title)
            ? scene.Title
            : scene.Files.OrderBy(file => file.Id).FirstOrDefault()?.Basename ?? $"Scene {scene.Id}";

    private sealed class FilterDynamicGroupQuery
    {
        public string EntityType { get; set; } = "scene";
        public FindFilter? FindFilter { get; set; }
        public SceneFilter? ObjectFilter { get; set; }
    }
}

public sealed class SaveForLaterDynamicGroupSource(CoveContext db) : UserScopedDynamicGroupSource(db)
{
    public override string Key => DynamicGroupResolver.SaveForLaterSourceKey;
    public override string DisplayName => "Save for Later";

    public override async Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct)
    {
        var query = Db.UserBookmarks.AsNoTracking()
            .Where(bookmark => bookmark.UserId == context.UserId)
            .OrderByDescending(bookmark => bookmark.CreatedAt);
        var totalCount = await query.CountAsync(ct);
        var rows = await query.Skip(context.Offset).Take(context.Limit)
            .Select(bookmark => new { bookmark.HostType, bookmark.HostId, bookmark.CreatedAt })
            .ToListAsync(ct);
        var items = await HydrateAsync(rows.Select(row => (row.HostType, row.HostId, (double)row.CreatedAt.Ticks)).ToList(), ct);
        return new DynamicGroupResolveResult(items, totalCount);
    }
}

public sealed class WatchHistoryDynamicGroupSource(CoveContext db) : UserScopedDynamicGroupSource(db)
{
    public override string Key => DynamicGroupResolver.WatchHistorySourceKey;
    public override string DisplayName => "Watch History";

    public override async Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct)
    {
        var query = Db.UserEntityAffinities.AsNoTracking()
            .Where(affinity => affinity.UserId == context.UserId && affinity.LastConsumedAt != null)
            .OrderByDescending(affinity => affinity.LastConsumedAt);
        var totalCount = await query.CountAsync(ct);
        var rows = await query.Skip(context.Offset).Take(context.Limit)
            .Select(affinity => new { affinity.HostType, affinity.HostId, affinity.LastConsumedAt })
            .ToListAsync(ct);
        var items = await HydrateAsync(rows.Select(row => (row.HostType, row.HostId, (double)row.LastConsumedAt!.Value.Ticks)).ToList(), ct);
        return new DynamicGroupResolveResult(items, totalCount);
    }
}

public sealed class ContinueWatchingDynamicGroupSource(CoveContext db) : UserScopedDynamicGroupSource(db)
{
    public override string Key => DynamicGroupResolver.ContinueWatchingSourceKey;
    public override string DisplayName => "Continue Watching";

    public override async Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct)
    {
        var query = Db.UserEntityAffinities.AsNoTracking()
            .Where(affinity => affinity.UserId == context.UserId
                && affinity.HostType == AffinityHostType.Scene
                && affinity.LastConsumedAt != null
                && affinity.LastPositionSec > 0)
            .Join(Db.Scenes.AsNoTracking(), affinity => affinity.HostId, scene => scene.Id, (affinity, scene) => new { affinity, scene })
            .Where(row => row.scene.MaxDuration <= 0 || row.affinity.TotalConsumedSec < row.scene.MaxDuration * 0.95)
            .OrderByDescending(row => row.affinity.LastConsumedAt);
        var totalCount = await query.CountAsync(ct);
        var rows = await query.Skip(context.Offset).Take(context.Limit)
            .Select(row => new { row.affinity.HostId, row.affinity.LastConsumedAt })
            .ToListAsync(ct);
        var items = await HydrateAsync(rows.Select(row => (AffinityHostType.Scene, row.HostId, (double)row.LastConsumedAt!.Value.Ticks)).ToList(), ct);
        return new DynamicGroupResolveResult(items, totalCount);
    }
}
