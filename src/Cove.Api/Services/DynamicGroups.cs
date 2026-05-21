using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
using Cove.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace Cove.Api.Services;

public interface IDynamicGroupSource
{
    string Key { get; }
    string DisplayName { get; }
    Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct);
    Task<JsonNode> GetEditorSchemaAsync(CancellationToken ct = default);
}

public interface IDynamicGroupCountingSource
{
    Task<IReadOnlyDictionary<GroupItemKind, int>> CountByKindAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct);
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

    public async Task<IReadOnlyDictionary<GroupItemKind, int>> CountByKindAsync(Group group, bool forceRefresh, CancellationToken ct)
    {
        if (group.Kind == GroupKind.Static)
        {
            return await db.GroupItems.AsNoTracking()
                .Where(item => item.GroupId == group.Id)
                .GroupBy(item => item.Kind)
                .Select(grouping => new { Kind = grouping.Key, Count = grouping.Count() })
                .ToDictionaryAsync(row => row.Kind, row => row.Count, ct);
        }

        if (string.IsNullOrWhiteSpace(group.QuerySourceKey) || !_sources.TryGetValue(group.QuerySourceKey, out var source))
            return new Dictionary<GroupItemKind, int>();

        var userId = principalAccessor.Current?.UserId ?? 0;

        if (source is IDynamicGroupCountingSource countingSource)
            return await countingSource.CountByKindAsync(group, new DynamicGroupResolveContext(userId, ForceRefresh: forceRefresh), ct);

        if (userId <= 0)
            return new Dictionary<GroupItemKind, int>();

        var resolved = await ResolveAllAsync(source, group, userId, forceRefresh, ct);
        return resolved.Items
            .GroupBy(item => item.Kind)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Count());
    }

    public async Task<PaginatedResponse<GroupItemDto>> ResolvePageDtosAsync(int groupId, FindFilter? filter, bool forceRefresh, CancellationToken ct)
    {
        var page = Math.Max(1, filter?.Page ?? 1);
        var requestedPerPage = filter?.PerPage ?? 40;
        var infinitePageSize = requestedPerPage <= 0;
        var perPage = infinitePageSize ? 0 : Math.Clamp(requestedPerPage, 1, 1000);
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
            var itemsQuery = infinitePageSize ? query : query.Skip(offset).Take(perPage);
            var items = await itemsQuery.ToListAsync(ct);
            return new PaginatedResponse<GroupItemDto>(items.Select(ToDto).ToList(), totalCount, page, perPage);
        }

        var resolved = infinitePageSize
            ? await ResolveAllPageAsync(group, forceRefresh, ct)
            : await ResolvePageAsync(group, offset, perPage, forceRefresh, ct);
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

    private async Task<DynamicGroupResolveResult> ResolveAllPageAsync(Group group, bool forceRefresh, CancellationToken ct)
    {
        if (principalAccessor.Current?.UserId is not int userId)
            return new DynamicGroupResolveResult([], 0);
        if (string.IsNullOrWhiteSpace(group.QuerySourceKey) || !_sources.TryGetValue(group.QuerySourceKey, out var source))
            return new DynamicGroupResolveResult([], 0);

        var result = await ResolveAllAsync(source, group, userId, forceRefresh, ct);
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

public sealed class FilterDynamicGroupSource(CoveContext db, ISceneRepository sceneRepository, IImageRepository imageRepository) : IDynamicGroupSource, IDynamicGroupCountingSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new CriterionModifierJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Key => DynamicGroupResolver.FilterSourceKey;
    public string DisplayName => "Filtered Entities";

    public async Task<DynamicGroupResolveResult> ResolveAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct)
    {
        var query = ParseQuery(group.QueryJson);
        var entityConfigs = GetEntityConfigs(query, group);
        if (entityConfigs.Count == 0)
            return new DynamicGroupResolveResult([], 0);

        var items = new List<DynamicGroupResolvedItem>();
        var totalCount = 0;
        var remainingOffset = Math.Max(0, context.Offset);
        var remainingLimit = Math.Max(0, context.Limit);

        foreach (var entityConfig in entityConfigs)
        {
            var pageOffset = remainingLimit > 0 ? remainingOffset : 0;
            var pageLimit = remainingLimit > 0 ? remainingLimit : 0;
            var page = await ResolveEntityAsync(entityConfig, pageOffset, pageLimit, ct);
            totalCount += page.TotalCount;

            if (remainingLimit <= 0)
                continue;

            if (remainingOffset >= page.TotalCount)
            {
                remainingOffset -= page.TotalCount;
                continue;
            }

            remainingOffset = 0;
            foreach (var item in page.Items)
            {
                if (items.Count >= context.Limit)
                    break;

                items.Add(item with { SortKey = context.Offset + items.Count });
            }

            remainingLimit = Math.Max(0, context.Limit - items.Count);
        }

        return new DynamicGroupResolveResult(items, totalCount);
    }

    public Task<JsonNode> GetEditorSchemaAsync(CancellationToken ct = default)
        => Task.FromResult<JsonNode>(new JsonObject { ["type"] = "filter", ["entityTypes"] = new JsonArray("scene", "image", "audio", "text", "segment") });

    public async Task<IReadOnlyDictionary<GroupItemKind, int>> CountByKindAsync(Group group, DynamicGroupResolveContext context, CancellationToken ct)
    {
        var query = ParseQuery(group.QueryJson);
        var entityConfigs = GetEntityConfigs(query, group);
        var result = new Dictionary<GroupItemKind, int>();
        foreach (var entityConfig in entityConfigs)
        {
            var count = await CountEntityAsync(entityConfig, ct);
            if (count <= 0)
                continue;

            var kind = KindForEntityType(entityConfig.EntityType);
            result[kind] = result.GetValueOrDefault(kind) + count;
        }

        return result;
    }

    private Task<DynamicGroupResolveResult> ResolveEntityAsync(FilterEntityConfig entityConfig, int localOffset, int localLimit, CancellationToken ct)
    {
        var findFilter = BuildFindFilter(entityConfig.FindFilter, localOffset, localLimit);
        return entityConfig.EntityType switch
        {
            "image" => ResolveImagesAsync(entityConfig, findFilter, localOffset, localLimit, ct),
            "audio" => ResolveAudiosAsync(entityConfig, findFilter, localOffset, localLimit, ct),
            "text" => ResolveTextsAsync(entityConfig, findFilter, localOffset, localLimit, ct),
            "segment" => ResolveSegmentsAsync(findFilter, localOffset, localLimit, ct),
            "scene" => ResolveScenesAsync(entityConfig, findFilter, localOffset, localLimit, ct),
            _ => Task.FromResult(new DynamicGroupResolveResult([], 0)),
        };
    }

    private async Task<int> CountEntityAsync(FilterEntityConfig entityConfig, CancellationToken ct)
    {
        var findFilter = BuildFindFilter(entityConfig.FindFilter, 0, 1);
        return entityConfig.EntityType switch
        {
            "scene" => (await sceneRepository.FindAsync(DeserializeFilter<SceneFilter>(entityConfig.ObjectFilter) ?? new SceneFilter(), findFilter, ct)).TotalCount,
            "image" => (await imageRepository.FindAsync(DeserializeFilter<ImageFilter>(entityConfig.ObjectFilter) ?? new ImageFilter(), findFilter, ct)).TotalCount,
            "audio" => await ApplyAudioFilter(ApplyAudioSearch(db.Audios.AsNoTracking(), findFilter.Q), DeserializeFilter<AudioFilter>(entityConfig.ObjectFilter)).CountAsync(ct),
            "text" => await ApplyTextFilter(ApplyTextSearch(db.TextDocuments.AsNoTracking(), findFilter.Q), DeserializeFilter<TextDocumentFilter>(entityConfig.ObjectFilter)).CountAsync(ct),
            "segment" => await ApplySegmentSearch(db.Segments.AsNoTracking().Include(segment => segment.Tag), findFilter.Q).CountAsync(ct),
            _ => 0,
        };
    }

    private async Task<DynamicGroupResolveResult> ResolveScenesAsync(FilterEntityConfig entityConfig, FindFilter findFilter, int localOffset, int localLimit, CancellationToken ct)
    {
        var (scenes, totalCount) = await sceneRepository.FindAsync(DeserializeFilter<SceneFilter>(entityConfig.ObjectFilter) ?? new SceneFilter(), findFilter, ct);
        if (localLimit <= 0 || localOffset >= totalCount)
            return new DynamicGroupResolveResult([], totalCount);

        var items = scenes.Skip(localOffset).Take(localLimit).Select((scene, index) => new DynamicGroupResolvedItem(
            "scene",
            scene.Id,
            GroupItemKind.Scene,
            SceneTitle(scene),
            localOffset + index,
            SceneId: scene.Id)).ToList();
        return new DynamicGroupResolveResult(items, totalCount);
    }

    private async Task<DynamicGroupResolveResult> ResolveImagesAsync(FilterEntityConfig entityConfig, FindFilter findFilter, int localOffset, int localLimit, CancellationToken ct)
    {
        var (images, totalCount) = await imageRepository.FindAsync(DeserializeFilter<ImageFilter>(entityConfig.ObjectFilter) ?? new ImageFilter(), findFilter, ct);
        if (localLimit <= 0 || localOffset >= totalCount)
            return new DynamicGroupResolveResult([], totalCount);

        var items = images.Skip(localOffset).Take(localLimit).Select((image, index) => new DynamicGroupResolvedItem(
            "image",
            image.Id,
            GroupItemKind.Image,
            image.Title ?? $"Image {image.Id}",
            localOffset + index,
            ImageId: image.Id)).ToList();
        return new DynamicGroupResolveResult(items, totalCount);
    }

    private async Task<DynamicGroupResolveResult> ResolveAudiosAsync(FilterEntityConfig entityConfig, FindFilter findFilter, int localOffset, int localLimit, CancellationToken ct)
    {
        var query = db.Audios.AsNoTracking().AsQueryable();
        query = ApplyAudioSearch(query, findFilter.Q);
        query = ApplyAudioFilter(query, DeserializeFilter<AudioFilter>(entityConfig.ObjectFilter));
        query = ApplyAudioSort(query, findFilter.Sort, findFilter.Direction == SortDirection.Desc);

        var totalCount = await query.CountAsync(ct);
        if (localLimit <= 0 || localOffset >= totalCount)
            return new DynamicGroupResolveResult([], totalCount);

        var items = await query.Skip(localOffset).Take(localLimit).ToListAsync(ct);
        return new DynamicGroupResolveResult(items.Select((audio, index) => new DynamicGroupResolvedItem(
            "audio",
            audio.Id,
            GroupItemKind.Audio,
            !string.IsNullOrWhiteSpace(audio.Title) ? audio.Title : audio.MinPath ?? $"Audio {audio.Id}",
            localOffset + index)).ToList(), totalCount);
    }

    private async Task<DynamicGroupResolveResult> ResolveTextsAsync(FilterEntityConfig entityConfig, FindFilter findFilter, int localOffset, int localLimit, CancellationToken ct)
    {
        var query = db.TextDocuments.AsNoTracking().AsQueryable();
        query = ApplyTextSearch(query, findFilter.Q);
        query = ApplyTextFilter(query, DeserializeFilter<TextDocumentFilter>(entityConfig.ObjectFilter));
        query = ApplyTextSort(query, findFilter.Sort, findFilter.Direction == SortDirection.Desc);

        var totalCount = await query.CountAsync(ct);
        if (localLimit <= 0 || localOffset >= totalCount)
            return new DynamicGroupResolveResult([], totalCount);

        var items = await query.Skip(localOffset).Take(localLimit).ToListAsync(ct);
        return new DynamicGroupResolveResult(items.Select((text, index) => new DynamicGroupResolvedItem(
            "text",
            text.Id,
            GroupItemKind.Text,
            !string.IsNullOrWhiteSpace(text.Title) ? text.Title : text.MinPath ?? $"Text {text.Id}",
            localOffset + index)).ToList(), totalCount);
    }

    private async Task<DynamicGroupResolveResult> ResolveSegmentsAsync(FindFilter findFilter, int localOffset, int localLimit, CancellationToken ct)
    {
        var query = db.Segments.AsNoTracking().Include(segment => segment.Tag).AsQueryable();
        query = ApplySegmentSearch(query, findFilter.Q);

        var desc = findFilter.Direction == SortDirection.Desc;
        query = (findFilter.Sort ?? "created_at") switch
        {
            "title" => desc
                ? query.OrderByDescending(segment => segment.Title ?? (segment.Tag != null ? segment.Tag.Name : null) ?? segment.Kind).ThenByDescending(segment => segment.Id)
                : query.OrderBy(segment => segment.Title ?? (segment.Tag != null ? segment.Tag.Name : null) ?? segment.Kind).ThenBy(segment => segment.Id),
            "start" => desc
                ? query.OrderByDescending(segment => segment.StartSec).ThenByDescending(segment => segment.Id)
                : query.OrderBy(segment => segment.StartSec).ThenBy(segment => segment.Id),
            "updated_at" => desc
                ? query.OrderByDescending(segment => segment.UpdatedAt).ThenByDescending(segment => segment.Id)
                : query.OrderBy(segment => segment.UpdatedAt).ThenBy(segment => segment.Id),
            _ => desc
                ? query.OrderByDescending(segment => segment.CreatedAt).ThenByDescending(segment => segment.Id)
                : query.OrderBy(segment => segment.CreatedAt).ThenBy(segment => segment.Id),
        };

        var totalCount = await query.CountAsync(ct);
        if (localLimit <= 0 || localOffset >= totalCount)
            return new DynamicGroupResolveResult([], totalCount);

        var items = await query.Skip(localOffset).Take(localLimit).ToListAsync(ct);
        return new DynamicGroupResolveResult(items.Select((segment, index) => new DynamicGroupResolvedItem(
            "segment",
            segment.Id,
            GroupItemKind.Segment,
            segment.Title ?? segment.Tag?.Name ?? segment.Kind ?? $"Segment {segment.Id}",
            localOffset + index,
            StartSec: segment.StartSec,
            EndSec: segment.EndSec,
            SceneId: segment.HostType == SegmentHostType.Scene ? segment.HostId : null,
            ImageId: segment.HostType == SegmentHostType.Image ? segment.HostId : null)).ToList(), totalCount);
    }

    private static FindFilter BuildFindFilter(FindFilter? savedFindFilter, int localOffset, int localLimit)
    {
        savedFindFilter ??= new FindFilter();
        var windowSize = Math.Max(1, localOffset + Math.Max(1, localLimit));
        return new FindFilter
        {
            Q = savedFindFilter.Q,
            Page = 1,
            PerPage = Math.Clamp(windowSize, 1, 10000),
            Sort = string.IsNullOrWhiteSpace(savedFindFilter.Sort) ? "updated_at" : savedFindFilter.Sort,
            Direction = savedFindFilter.Direction,
            Seed = savedFindFilter.Seed,
        };
    }

    private static IReadOnlyList<FilterEntityConfig> GetEntityConfigs(FilterDynamicGroupQuery query, Group group)
    {
        var rawEntityTypes = query.EntityTypes?.Count > 0
            ? query.EntityTypes
            : [query.EntityType ?? "scene"];
        var allowedHostTypes = group.AllowedHostTypes.Count > 0
            ? group.AllowedHostTypes
            : ["scene", "image", "audio", "text", "segment"];

        return rawEntityTypes
            .Select(NormalizeEntityType)
            .Where(entityType => IsSupportedEntityType(entityType))
            .Where(entityType => allowedHostTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(entityType => new FilterEntityConfig(entityType, GetFindFilter(query, entityType), GetObjectFilter(query, entityType)))
            .ToList();
    }

    private static FindFilter? GetFindFilter(FilterDynamicGroupQuery query, string entityType)
    {
        if (query.FindFilters != null)
        {
            if (query.FindFilters.TryGetValue(entityType, out var findFilter))
                return findFilter;
            if (query.FindFilters.TryGetValue($"{entityType}s", out findFilter))
                return findFilter;
        }

        return query.FindFilter;
    }

    private static JsonElement? GetObjectFilter(FilterDynamicGroupQuery query, string entityType)
    {
        if (query.ObjectFilters != null)
        {
            if (query.ObjectFilters.TryGetValue(entityType, out var objectFilter))
                return objectFilter;
            if (query.ObjectFilters.TryGetValue($"{entityType}s", out objectFilter))
                return objectFilter;
        }

        return string.Equals(NormalizeEntityType(query.EntityType), entityType, StringComparison.OrdinalIgnoreCase)
            ? query.ObjectFilter
            : null;
    }

    private static bool IsSupportedEntityType(string entityType)
        => entityType is "scene" or "image" or "audio" or "text" or "segment";

    private static TFilter? DeserializeFilter<TFilter>(JsonElement? objectFilter)
    {
        if (!objectFilter.HasValue || objectFilter.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;

        try
        {
            return objectFilter.Value.Deserialize<TFilter>(JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed class CriterionModifierJsonConverter : JsonConverter<CriterionModifier>
    {
        public override CriterionModifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && TryParse(reader.GetString(), out var modifier))
                return modifier;

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric) && Enum.IsDefined(typeof(CriterionModifier), numeric))
                return (CriterionModifier)numeric;

            throw new JsonException($"Invalid criterion modifier token '{reader.TokenType}'.");
        }

        public override void Write(Utf8JsonWriter writer, CriterionModifier value, JsonSerializerOptions options)
            => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

        private static bool TryParse(string? value, out CriterionModifier modifier)
        {
            modifier = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = Normalize(value);
            foreach (var name in Enum.GetNames<CriterionModifier>())
            {
                if (!string.Equals(Normalize(name), normalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                modifier = Enum.Parse<CriterionModifier>(name);
                return true;
            }

            return false;
        }

        private static string Normalize(string value)
            => new(value.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string NormalizeEntityType(string? entityType)
    {
        var normalized = string.IsNullOrWhiteSpace(entityType) ? "scene" : entityType.Trim().ToLowerInvariant();
        return normalized.EndsWith('s') ? normalized[..^1] : normalized;
    }

    private static IQueryable<Audio> ApplyAudioSearch(IQueryable<Audio> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var value = $"%{search.Trim()}%";
        return query.Where(audio =>
            (audio.Title != null && EF.Functions.ILike(audio.Title, value))
            || (audio.Code != null && EF.Functions.ILike(audio.Code, value))
            || (audio.Details != null && EF.Functions.ILike(audio.Details, value))
            || (audio.MinPath != null && EF.Functions.ILike(audio.MinPath, value))
            || (audio.SearchText != null && EF.Functions.ILike(audio.SearchText, value))
            || (audio.FileSearchText != null && EF.Functions.ILike(audio.FileSearchText, value)));
    }

    private static IQueryable<TextDocument> ApplyTextSearch(IQueryable<TextDocument> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var value = $"%{search.Trim()}%";
        return query.Where(text =>
            (text.Title != null && EF.Functions.ILike(text.Title, value))
            || (text.Code != null && EF.Functions.ILike(text.Code, value))
            || (text.Details != null && EF.Functions.ILike(text.Details, value))
            || (text.MinPath != null && EF.Functions.ILike(text.MinPath, value))
            || (text.SearchText != null && EF.Functions.ILike(text.SearchText, value))
            || (text.FileSearchText != null && EF.Functions.ILike(text.FileSearchText, value)));
    }

    private static IQueryable<Segment> ApplySegmentSearch(IQueryable<Segment> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var value = $"%{search.Trim()}%";
        return query.Where(segment =>
            (segment.Title != null && EF.Functions.ILike(segment.Title, value))
            || (segment.Kind != null && EF.Functions.ILike(segment.Kind, value))
            || EF.Functions.ILike(segment.SourceKey, value)
            || (segment.Tag != null && EF.Functions.ILike(segment.Tag.Name, value)));
    }

    private IQueryable<Audio> ApplyAudioFilter(IQueryable<Audio> query, AudioFilter? filter)
    {
        if (filter == null)
            return query;

        query = FilterHelpers.ApplyString(query, filter.TitleCriterion, audio => audio.Title);
        query = FilterHelpers.ApplyString(query, filter.CodeCriterion, audio => audio.Code);
        query = FilterHelpers.ApplyString(query, filter.DetailsCriterion, audio => audio.Details);
        query = FilterHelpers.ApplyFilePath(query, filter.PathCriterion, audio => audio.Files);
        query = FilterHelpers.ApplyString(query, filter.UrlCriterion, audio => audio.Urls.Select(url => url.Url).FirstOrDefault());
        query = FilterHelpers.ApplyBool(query, filter.OrganizedCriterion, audio => audio.Organized);
        query = FilterHelpers.ApplyDate(query, filter.DateCriterion, audio => audio.Date);
        query = FilterHelpers.ApplyInt(query, filter.DurationCriterion, audio => (int)audio.MaxDuration);
        query = FilterHelpers.ApplyInt(query, filter.FileCountCriterion, audio => audio.FileCount);
        query = ApplyAudioEffectiveTagCountCriterion(query, filter.TagCountCriterion);
        query = FilterHelpers.ApplyInt(query, filter.PerformerCountCriterion, audio => audio.AudioPerformers.Count);
        query = ApplyAudioTagCriterion(query, filter.TagsCriterion);
        query = FilterHelpers.ApplyMultiId(query, filter.PerformersCriterion, audio => audio.AudioPerformers.Select(link => link.PerformerId));
        query = ApplyAudioPerformerOccurrenceTagCriterion(query, filter.PerformerTagsCriterion, GetIncludedPerformerIds(filter));
        query = FilterHelpers.ApplyStudioCriterion(query, filter.StudiosCriterion, audio => audio.StudioId);
        query = FilterHelpers.ApplyMultiId(query, filter.GroupsCriterion, audio => db.GroupItems
            .Where(item => item.HostType == "audio" && item.HostId == audio.Id && item.Kind == GroupItemKind.Audio)
            .Select(item => item.GroupId));
        query = FilterHelpers.ApplyTimestamp(query, filter.CreatedAtCriterion, audio => audio.CreatedAt);
        query = FilterHelpers.ApplyTimestamp(query, filter.UpdatedAtCriterion, audio => audio.UpdatedAt);
        query = query.ApplyCustomFieldCriteria(db, CustomFieldEntityTypes.Audio, filter.CustomFieldCriterion, filter.CustomFieldCriteria);
        return query;
    }

    private IQueryable<TextDocument> ApplyTextFilter(IQueryable<TextDocument> query, TextDocumentFilter? filter)
    {
        if (filter == null)
            return query;

        query = FilterHelpers.ApplyString(query, filter.TitleCriterion, text => text.Title);
        query = FilterHelpers.ApplyString(query, filter.CodeCriterion, text => text.Code);
        query = FilterHelpers.ApplyString(query, filter.DetailsCriterion, text => text.Details);
        query = FilterHelpers.ApplyFilePath(query, filter.PathCriterion, text => text.Files);
        query = FilterHelpers.ApplyString(query, filter.UrlCriterion, text => text.Urls.Select(url => url.Url).FirstOrDefault());
        query = FilterHelpers.ApplyBool(query, filter.OrganizedCriterion, text => text.Organized);
        query = FilterHelpers.ApplyDate(query, filter.DateCriterion, text => text.Date);
        query = FilterHelpers.ApplyInt(query, filter.WordCountCriterion, text => text.MaxWordCount ?? 0);
        query = FilterHelpers.ApplyInt(query, filter.PageCountCriterion, text => text.MaxPageCount ?? 0);
        query = FilterHelpers.ApplyInt(query, filter.FileCountCriterion, text => text.FileCount);
        query = FilterHelpers.ApplyInt(query, filter.TagCountCriterion, text => text.TextTags.Count);
        query = FilterHelpers.ApplyInt(query, filter.PerformerCountCriterion, text => text.TextPerformers.Count);
        query = FilterHelpers.ApplyMultiId(query, filter.TagsCriterion, text => text.TextTags.Select(link => link.TagId));
        query = FilterHelpers.ApplyMultiId(query, filter.PerformersCriterion, text => text.TextPerformers.Select(link => link.PerformerId));
        query = ApplyTextPerformerOccurrenceTagCriterion(query, filter.PerformerTagsCriterion, GetIncludedPerformerIds(filter));
        query = FilterHelpers.ApplyStudioCriterion(query, filter.StudiosCriterion, text => text.StudioId);
        query = FilterHelpers.ApplyMultiId(query, filter.GroupsCriterion, text => db.GroupItems
            .Where(item => item.HostType == "text" && item.HostId == text.Id && item.Kind == GroupItemKind.Text)
            .Select(item => item.GroupId));
        query = FilterHelpers.ApplyTimestamp(query, filter.CreatedAtCriterion, text => text.CreatedAt);
        query = FilterHelpers.ApplyTimestamp(query, filter.UpdatedAtCriterion, text => text.UpdatedAt);
        query = query.ApplyCustomFieldCriteria(db, CustomFieldEntityTypes.Text, filter.CustomFieldCriterion, filter.CustomFieldCriteria);
        return query;
    }

    private IQueryable<Audio> ApplyAudioEffectiveTagCountCriterion(IQueryable<Audio> query, IntCriterion? criterion)
    {
        if (criterion == null)
            return query;

        var effectiveTags = EffectiveHostTagQuery.ForHostType(db, AffinityHostType.Audio);
        return FilterHelpers.ApplyInt(query, criterion, audio => effectiveTags
            .Where(tag => tag.HostId == audio.Id)
            .Select(tag => tag.TagId)
            .Distinct()
            .Count());
    }

    private IQueryable<Audio> ApplyAudioTagCriterion(IQueryable<Audio> query, MultiIdCriterion? criterion)
    {
        if (criterion == null)
            return query;

        var effectiveTags = EffectiveHostTagQuery.ForHostType(db, AffinityHostType.Audio);
        if (criterion.Modifier == CriterionModifier.IsNull)
            return query.Where(audio => !effectiveTags.Any(tag => tag.HostId == audio.Id));
        if (criterion.Modifier == CriterionModifier.NotNull)
            return query.Where(audio => effectiveTags.Any(tag => tag.HostId == audio.Id));

        var ids = criterion.Value.Where(tagId => tagId > 0).Distinct().ToArray();
        if (ids.Length > 0)
        {
            query = criterion.Modifier switch
            {
                CriterionModifier.Excludes => query.Where(audio => !effectiveTags.Any(tag => tag.HostId == audio.Id && ids.Contains(tag.TagId))),
                CriterionModifier.ExcludesAll => ApplyAudioTagExcludesAll(query, effectiveTags, ids),
                CriterionModifier.IncludesAll => ApplyAudioTagIncludesAll(query, effectiveTags, ids),
                _ => query.Where(audio => effectiveTags.Any(tag => tag.HostId == audio.Id && ids.Contains(tag.TagId))),
            };
        }

        var excludedIds = criterion.Excludes?.Where(tagId => tagId > 0).Distinct().ToArray() ?? [];
        if (excludedIds.Length > 0)
            query = query.Where(audio => !effectiveTags.Any(tag => tag.HostId == audio.Id && excludedIds.Contains(tag.TagId)));

        return query;
    }

    private static IQueryable<Audio> ApplyAudioTagIncludesAll(IQueryable<Audio> query, IQueryable<EffectiveHostTagRow> effectiveTags, IReadOnlyCollection<int> tagIds)
    {
        foreach (var tagId in tagIds)
            query = query.Where(audio => effectiveTags.Any(tag => tag.HostId == audio.Id && tag.TagId == tagId));
        return query;
    }

    private static IQueryable<Audio> ApplyAudioTagExcludesAll(IQueryable<Audio> query, IQueryable<EffectiveHostTagRow> effectiveTags, IReadOnlyCollection<int> tagIds)
    {
        var matchingAll = query;
        foreach (var tagId in tagIds)
            matchingAll = matchingAll.Where(audio => effectiveTags.Any(tag => tag.HostId == audio.Id && tag.TagId == tagId));
        return query.Where(audio => !matchingAll.Select(match => match.Id).Contains(audio.Id));
    }

    private IQueryable<Audio> ApplyAudioPerformerOccurrenceTagCriterion(IQueryable<Audio> query, MultiIdCriterion? criterion, IReadOnlyCollection<int> performerIds)
    {
        if (criterion == null)
            return query;

        var tagIds = criterion.Value.Where(tagId => tagId > 0).Distinct().ToArray();
        var excludedTagIds = criterion.Excludes?.Where(tagId => tagId > 0).Distinct().ToArray() ?? [];
        if (tagIds.Length == 0 && excludedTagIds.Length == 0)
            return query;

        var scopedApplications = db.TagApplications.AsNoTracking()
            .Where(application => application.HostType == AffinityHostType.Audio
                && application.ContextType == "performer"
                && application.ContextId != null);

        if (performerIds.Count > 0)
        {
            var performerIdArray = performerIds.ToArray();
            scopedApplications = scopedApplications.Where(application => application.ContextId != null && performerIdArray.Contains(application.ContextId.Value));
        }

        if (tagIds.Length > 0)
        {
            query = criterion.Modifier switch
            {
                CriterionModifier.Excludes => query.Where(audio => !scopedApplications.Any(application => application.HostId == audio.Id && tagIds.Contains(application.TagId))),
                CriterionModifier.ExcludesAll => ApplyAudioPerformerOccurrenceTagExcludesAll(query, scopedApplications, tagIds),
                CriterionModifier.IncludesAll => ApplyAudioPerformerOccurrenceTagIncludesAll(query, scopedApplications, tagIds),
                _ => query.Where(audio => scopedApplications.Any(application => application.HostId == audio.Id && tagIds.Contains(application.TagId))),
            };
        }

        if (excludedTagIds.Length > 0)
            query = query.Where(audio => !scopedApplications.Any(application => application.HostId == audio.Id && excludedTagIds.Contains(application.TagId)));

        return query;
    }

    private static IQueryable<Audio> ApplyAudioPerformerOccurrenceTagIncludesAll(IQueryable<Audio> query, IQueryable<TagApplication> applications, IReadOnlyCollection<int> tagIds)
    {
        foreach (var tagId in tagIds)
            query = query.Where(audio => applications.Any(application => application.HostId == audio.Id && application.TagId == tagId));
        return query;
    }

    private static IQueryable<Audio> ApplyAudioPerformerOccurrenceTagExcludesAll(IQueryable<Audio> query, IQueryable<TagApplication> applications, IReadOnlyCollection<int> tagIds)
    {
        var matchingAll = query;
        foreach (var tagId in tagIds)
            matchingAll = matchingAll.Where(audio => applications.Any(application => application.HostId == audio.Id && application.TagId == tagId));
        return query.Where(audio => !matchingAll.Select(match => match.Id).Contains(audio.Id));
    }

    private IQueryable<TextDocument> ApplyTextPerformerOccurrenceTagCriterion(IQueryable<TextDocument> query, MultiIdCriterion? criterion, IReadOnlyCollection<int> performerIds)
    {
        if (criterion == null)
            return query;

        var tagIds = criterion.Value.Where(tagId => tagId > 0).Distinct().ToArray();
        var excludedTagIds = criterion.Excludes?.Where(tagId => tagId > 0).Distinct().ToArray() ?? [];
        if (tagIds.Length == 0 && excludedTagIds.Length == 0)
            return query;

        var scopedApplications = db.TagApplications.AsNoTracking()
            .Where(application => application.HostType == AffinityHostType.Text
                && application.ContextType == "performer"
                && application.ContextId != null);

        if (performerIds.Count > 0)
        {
            var performerIdArray = performerIds.ToArray();
            scopedApplications = scopedApplications.Where(application => application.ContextId != null && performerIdArray.Contains(application.ContextId.Value));
        }

        if (tagIds.Length > 0)
        {
            query = criterion.Modifier switch
            {
                CriterionModifier.Excludes => query.Where(text => !scopedApplications.Any(application => application.HostId == text.Id && tagIds.Contains(application.TagId))),
                CriterionModifier.ExcludesAll => ApplyTextPerformerOccurrenceTagExcludesAll(query, scopedApplications, tagIds),
                CriterionModifier.IncludesAll => ApplyTextPerformerOccurrenceTagIncludesAll(query, scopedApplications, tagIds),
                _ => query.Where(text => scopedApplications.Any(application => application.HostId == text.Id && tagIds.Contains(application.TagId))),
            };
        }

        if (excludedTagIds.Length > 0)
            query = query.Where(text => !scopedApplications.Any(application => application.HostId == text.Id && excludedTagIds.Contains(application.TagId)));

        return query;
    }

    private static IQueryable<TextDocument> ApplyTextPerformerOccurrenceTagIncludesAll(IQueryable<TextDocument> query, IQueryable<TagApplication> applications, IReadOnlyCollection<int> tagIds)
    {
        foreach (var tagId in tagIds)
            query = query.Where(text => applications.Any(application => application.HostId == text.Id && application.TagId == tagId));
        return query;
    }

    private static IQueryable<TextDocument> ApplyTextPerformerOccurrenceTagExcludesAll(IQueryable<TextDocument> query, IQueryable<TagApplication> applications, IReadOnlyCollection<int> tagIds)
    {
        var matchingAll = query;
        foreach (var tagId in tagIds)
            matchingAll = matchingAll.Where(text => applications.Any(application => application.HostId == text.Id && application.TagId == tagId));
        return query.Where(text => !matchingAll.Select(match => match.Id).Contains(text.Id));
    }

    private static int[] GetIncludedPerformerIds(AudioFilter filter)
        => filter.PerformersCriterion?.Value is { Count: > 0 }
            && filter.PerformersCriterion.Modifier is CriterionModifier.Includes or CriterionModifier.IncludesAll
            ? filter.PerformersCriterion.Value.Where(id => id > 0).Distinct().ToArray()
            : [];

    private static int[] GetIncludedPerformerIds(TextDocumentFilter filter)
        => filter.PerformersCriterion?.Value is { Count: > 0 }
            && filter.PerformersCriterion.Modifier is CriterionModifier.Includes or CriterionModifier.IncludesAll
            ? filter.PerformersCriterion.Value.Where(id => id > 0).Distinct().ToArray()
            : [];

    private static GroupItemKind KindForEntityType(string entityType) => entityType switch
    {
        "image" => GroupItemKind.Image,
        "audio" => GroupItemKind.Audio,
        "text" => GroupItemKind.Text,
        "segment" => GroupItemKind.Segment,
        _ => GroupItemKind.Scene,
    };

    private static IQueryable<Audio> ApplyAudioSort(IQueryable<Audio> query, string? sort, bool desc) => (sort ?? "updated_at") switch
    {
        "title" => desc ? query.OrderByDescending(audio => audio.Title ?? audio.MinPath).ThenByDescending(audio => audio.Id) : query.OrderBy(audio => audio.Title ?? audio.MinPath).ThenBy(audio => audio.Id),
        "date" => desc ? query.OrderByDescending(audio => audio.Date ?? DateOnly.MinValue).ThenByDescending(audio => audio.Id) : query.OrderBy(audio => audio.Date ?? DateOnly.MinValue).ThenBy(audio => audio.Id),
        "duration" => desc ? query.OrderByDescending(audio => audio.MaxDuration).ThenByDescending(audio => audio.Id) : query.OrderBy(audio => audio.MaxDuration).ThenBy(audio => audio.Id),
        "created_at" => desc ? query.OrderByDescending(audio => audio.CreatedAt).ThenByDescending(audio => audio.Id) : query.OrderBy(audio => audio.CreatedAt).ThenBy(audio => audio.Id),
        _ => desc ? query.OrderByDescending(audio => audio.UpdatedAt).ThenByDescending(audio => audio.Id) : query.OrderBy(audio => audio.UpdatedAt).ThenBy(audio => audio.Id),
    };

    private static IQueryable<TextDocument> ApplyTextSort(IQueryable<TextDocument> query, string? sort, bool desc) => (sort ?? "updated_at") switch
    {
        "title" => desc ? query.OrderByDescending(text => text.Title ?? text.MinPath).ThenByDescending(text => text.Id) : query.OrderBy(text => text.Title ?? text.MinPath).ThenBy(text => text.Id),
        "date" => desc ? query.OrderByDescending(text => text.Date ?? DateOnly.MinValue).ThenByDescending(text => text.Id) : query.OrderBy(text => text.Date ?? DateOnly.MinValue).ThenBy(text => text.Id),
        "word_count" => desc ? query.OrderByDescending(text => text.MaxWordCount).ThenByDescending(text => text.Id) : query.OrderBy(text => text.MaxWordCount).ThenBy(text => text.Id),
        "created_at" => desc ? query.OrderByDescending(text => text.CreatedAt).ThenByDescending(text => text.Id) : query.OrderBy(text => text.CreatedAt).ThenBy(text => text.Id),
        _ => desc ? query.OrderByDescending(text => text.UpdatedAt).ThenByDescending(text => text.Id) : query.OrderBy(text => text.UpdatedAt).ThenBy(text => text.Id),
    };

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
        public string? EntityType { get; set; }
        public List<string>? EntityTypes { get; set; }
        public FindFilter? FindFilter { get; set; }
        public Dictionary<string, FindFilter>? FindFilters { get; set; }
        public JsonElement? ObjectFilter { get; set; }
        public Dictionary<string, JsonElement>? ObjectFilters { get; set; }
    }

    private sealed record FilterEntityConfig(string EntityType, FindFilter? FindFilter, JsonElement? ObjectFilter);
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
