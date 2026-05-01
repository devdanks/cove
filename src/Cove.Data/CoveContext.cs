using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Entities.Auth;
using Cove.Plugins;
using System.Text.Json;
using System.Linq.Expressions;
using Pgvector;

namespace Cove.Data;

public partial class CoveContext : DbContext
{
    private static IReadOnlyList<IDataExtension> _dataExtensions = [];
    private bool _persistingDerivedCounts;

    public static void SetDataExtensions(IEnumerable<IDataExtension> extensions)
    {
        _dataExtensions = extensions.ToList();
    }

    public CoveContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor? principalAccessor = null) : base(options)
    {
        _principalAccessor = principalAccessor;
    }

    protected CoveContext(DbContextOptions options) : base(options) { }

    // Core entities
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<Performer> Performers => Set<Performer>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Studio> Studios => Set<Studio>();
    public DbSet<Gallery> Galleries => Set<Gallery>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<SceneMarker> SceneMarkers => Set<SceneMarker>();
    public DbSet<TagApplication> TagApplications => Set<TagApplication>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<SegmentDisplayProfile> SegmentDisplayProfiles => Set<SegmentDisplayProfile>();
    public DbSet<SegmentDisplayRule> SegmentDisplayRules => Set<SegmentDisplayRule>();
    public DbSet<Detection> Detections => Set<Detection>();
    public DbSet<Face> Faces => Set<Face>();
    public DbSet<FaceAppearance> FaceAppearances => Set<FaceAppearance>();
    public DbSet<FaceSuggestionDecision> FaceSuggestionDecisions => Set<FaceSuggestionDecision>();
    public DbSet<Embedding> Embeddings => Set<Embedding>();
    public DbSet<AiRun> AiRuns => Set<AiRun>();
    public DbSet<UserEntityAffinity> UserEntityAffinities => Set<UserEntityAffinity>();
    public DbSet<Interaction> Interactions => Set<Interaction>();
    public DbSet<PlaybackSession> PlaybackSessions => Set<PlaybackSession>();
    public DbSet<PlaybackInterval> PlaybackIntervals => Set<PlaybackInterval>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<SavedFilter> SavedFilters => Set<SavedFilter>();
    public DbSet<GalleryChapter> GalleryChapters => Set<GalleryChapter>();
    public DbSet<ScrapeAttempt> ScrapeAttempts => Set<ScrapeAttempt>();

    // Users / Auth / Permissions / Audit
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleContentRule> RoleContentRules => Set<RoleContentRule>();
    public DbSet<RoleEntityOverride> RoleEntityOverrides => Set<RoleEntityOverride>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // Schema C Stage 1: universal identifier table (dual-write with *Url/*Alias/*RemoteId)
    public DbSet<EntityIdentifier> EntityIdentifiers => Set<EntityIdentifier>();

    // Extensions
    public DbSet<ExtensionData> ExtensionData => Set<ExtensionData>();

    // Files & Folders
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<VideoFile> VideoFiles => Set<VideoFile>();
    public DbSet<ImageFile> ImageFiles => Set<ImageFile>();
    public DbSet<GalleryFile> GalleryFiles => Set<GalleryFile>();
    public DbSet<FileFingerprint> FileFingerprints => Set<FileFingerprint>();
    public DbSet<VideoCaption> VideoCaptions => Set<VideoCaption>();
    public DbSet<GroupItem> GroupItems => Set<GroupItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoveContext).Assembly);

        // TPH for file hierarchy
        modelBuilder.Entity<BaseFileEntity>()
            .HasDiscriminator<string>("FileType")
            .HasValue<VideoFile>("Video")
            .HasValue<ImageFile>("Image")
            .HasValue<GalleryFile>("Gallery");

        modelBuilder.Entity<BaseFileEntity>()
            .HasMany(f => f.Fingerprints)
            .WithOne(fp => fp.File)
            .HasForeignKey(fp => fp.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FileFingerprint>(entity =>
        {
            entity.HasIndex(fp => new { fp.Type, fp.Value });
            entity.HasIndex(fp => fp.FileId);
        });

        modelBuilder.Entity<VideoCaption>()
            .ToTable("VideoCaptions");

        modelBuilder.Entity<VideoFile>()
            .HasMany(v => v.Captions)
            .WithOne(c => c.File)
            .HasForeignKey(c => c.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlaybackSession>(entity =>
        {
            entity.HasIndex(session => new { session.UserId, session.HostType, session.HostId, session.StartedAt });
            entity.HasIndex(session => new { session.UserId, session.SessionId }).IsUnique();
            entity.HasMany(session => session.Intervals)
                .WithOne(interval => interval.Session)
                .HasForeignKey(interval => interval.PlaybackSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaybackInterval>(entity =>
        {
            entity.HasIndex(interval => new { interval.UserId, interval.HostType, interval.HostId });
            entity.HasIndex(interval => new { interval.PlaybackSessionId, interval.StartSec });
        });

        foreach (var ext in _dataExtensions)
        {
            ext.ConfigureModel(modelBuilder);
        }

        ConfigureVectorStorage(modelBuilder);

        if (Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            ConfigureAuthorizationFilters(modelBuilder);
        }
        else
            ConfigureProviderFallbacks(modelBuilder);
    }

    private static void ConfigureVectorStorage(ModelBuilder modelBuilder)
    {
        var vectorConverter = new ValueConverter<Vector?, string?>(
            vector => vector == null ? null : SerializeVector(vector),
            json => string.IsNullOrWhiteSpace(json) ? null : DeserializeVector(json));

        var vectorComparer = new ValueComparer<Vector?>(
            (left, right) => left == null ? right == null : right != null && VectorsEqual(left, right),
            vector => vector == null ? 0 : GetVectorHash(vector),
            vector => vector == null ? null : CloneVector(vector));

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()))
        {
            if (property.ClrType != typeof(Vector))
                continue;

            property.SetValueConverter(vectorConverter);
            property.SetValueComparer(vectorComparer);
            property.SetColumnType("text");
        }
    }

    private static void ConfigureProviderFallbacks(ModelBuilder modelBuilder)
    {
        var jsonConverter = new ValueConverter<JsonDocument?, string?>(
            document => SerializeJsonDocument(document),
            json => DeserializeJsonDocument(json));

        var jsonComparer = new ValueComparer<JsonDocument?>(
            (left, right) => JsonDocumentsEqual(left, right),
            document => GetJsonDocumentHash(document),
            document => CloneJsonDocument(document));

        var objectDictionaryConverter = new ValueConverter<Dictionary<string, object>?, string?>(
            dictionary => SerializeObjectDictionary(dictionary),
            json => DeserializeObjectDictionary(json));

        var objectDictionaryComparer = new ValueComparer<Dictionary<string, object>?>(
            (left, right) => string.Equals(GetObjectDictionaryText(left), GetObjectDictionaryText(right), StringComparison.Ordinal),
            dictionary => GetObjectDictionaryHash(dictionary),
            dictionary => CloneObjectDictionary(dictionary));

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()))
        {
            if (property.ClrType == typeof(JsonDocument))
            {
                property.SetValueConverter(jsonConverter);
                property.SetValueComparer(jsonComparer);
            }

            if (property.ClrType == typeof(Dictionary<string, object>))
            {
                property.SetValueConverter(objectDictionaryConverter);
                property.SetValueComparer(objectDictionaryComparer);
            }
        }
    }

    private static string? SerializeJsonDocument(JsonDocument? document) =>
        document is null ? null : document.RootElement.GetRawText();

    private static JsonDocument? DeserializeJsonDocument(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json);

    private static bool JsonDocumentsEqual(JsonDocument? left, JsonDocument? right) =>
        string.Equals(GetJsonText(left), GetJsonText(right), StringComparison.Ordinal);

    private static int GetJsonDocumentHash(JsonDocument? document) =>
        GetJsonText(document)?.GetHashCode(StringComparison.Ordinal) ?? 0;

    private static JsonDocument? CloneJsonDocument(JsonDocument? document) =>
        document is null ? null : JsonDocument.Parse(document.RootElement.GetRawText());

    private static string? GetJsonText(JsonDocument? document) =>
        document is null ? null : document.RootElement.GetRawText();

    private static string? SerializeObjectDictionary(Dictionary<string, object>? dictionary)
    {
        if (dictionary is null)
        {
            return null;
        }

        var normalized = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in dictionary)
        {
            normalized[key] = value;
        }

        return JsonSerializer.Serialize(normalized);
    }

    private static Dictionary<string, object>? DeserializeObjectDictionary(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(json);

    private static string? GetObjectDictionaryText(Dictionary<string, object>? dictionary) =>
        SerializeObjectDictionary(dictionary);

    private static int GetObjectDictionaryHash(Dictionary<string, object>? dictionary) =>
        GetObjectDictionaryText(dictionary) is { } json ? json.GetHashCode(StringComparison.Ordinal) : 0;

    private static Dictionary<string, object>? CloneObjectDictionary(Dictionary<string, object>? dictionary) =>
        DeserializeObjectDictionary(SerializeObjectDictionary(dictionary));

    private static string SerializeVector(Vector vector) =>
        JsonSerializer.Serialize(vector.ToArray());

    private static Vector DeserializeVector(string json)
    {
        var values = JsonSerializer.Deserialize<float[]>(json) ?? [];
        return new Vector(values);
    }

    private static bool VectorsEqual(Vector left, Vector right)
    {
        var leftValues = left.ToArray();
        var rightValues = right.ToArray();

        if (leftValues.Length != rightValues.Length)
            return false;

        for (var index = 0; index < leftValues.Length; index++)
        {
            if (!leftValues[index].Equals(rightValues[index]))
                return false;
        }

        return true;
    }

    private static int GetVectorHash(Vector vector)
    {
        var hash = new HashCode();
        foreach (var value in vector.ToArray())
            hash.Add(value);
        return hash.ToHashCode();
    }

    private static Vector CloneVector(Vector vector) =>
        new(vector.ToArray());

    public override int SaveChanges()
    {
        if (_persistingDerivedCounts)
            return base.SaveChanges();

        UpdateTimestamps();
        ComputeFilePaths();
        MaintainDenormalizedIdArrays();
        var derivedCountTargets = CollectDerivedCountTargets();
        var result = base.SaveChanges();
        PersistDerivedCounts(derivedCountTargets);
        return result;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_persistingDerivedCounts)
            return base.SaveChangesAsync(cancellationToken);

        UpdateTimestamps();
        ComputeFilePaths();
        MaintainDenormalizedIdArrays();
        var derivedCountTargets = CollectDerivedCountTargets();
        return SaveChangesWithDerivedCountsAsync(derivedCountTargets, cancellationToken);
    }

    private async Task<int> SaveChangesWithDerivedCountsAsync(DerivedCountTargets derivedCountTargets, CancellationToken cancellationToken)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        await PersistDerivedCountsAsync(derivedCountTargets, cancellationToken);
        return result;
    }

    private void MaintainDenormalizedIdArrays()
    {
        // Refresh GIN-indexed Scene/Image/Gallery TagIds/PerformerIds arrays whenever
        // the corresponding join tables change. The arrays let combo filters like
        // "scenes with tags A AND B AND performer C" run as a single index-only
        // array-containment scan instead of N joins per filter term.
        //
        // Strategy: collect parent ids whose link rows changed in this unit of work,
        // then for each parent rebuild the array from the join table in one query per
        // (parent type, link type). This is O(changed parents) round-trips, not O(rows).

        InitializeAddedParentIdArrays();

        var sceneTagParents = CollectChangedParentIds<SceneTag>(e => e.SceneId);
        var scenePerformerParents = CollectChangedParentIds<ScenePerformer>(e => e.SceneId);
        var imageTagParents = CollectChangedParentIds<ImageTag>(e => e.ImageId);
        var imagePerformerParents = CollectChangedParentIds<ImagePerformer>(e => e.ImageId);
        var galleryTagParents = CollectChangedParentIds<GalleryTag>(e => e.GalleryId);
        var galleryPerformerParents = CollectChangedParentIds<GalleryPerformer>(e => e.GalleryId);

        // Also handle Added Scene/Image/Gallery rows whose join collections were set
        // through the navigation property: in that case the link entries are Added too
        // and will already be picked up above. But a freshly-Added parent with no links
        // still needs its arrays initialized to an empty array (the default), so nothing
        // extra is needed here.

        if (sceneTagParents.Count > 0)
            RebuildArray<Scene, SceneTag>(sceneTagParents, s => s.TagIds, e => e.SceneId, e => e.TagId);
        if (scenePerformerParents.Count > 0)
            RebuildArray<Scene, ScenePerformer>(scenePerformerParents, s => s.PerformerIds, e => e.SceneId, e => e.PerformerId);
        if (imageTagParents.Count > 0)
            RebuildArray<Image, ImageTag>(imageTagParents, i => i.TagIds, e => e.ImageId, e => e.TagId);
        if (imagePerformerParents.Count > 0)
            RebuildArray<Image, ImagePerformer>(imagePerformerParents, i => i.PerformerIds, e => e.ImageId, e => e.PerformerId);
        if (galleryTagParents.Count > 0)
            RebuildArray<Gallery, GalleryTag>(galleryTagParents, g => g.TagIds, e => e.GalleryId, e => e.TagId);
        if (galleryPerformerParents.Count > 0)
            RebuildArray<Gallery, GalleryPerformer>(galleryPerformerParents, g => g.PerformerIds, e => e.GalleryId, e => e.PerformerId);
    }

    private readonly record struct DerivedCountTargets(
        HashSet<int> TagIds,
        HashSet<int> StudioIds,
        HashSet<int> PerformerIds,
        HashSet<int> GalleryIds,
        HashSet<int> SceneIds,
        HashSet<int> ImageIds)
    {
        public bool HasAny => TagIds.Count > 0
            || StudioIds.Count > 0
            || PerformerIds.Count > 0
            || GalleryIds.Count > 0
            || SceneIds.Count > 0
            || ImageIds.Count > 0;
    }

    private DerivedCountTargets CollectDerivedCountTargets()
    {
        return new DerivedCountTargets(
            CollectAffectedTagCountIds(),
            CollectAffectedStudioCountIds(),
            CollectAffectedPerformerCountIds(),
            CollectAffectedGalleryCountIds(),
            CollectAffectedSceneMetricIds(),
            CollectAffectedImageIds());
    }

    private HashSet<int> CollectAffectedSceneMetricIds()
    {
        var ids = new HashSet<int>();
        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<VideoFile>(), entry => entry.SceneId, nameof(VideoFile.SceneId));
        return ids;
    }

    private HashSet<int> CollectAffectedImageIds()
    {
        var ids = new HashSet<int>();
        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<ImageFile>(), entry => entry.ImageId, nameof(ImageFile.ImageId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<ImageTag>(), entry => entry.ImageId, nameof(ImageTag.ImageId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<ImagePerformer>(), entry => entry.ImageId, nameof(ImagePerformer.ImageId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<ImageGallery>(), entry => entry.ImageId, nameof(ImageGallery.ImageId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Tag>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            tagIds => Set<ImageTag>().AsNoTracking()
                .Where(imageTag => tagIds.Contains(imageTag.TagId))
                .Select(imageTag => imageTag.ImageId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Performer>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            performerIds => Set<ImagePerformer>().AsNoTracking()
                .Where(imagePerformer => performerIds.Contains(imagePerformer.PerformerId))
                .Select(imagePerformer => imagePerformer.ImageId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Gallery>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            galleryIds => Set<ImageGallery>().AsNoTracking()
                .Where(imageGallery => galleryIds.Contains(imageGallery.GalleryId))
                .Select(imageGallery => imageGallery.ImageId));

        return ids;
    }

    private HashSet<int> CollectAffectedPerformerCountIds()
    {
        var ids = new HashSet<int>();

        CollectChangedIntKey(ids, ChangeTracker.Entries<ScenePerformer>(), entry => entry.PerformerId, nameof(ScenePerformer.PerformerId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<ImagePerformer>(), entry => entry.PerformerId, nameof(ImagePerformer.PerformerId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<GalleryPerformer>(), entry => entry.PerformerId, nameof(GalleryPerformer.PerformerId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<PerformerTag>(), entry => entry.PerformerId, nameof(PerformerTag.PerformerId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Scene>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            sceneIds => Set<ScenePerformer>().AsNoTracking()
                .Where(scenePerformer => sceneIds.Contains(scenePerformer.SceneId))
                .Select(scenePerformer => scenePerformer.PerformerId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Image>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            imageIds => Set<ImagePerformer>().AsNoTracking()
                .Where(imagePerformer => imageIds.Contains(imagePerformer.ImageId))
                .Select(imagePerformer => imagePerformer.PerformerId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Gallery>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            galleryIds => Set<GalleryPerformer>().AsNoTracking()
                .Where(galleryPerformer => galleryIds.Contains(galleryPerformer.GalleryId))
                .Select(galleryPerformer => galleryPerformer.PerformerId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Tag>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            tagIds => Set<PerformerTag>().AsNoTracking()
                .Where(performerTag => tagIds.Contains(performerTag.TagId))
                .Select(performerTag => performerTag.PerformerId));

        return ids;
    }

    private HashSet<int> CollectAffectedGalleryCountIds()
    {
        var ids = new HashSet<int>();

        CollectChangedIntKey(ids, ChangeTracker.Entries<ImageGallery>(), entry => entry.GalleryId, nameof(ImageGallery.GalleryId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<SceneGallery>(), entry => entry.GalleryId, nameof(SceneGallery.GalleryId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<GalleryPerformer>(), entry => entry.GalleryId, nameof(GalleryPerformer.GalleryId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<GalleryTag>(), entry => entry.GalleryId, nameof(GalleryTag.GalleryId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Image>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            imageIds => Set<ImageGallery>().AsNoTracking()
                .Where(imageGallery => imageIds.Contains(imageGallery.ImageId))
                .Select(imageGallery => imageGallery.GalleryId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Scene>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            sceneIds => Set<SceneGallery>().AsNoTracking()
                .Where(sceneGallery => sceneIds.Contains(sceneGallery.SceneId))
                .Select(sceneGallery => sceneGallery.GalleryId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Performer>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            performerIds => Set<GalleryPerformer>().AsNoTracking()
                .Where(galleryPerformer => performerIds.Contains(galleryPerformer.PerformerId))
                .Select(galleryPerformer => galleryPerformer.GalleryId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Tag>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            tagIds => Set<GalleryTag>().AsNoTracking()
                .Where(galleryTag => tagIds.Contains(galleryTag.TagId))
                .Select(galleryTag => galleryTag.GalleryId));

        return ids;
    }

    private HashSet<int> CollectAffectedTagCountIds()
    {
        var ids = new HashSet<int>();

        CollectChangedIntKey(ids, ChangeTracker.Entries<SceneTag>(), entry => entry.TagId, nameof(SceneTag.TagId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<PerformerTag>(), entry => entry.TagId, nameof(PerformerTag.TagId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<ImageTag>(), entry => entry.TagId, nameof(ImageTag.TagId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<GalleryTag>(), entry => entry.TagId, nameof(GalleryTag.TagId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<StudioTag>(), entry => entry.TagId, nameof(StudioTag.TagId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<GroupTag>(), entry => entry.TagId, nameof(GroupTag.TagId));
        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<Segment>(), entry => entry.TagId, nameof(Segment.TagId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<SceneMarkerTag>(), entry => entry.TagId, nameof(SceneMarkerTag.TagId));

        foreach (var entry in ChangeTracker.Entries<SceneMarker>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            AddIfPositive(ids, entry.Entity.PrimaryTagId);
            AddIfPositive(ids, entry.Property<int>(nameof(SceneMarker.PrimaryTagId)).OriginalValue);
        }

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Scene>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            sceneIds => Set<SceneTag>().AsNoTracking()
                .Where(sceneTag => sceneIds.Contains(sceneTag.SceneId))
                .Select(sceneTag => sceneTag.TagId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Performer>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            performerIds => Set<PerformerTag>().AsNoTracking()
                .Where(performerTag => performerIds.Contains(performerTag.PerformerId))
                .Select(performerTag => performerTag.TagId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Image>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            imageIds => Set<ImageTag>().AsNoTracking()
                .Where(imageTag => imageIds.Contains(imageTag.ImageId))
                .Select(imageTag => imageTag.TagId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Gallery>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            galleryIds => Set<GalleryTag>().AsNoTracking()
                .Where(galleryTag => galleryIds.Contains(galleryTag.GalleryId))
                .Select(galleryTag => galleryTag.TagId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Studio>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            studioIds => Set<StudioTag>().AsNoTracking()
                .Where(studioTag => studioIds.Contains(studioTag.StudioId))
                .Select(studioTag => studioTag.TagId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Group>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            groupIds => Set<GroupTag>().AsNoTracking()
                .Where(groupTag => groupIds.Contains(groupTag.GroupId))
                .Select(groupTag => groupTag.TagId));

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<SceneMarker>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            markerIds => Set<SceneMarkerTag>().AsNoTracking()
                .Where(sceneMarkerTag => markerIds.Contains(sceneMarkerTag.SceneMarkerId))
                .Select(sceneMarkerTag => sceneMarkerTag.TagId));

        return ids;
    }

    private HashSet<int> CollectAffectedStudioCountIds()
    {
        var ids = new HashSet<int>();

        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<Scene>(), entry => entry.StudioId, nameof(Scene.StudioId));
        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<Image>(), entry => entry.StudioId, nameof(Image.StudioId));
        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<Gallery>(), entry => entry.StudioId, nameof(Gallery.StudioId));
        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<Group>(), entry => entry.StudioId, nameof(Group.StudioId));
        CollectChangedNullableIntKey(ids, ChangeTracker.Entries<Studio>(), entry => entry.ParentId, nameof(Studio.ParentId));
        CollectChangedIntKey(ids, ChangeTracker.Entries<StudioTag>(), entry => entry.StudioId, nameof(StudioTag.StudioId));

        var sceneIds = new HashSet<int>();
        foreach (var entry in ChangeTracker.Entries<ScenePerformer>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            AddIfPositive(sceneIds, entry.Entity.SceneId);
            AddIfPositive(sceneIds, entry.Property<int>(nameof(ScenePerformer.SceneId)).OriginalValue);
        }

        if (sceneIds.Count > 0)
        {
            var trackedScenes = ChangeTracker.Entries<Scene>()
                .Where(entry => sceneIds.Contains(entry.Entity.Id))
                .ToDictionary(entry => entry.Entity.Id);

            foreach (var sceneId in sceneIds)
            {
                if (!trackedScenes.TryGetValue(sceneId, out var trackedScene))
                    continue;

                AddIfPositive(ids, trackedScene.Entity.StudioId);
                AddIfPositive(ids, trackedScene.Property<int?>(nameof(Scene.StudioId)).OriginalValue);
            }

            var missingSceneIds = sceneIds.Where(sceneId => !trackedScenes.ContainsKey(sceneId)).ToArray();
            if (missingSceneIds.Length > 0)
            {
                foreach (var studioId in Scenes.AsNoTracking()
                    .Where(scene => missingSceneIds.Contains(scene.Id) && scene.StudioId.HasValue)
                    .Select(scene => scene.StudioId)
                    .ToList())
                {
                    AddIfPositive(ids, studioId);
                }
            }
        }

        AddRelatedIdsFromDeletedParents(ids,
            ChangeTracker.Entries<Performer>()
                .Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id)
                .ToArray(),
            performerIds => Set<ScenePerformer>().AsNoTracking()
                .Where(scenePerformer => performerIds.Contains(scenePerformer.PerformerId) && scenePerformer.Scene!.StudioId.HasValue)
                .Select(scenePerformer => scenePerformer.Scene!.StudioId!.Value));

        return ids;
    }

    private void PersistDerivedCounts(DerivedCountTargets derivedCountTargets)
    {
        if (!derivedCountTargets.HasAny)
            return;

        _persistingDerivedCounts = true;
        try
        {
            if (derivedCountTargets.TagIds.Count > 0)
                RefreshTagCounts(derivedCountTargets.TagIds);
            if (derivedCountTargets.StudioIds.Count > 0)
                RefreshStudioCounts(derivedCountTargets.StudioIds);
            if (derivedCountTargets.PerformerIds.Count > 0)
                RefreshPerformerCounts(derivedCountTargets.PerformerIds);
            if (derivedCountTargets.GalleryIds.Count > 0)
                RefreshGalleryCounts(derivedCountTargets.GalleryIds);
            if (derivedCountTargets.SceneIds.Count > 0)
                RefreshSceneMetrics(derivedCountTargets.SceneIds);
            if (derivedCountTargets.ImageIds.Count > 0)
                RefreshImageMetrics(derivedCountTargets.ImageIds);

            if (ChangeTracker.HasChanges())
                base.SaveChanges();
        }
        finally
        {
            _persistingDerivedCounts = false;
        }
    }

    private async Task PersistDerivedCountsAsync(DerivedCountTargets derivedCountTargets, CancellationToken cancellationToken)
    {
        if (!derivedCountTargets.HasAny)
            return;

        _persistingDerivedCounts = true;
        try
        {
            if (derivedCountTargets.TagIds.Count > 0)
                await RefreshTagCountsAsync(derivedCountTargets.TagIds, cancellationToken);
            if (derivedCountTargets.StudioIds.Count > 0)
                await RefreshStudioCountsAsync(derivedCountTargets.StudioIds, cancellationToken);
            if (derivedCountTargets.PerformerIds.Count > 0)
                await RefreshPerformerCountsAsync(derivedCountTargets.PerformerIds, cancellationToken);
            if (derivedCountTargets.GalleryIds.Count > 0)
                await RefreshGalleryCountsAsync(derivedCountTargets.GalleryIds, cancellationToken);
            if (derivedCountTargets.SceneIds.Count > 0)
                await RefreshSceneMetricsAsync(derivedCountTargets.SceneIds, cancellationToken);
            if (derivedCountTargets.ImageIds.Count > 0)
                await RefreshImageMetricsAsync(derivedCountTargets.ImageIds, cancellationToken);

            if (ChangeTracker.HasChanges())
                await base.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _persistingDerivedCounts = false;
        }
    }

    private void RefreshTagCounts(HashSet<int> affectedTagIds)
    {
        var tags = Tags.Where(BuildIdContainsPredicate<Tag>(affectedTagIds.ToArray())).ToDictionary(tag => tag.Id);
        if (tags.Count == 0)
            return;

        var ids = tags.Keys.ToArray();
        var sceneCounts = Set<SceneTag>().AsNoTracking().Where(sceneTag => ids.Contains(sceneTag.TagId))
            .GroupBy(sceneTag => sceneTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var sceneSegmentCounts = Segments.AsNoTracking()
            .Where(segment => segment.HostType == SegmentHostType.Scene && segment.TagId.HasValue && ids.Contains(segment.TagId.Value))
            .GroupBy(segment => segment.TagId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var imageCounts = Set<ImageTag>().AsNoTracking().Where(imageTag => ids.Contains(imageTag.TagId))
            .GroupBy(imageTag => imageTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var galleryCounts = Set<GalleryTag>().AsNoTracking().Where(galleryTag => ids.Contains(galleryTag.TagId))
            .GroupBy(galleryTag => galleryTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var groupCounts = Set<GroupTag>().AsNoTracking().Where(groupTag => ids.Contains(groupTag.TagId))
            .GroupBy(groupTag => groupTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var performerCounts = Set<PerformerTag>().AsNoTracking().Where(performerTag => ids.Contains(performerTag.TagId))
            .GroupBy(performerTag => performerTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var studioCounts = Set<StudioTag>().AsNoTracking().Where(studioTag => ids.Contains(studioTag.TagId))
            .GroupBy(studioTag => studioTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);

        foreach (var tag in tags.Values)
        {
            tag.SceneCount = sceneCounts.GetValueOrDefault(tag.Id, 0);
            tag.SceneMarkerCount = sceneSegmentCounts.GetValueOrDefault(tag.Id, 0);
            tag.ImageCount = imageCounts.GetValueOrDefault(tag.Id, 0);
            tag.GalleryCount = galleryCounts.GetValueOrDefault(tag.Id, 0);
            tag.GroupCount = groupCounts.GetValueOrDefault(tag.Id, 0);
            tag.PerformerCount = performerCounts.GetValueOrDefault(tag.Id, 0);
            tag.StudioCount = studioCounts.GetValueOrDefault(tag.Id, 0);
        }
    }

    private async Task RefreshTagCountsAsync(HashSet<int> affectedTagIds, CancellationToken cancellationToken)
    {
        var tags = await Tags.Where(BuildIdContainsPredicate<Tag>(affectedTagIds.ToArray())).ToDictionaryAsync(tag => tag.Id, cancellationToken);
        if (tags.Count == 0)
            return;

        var ids = tags.Keys.ToArray();
        var sceneCounts = await Set<SceneTag>().AsNoTracking().Where(sceneTag => ids.Contains(sceneTag.TagId))
            .GroupBy(sceneTag => sceneTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var sceneSegmentCounts = await Segments.AsNoTracking()
            .Where(segment => segment.HostType == SegmentHostType.Scene && segment.TagId.HasValue && ids.Contains(segment.TagId.Value))
            .GroupBy(segment => segment.TagId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var imageCounts = await Set<ImageTag>().AsNoTracking().Where(imageTag => ids.Contains(imageTag.TagId))
            .GroupBy(imageTag => imageTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var galleryCounts = await Set<GalleryTag>().AsNoTracking().Where(galleryTag => ids.Contains(galleryTag.TagId))
            .GroupBy(galleryTag => galleryTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var groupCounts = await Set<GroupTag>().AsNoTracking().Where(groupTag => ids.Contains(groupTag.TagId))
            .GroupBy(groupTag => groupTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var performerCounts = await Set<PerformerTag>().AsNoTracking().Where(performerTag => ids.Contains(performerTag.TagId))
            .GroupBy(performerTag => performerTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var studioCounts = await Set<StudioTag>().AsNoTracking().Where(studioTag => ids.Contains(studioTag.TagId))
            .GroupBy(studioTag => studioTag.TagId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        foreach (var tag in tags.Values)
        {
            tag.SceneCount = sceneCounts.GetValueOrDefault(tag.Id, 0);
            tag.SceneMarkerCount = sceneSegmentCounts.GetValueOrDefault(tag.Id, 0);
            tag.ImageCount = imageCounts.GetValueOrDefault(tag.Id, 0);
            tag.GalleryCount = galleryCounts.GetValueOrDefault(tag.Id, 0);
            tag.GroupCount = groupCounts.GetValueOrDefault(tag.Id, 0);
            tag.PerformerCount = performerCounts.GetValueOrDefault(tag.Id, 0);
            tag.StudioCount = studioCounts.GetValueOrDefault(tag.Id, 0);
        }
    }

    private void RefreshSceneMetrics(HashSet<int> affectedSceneIds)
    {
        var scenes = Scenes.Where(BuildIdContainsPredicate<Scene>(affectedSceneIds.ToArray())).ToDictionary(scene => scene.Id);
        if (scenes.Count == 0)
            return;

        var ids = scenes.Keys.ToArray();
        var fileRows = VideoFiles.AsNoTracking()
            .Where(file => file.SceneId.HasValue && ids.Contains(file.SceneId.Value))
            .Select(file => new
            {
                SceneId = file.SceneId!.Value,
                file.Path,
                file.Duration,
                file.Width,
                file.Height,
                file.FrameRate,
                file.BitRate,
                file.Size,
                file.ModTime,
                file.Interactive,
            })
            .ToList();
        var summaries = fileRows
            .GroupBy(file => file.SceneId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    FileCount = group.Count(),
                    MaxDuration = group.Max(file => file.Duration),
                    MaxResolution = group.Max(file => Math.Max(file.Width, file.Height)),
                    MaxHeight = group.Max(file => file.Height),
                    MaxFrameRate = group.Max(file => file.FrameRate),
                    MaxBitRate = group.Max(file => file.BitRate),
                    MaxFileSize = group.Max(file => file.Size),
                    MaxFileModTime = group.Max(file => (DateTime?)file.ModTime),
                    MinPath = group.Min(file => file.Path),
                    MaxPath = group.Max(file => file.Path),
                    FileSearchText = BuildFileSearchText(group.Select(file => file.Path)),
                    HasDimensionData = group.Any(file => file.Width > 0 && file.Height > 0),
                    HasLandscapeFiles = group.Any(file => file.Width > file.Height),
                    HasPortraitFiles = group.Any(file => file.Height > file.Width),
                    HasSquareFiles = group.Any(file => file.Width > 0 && file.Width == file.Height),
                    HasInteractiveFiles = group.Any(file => file.Interactive),
                    HasNonInteractiveFiles = group.Any(file => !file.Interactive),
                });

        foreach (var scene in scenes.Values)
        {
            if (!summaries.TryGetValue(scene.Id, out var summary))
            {
                scene.FileCount = 0;
                scene.MaxDuration = 0;
                scene.MaxResolution = 0;
                scene.MaxHeight = 0;
                scene.MaxFrameRate = 0;
                scene.MaxBitRate = 0;
                scene.MaxFileSize = 0;
                scene.MaxFileModTime = null;
                scene.MinPath = null;
                scene.MaxPath = null;
                scene.FileSearchText = null;
                scene.HasDimensionData = false;
                scene.HasLandscapeFiles = false;
                scene.HasPortraitFiles = false;
                scene.HasSquareFiles = false;
                scene.HasInteractiveFiles = false;
                scene.HasNonInteractiveFiles = false;
                continue;
            }

            scene.FileCount = summary.FileCount;
            scene.MaxDuration = summary.MaxDuration;
            scene.MaxResolution = summary.MaxResolution;
            scene.MaxHeight = summary.MaxHeight;
            scene.MaxFrameRate = summary.MaxFrameRate;
            scene.MaxBitRate = summary.MaxBitRate;
            scene.MaxFileSize = summary.MaxFileSize;
            scene.MaxFileModTime = summary.MaxFileModTime;
            scene.MinPath = summary.MinPath;
            scene.MaxPath = summary.MaxPath;
            scene.FileSearchText = summary.FileSearchText;
            scene.HasDimensionData = summary.HasDimensionData;
            scene.HasLandscapeFiles = summary.HasLandscapeFiles;
            scene.HasPortraitFiles = summary.HasPortraitFiles;
            scene.HasSquareFiles = summary.HasSquareFiles;
            scene.HasInteractiveFiles = summary.HasInteractiveFiles;
            scene.HasNonInteractiveFiles = summary.HasNonInteractiveFiles;
        }
    }

    private async Task RefreshSceneMetricsAsync(HashSet<int> affectedSceneIds, CancellationToken cancellationToken)
    {
        var scenes = await Scenes.Where(BuildIdContainsPredicate<Scene>(affectedSceneIds.ToArray())).ToDictionaryAsync(scene => scene.Id, cancellationToken);
        if (scenes.Count == 0)
            return;

        var ids = scenes.Keys.ToArray();
        var fileRows = await VideoFiles.AsNoTracking()
            .Where(file => file.SceneId.HasValue && ids.Contains(file.SceneId.Value))
            .Select(file => new
            {
                SceneId = file.SceneId!.Value,
                file.Path,
                file.Duration,
                file.Width,
                file.Height,
                file.FrameRate,
                file.BitRate,
                file.Size,
                file.ModTime,
                file.Interactive,
            })
            .ToListAsync(cancellationToken);
        var summaries = fileRows
            .GroupBy(file => file.SceneId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    FileCount = group.Count(),
                    MaxDuration = group.Max(file => file.Duration),
                    MaxResolution = group.Max(file => Math.Max(file.Width, file.Height)),
                    MaxHeight = group.Max(file => file.Height),
                    MaxFrameRate = group.Max(file => file.FrameRate),
                    MaxBitRate = group.Max(file => file.BitRate),
                    MaxFileSize = group.Max(file => file.Size),
                    MaxFileModTime = group.Max(file => (DateTime?)file.ModTime),
                    MinPath = group.Min(file => file.Path),
                    MaxPath = group.Max(file => file.Path),
                    FileSearchText = BuildFileSearchText(group.Select(file => file.Path)),
                    HasDimensionData = group.Any(file => file.Width > 0 && file.Height > 0),
                    HasLandscapeFiles = group.Any(file => file.Width > file.Height),
                    HasPortraitFiles = group.Any(file => file.Height > file.Width),
                    HasSquareFiles = group.Any(file => file.Width > 0 && file.Width == file.Height),
                    HasInteractiveFiles = group.Any(file => file.Interactive),
                    HasNonInteractiveFiles = group.Any(file => !file.Interactive),
                });

        foreach (var scene in scenes.Values)
        {
            if (!summaries.TryGetValue(scene.Id, out var summary))
            {
                scene.FileCount = 0;
                scene.MaxDuration = 0;
                scene.MaxResolution = 0;
                scene.MaxHeight = 0;
                scene.MaxFrameRate = 0;
                scene.MaxBitRate = 0;
                scene.MaxFileSize = 0;
                scene.MaxFileModTime = null;
                scene.MinPath = null;
                scene.MaxPath = null;
                scene.FileSearchText = null;
                scene.HasDimensionData = false;
                scene.HasLandscapeFiles = false;
                scene.HasPortraitFiles = false;
                scene.HasSquareFiles = false;
                scene.HasInteractiveFiles = false;
                scene.HasNonInteractiveFiles = false;
                continue;
            }

            scene.FileCount = summary.FileCount;
            scene.MaxDuration = summary.MaxDuration;
            scene.MaxResolution = summary.MaxResolution;
            scene.MaxHeight = summary.MaxHeight;
            scene.MaxFrameRate = summary.MaxFrameRate;
            scene.MaxBitRate = summary.MaxBitRate;
            scene.MaxFileSize = summary.MaxFileSize;
            scene.MaxFileModTime = summary.MaxFileModTime;
            scene.MinPath = summary.MinPath;
            scene.MaxPath = summary.MaxPath;
            scene.FileSearchText = summary.FileSearchText;
            scene.HasDimensionData = summary.HasDimensionData;
            scene.HasLandscapeFiles = summary.HasLandscapeFiles;
            scene.HasPortraitFiles = summary.HasPortraitFiles;
            scene.HasSquareFiles = summary.HasSquareFiles;
            scene.HasInteractiveFiles = summary.HasInteractiveFiles;
            scene.HasNonInteractiveFiles = summary.HasNonInteractiveFiles;
        }
    }

    private void RefreshImageMetrics(HashSet<int> affectedImageIds)
    {
        var images = Images.Where(BuildIdContainsPredicate<Image>(affectedImageIds.ToArray())).ToDictionary(image => image.Id);
        if (images.Count == 0)
            return;

        var ids = images.Keys.ToArray();
        var tagCounts = Set<ImageTag>().AsNoTracking().Where(imageTag => ids.Contains(imageTag.ImageId))
            .GroupBy(imageTag => imageTag.ImageId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var performerCounts = Set<ImagePerformer>().AsNoTracking().Where(imagePerformer => ids.Contains(imagePerformer.ImageId))
            .GroupBy(imagePerformer => imagePerformer.ImageId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var galleryCounts = Set<ImageGallery>().AsNoTracking().Where(imageGallery => ids.Contains(imageGallery.ImageId))
            .GroupBy(imageGallery => imageGallery.ImageId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var fileRows = ImageFiles.AsNoTracking()
            .Where(file => file.ImageId.HasValue && ids.Contains(file.ImageId.Value))
            .Select(file => new
            {
                ImageId = file.ImageId!.Value,
                file.Path,
                file.Width,
                file.Height,
                file.Size,
                file.ModTime,
            })
            .ToList();
        var summaries = fileRows
            .GroupBy(file => file.ImageId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    FileCount = group.Count(),
                    MaxResolution = group.Max(file => Math.Max(file.Width, file.Height)),
                    MaxFileSize = group.Max(file => file.Size),
                    MaxFileModTime = group.Max(file => (DateTime?)file.ModTime),
                    MinPath = group.Min(file => file.Path),
                    MaxPath = group.Max(file => file.Path),
                    FileSearchText = BuildFileSearchText(group.Select(file => file.Path)),
                    HasDimensionData = group.Any(file => file.Width > 0 && file.Height > 0),
                    HasLandscapeFiles = group.Any(file => file.Width > file.Height),
                    HasPortraitFiles = group.Any(file => file.Height > file.Width),
                    HasSquareFiles = group.Any(file => file.Width > 0 && file.Width == file.Height),
                });

        foreach (var image in images.Values)
        {
            image.TagCount = tagCounts.GetValueOrDefault(image.Id, 0);
            image.PerformerCount = performerCounts.GetValueOrDefault(image.Id, 0);
            image.GalleryCount = galleryCounts.GetValueOrDefault(image.Id, 0);

            if (!summaries.TryGetValue(image.Id, out var summary))
            {
                image.FileCount = 0;
                image.MaxResolution = 0;
                image.MaxFileSize = 0;
                image.MaxFileModTime = null;
                image.MinPath = null;
                image.MaxPath = null;
                image.FileSearchText = null;
                image.HasDimensionData = false;
                image.HasLandscapeFiles = false;
                image.HasPortraitFiles = false;
                image.HasSquareFiles = false;
                continue;
            }

            image.FileCount = summary.FileCount;
            image.MaxResolution = summary.MaxResolution;
            image.MaxFileSize = summary.MaxFileSize;
            image.MaxFileModTime = summary.MaxFileModTime;
            image.MinPath = summary.MinPath;
            image.MaxPath = summary.MaxPath;
            image.FileSearchText = summary.FileSearchText;
            image.HasDimensionData = summary.HasDimensionData;
            image.HasLandscapeFiles = summary.HasLandscapeFiles;
            image.HasPortraitFiles = summary.HasPortraitFiles;
            image.HasSquareFiles = summary.HasSquareFiles;
        }
    }

    private async Task RefreshImageMetricsAsync(HashSet<int> affectedImageIds, CancellationToken cancellationToken)
    {
        var images = await Images.Where(BuildIdContainsPredicate<Image>(affectedImageIds.ToArray())).ToDictionaryAsync(image => image.Id, cancellationToken);
        if (images.Count == 0)
            return;

        var ids = images.Keys.ToArray();
        var tagCounts = await Set<ImageTag>().AsNoTracking().Where(imageTag => ids.Contains(imageTag.ImageId))
            .GroupBy(imageTag => imageTag.ImageId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var performerCounts = await Set<ImagePerformer>().AsNoTracking().Where(imagePerformer => ids.Contains(imagePerformer.ImageId))
            .GroupBy(imagePerformer => imagePerformer.ImageId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var galleryCounts = await Set<ImageGallery>().AsNoTracking().Where(imageGallery => ids.Contains(imageGallery.ImageId))
            .GroupBy(imageGallery => imageGallery.ImageId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var fileRows = await ImageFiles.AsNoTracking()
            .Where(file => file.ImageId.HasValue && ids.Contains(file.ImageId.Value))
            .Select(file => new
            {
                ImageId = file.ImageId!.Value,
                file.Path,
                file.Width,
                file.Height,
                file.Size,
                file.ModTime,
            })
            .ToListAsync(cancellationToken);
        var summaries = fileRows
            .GroupBy(file => file.ImageId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    FileCount = group.Count(),
                    MaxResolution = group.Max(file => Math.Max(file.Width, file.Height)),
                    MaxFileSize = group.Max(file => file.Size),
                    MaxFileModTime = group.Max(file => (DateTime?)file.ModTime),
                    MinPath = group.Min(file => file.Path),
                    MaxPath = group.Max(file => file.Path),
                    FileSearchText = BuildFileSearchText(group.Select(file => file.Path)),
                    HasDimensionData = group.Any(file => file.Width > 0 && file.Height > 0),
                    HasLandscapeFiles = group.Any(file => file.Width > file.Height),
                    HasPortraitFiles = group.Any(file => file.Height > file.Width),
                    HasSquareFiles = group.Any(file => file.Width > 0 && file.Width == file.Height),
                });

        foreach (var image in images.Values)
        {
            image.TagCount = tagCounts.GetValueOrDefault(image.Id, 0);
            image.PerformerCount = performerCounts.GetValueOrDefault(image.Id, 0);
            image.GalleryCount = galleryCounts.GetValueOrDefault(image.Id, 0);

            if (!summaries.TryGetValue(image.Id, out var summary))
            {
                image.FileCount = 0;
                image.MaxResolution = 0;
                image.MaxFileSize = 0;
                image.MaxFileModTime = null;
                image.MinPath = null;
                image.MaxPath = null;
                image.FileSearchText = null;
                image.HasDimensionData = false;
                image.HasLandscapeFiles = false;
                image.HasPortraitFiles = false;
                image.HasSquareFiles = false;
                continue;
            }

            image.FileCount = summary.FileCount;
            image.MaxResolution = summary.MaxResolution;
            image.MaxFileSize = summary.MaxFileSize;
            image.MaxFileModTime = summary.MaxFileModTime;
            image.MinPath = summary.MinPath;
            image.MaxPath = summary.MaxPath;
            image.FileSearchText = summary.FileSearchText;
            image.HasDimensionData = summary.HasDimensionData;
            image.HasLandscapeFiles = summary.HasLandscapeFiles;
            image.HasPortraitFiles = summary.HasPortraitFiles;
            image.HasSquareFiles = summary.HasSquareFiles;
        }
    }

    private void RefreshPerformerCounts(HashSet<int> affectedPerformerIds)
    {
        var performers = Performers.Where(BuildIdContainsPredicate<Performer>(affectedPerformerIds.ToArray())).ToDictionary(performer => performer.Id);
        if (performers.Count == 0)
            return;

        var ids = performers.Keys.ToArray();
        var sceneCounts = Set<ScenePerformer>().AsNoTracking().Where(scenePerformer => ids.Contains(scenePerformer.PerformerId))
            .GroupBy(scenePerformer => scenePerformer.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var imageCounts = Set<ImagePerformer>().AsNoTracking().Where(imagePerformer => ids.Contains(imagePerformer.PerformerId))
            .GroupBy(imagePerformer => imagePerformer.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var galleryCounts = Set<GalleryPerformer>().AsNoTracking().Where(galleryPerformer => ids.Contains(galleryPerformer.PerformerId))
            .GroupBy(galleryPerformer => galleryPerformer.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var tagCounts = Set<PerformerTag>().AsNoTracking().Where(performerTag => ids.Contains(performerTag.PerformerId))
            .GroupBy(performerTag => performerTag.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);

        foreach (var performer in performers.Values)
        {
            performer.SceneCount = sceneCounts.GetValueOrDefault(performer.Id, 0);
            performer.ImageCount = imageCounts.GetValueOrDefault(performer.Id, 0);
            performer.GalleryCount = galleryCounts.GetValueOrDefault(performer.Id, 0);
            performer.TagCount = tagCounts.GetValueOrDefault(performer.Id, 0);
        }
    }

    private async Task RefreshPerformerCountsAsync(HashSet<int> affectedPerformerIds, CancellationToken cancellationToken)
    {
        var performers = await Performers.Where(BuildIdContainsPredicate<Performer>(affectedPerformerIds.ToArray())).ToDictionaryAsync(performer => performer.Id, cancellationToken);
        if (performers.Count == 0)
            return;

        var ids = performers.Keys.ToArray();
        var sceneCounts = await Set<ScenePerformer>().AsNoTracking().Where(scenePerformer => ids.Contains(scenePerformer.PerformerId))
            .GroupBy(scenePerformer => scenePerformer.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var imageCounts = await Set<ImagePerformer>().AsNoTracking().Where(imagePerformer => ids.Contains(imagePerformer.PerformerId))
            .GroupBy(imagePerformer => imagePerformer.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var galleryCounts = await Set<GalleryPerformer>().AsNoTracking().Where(galleryPerformer => ids.Contains(galleryPerformer.PerformerId))
            .GroupBy(galleryPerformer => galleryPerformer.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var tagCounts = await Set<PerformerTag>().AsNoTracking().Where(performerTag => ids.Contains(performerTag.PerformerId))
            .GroupBy(performerTag => performerTag.PerformerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        foreach (var performer in performers.Values)
        {
            performer.SceneCount = sceneCounts.GetValueOrDefault(performer.Id, 0);
            performer.ImageCount = imageCounts.GetValueOrDefault(performer.Id, 0);
            performer.GalleryCount = galleryCounts.GetValueOrDefault(performer.Id, 0);
            performer.TagCount = tagCounts.GetValueOrDefault(performer.Id, 0);
        }
    }

    private void RefreshGalleryCounts(HashSet<int> affectedGalleryIds)
    {
        var galleries = Galleries.Where(BuildIdContainsPredicate<Gallery>(affectedGalleryIds.ToArray())).ToDictionary(gallery => gallery.Id);
        if (galleries.Count == 0)
            return;

        var ids = galleries.Keys.ToArray();
        var imageCounts = Set<ImageGallery>().AsNoTracking().Where(imageGallery => ids.Contains(imageGallery.GalleryId))
            .GroupBy(imageGallery => imageGallery.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var sceneCounts = Set<SceneGallery>().AsNoTracking().Where(sceneGallery => ids.Contains(sceneGallery.GalleryId))
            .GroupBy(sceneGallery => sceneGallery.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var performerCounts = Set<GalleryPerformer>().AsNoTracking().Where(galleryPerformer => ids.Contains(galleryPerformer.GalleryId))
            .GroupBy(galleryPerformer => galleryPerformer.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var tagCounts = Set<GalleryTag>().AsNoTracking().Where(galleryTag => ids.Contains(galleryTag.GalleryId))
            .GroupBy(galleryTag => galleryTag.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);

        foreach (var gallery in galleries.Values)
        {
            gallery.ImageCount = imageCounts.GetValueOrDefault(gallery.Id, 0);
            gallery.SceneCount = sceneCounts.GetValueOrDefault(gallery.Id, 0);
            gallery.PerformerCount = performerCounts.GetValueOrDefault(gallery.Id, 0);
            gallery.TagCount = tagCounts.GetValueOrDefault(gallery.Id, 0);
        }
    }

    private async Task RefreshGalleryCountsAsync(HashSet<int> affectedGalleryIds, CancellationToken cancellationToken)
    {
        var galleries = await Galleries.Where(BuildIdContainsPredicate<Gallery>(affectedGalleryIds.ToArray())).ToDictionaryAsync(gallery => gallery.Id, cancellationToken);
        if (galleries.Count == 0)
            return;

        var ids = galleries.Keys.ToArray();
        var imageCounts = await Set<ImageGallery>().AsNoTracking().Where(imageGallery => ids.Contains(imageGallery.GalleryId))
            .GroupBy(imageGallery => imageGallery.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var sceneCounts = await Set<SceneGallery>().AsNoTracking().Where(sceneGallery => ids.Contains(sceneGallery.GalleryId))
            .GroupBy(sceneGallery => sceneGallery.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var performerCounts = await Set<GalleryPerformer>().AsNoTracking().Where(galleryPerformer => ids.Contains(galleryPerformer.GalleryId))
            .GroupBy(galleryPerformer => galleryPerformer.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var tagCounts = await Set<GalleryTag>().AsNoTracking().Where(galleryTag => ids.Contains(galleryTag.GalleryId))
            .GroupBy(galleryTag => galleryTag.GalleryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        foreach (var gallery in galleries.Values)
        {
            gallery.ImageCount = imageCounts.GetValueOrDefault(gallery.Id, 0);
            gallery.SceneCount = sceneCounts.GetValueOrDefault(gallery.Id, 0);
            gallery.PerformerCount = performerCounts.GetValueOrDefault(gallery.Id, 0);
            gallery.TagCount = tagCounts.GetValueOrDefault(gallery.Id, 0);
        }
    }

    private static string? BuildFileSearchText(IEnumerable<string?> paths)
    {
        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (normalizedPaths.Length == 0)
            return null;

        return "\n" + string.Join("\n", normalizedPaths) + "\n";
    }

    private void RefreshStudioCounts(HashSet<int> affectedStudioIds)
    {
        var studios = Studios.Where(BuildIdContainsPredicate<Studio>(affectedStudioIds.ToArray())).ToDictionary(studio => studio.Id);
        if (studios.Count == 0)
            return;

        var ids = studios.Keys.ToArray();
        var sceneCounts = Scenes.AsNoTracking().Where(scene => scene.StudioId.HasValue && ids.Contains(scene.StudioId.Value))
            .GroupBy(scene => scene.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var imageCounts = Images.AsNoTracking().Where(image => image.StudioId.HasValue && ids.Contains(image.StudioId.Value))
            .GroupBy(image => image.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var galleryCounts = Galleries.AsNoTracking().Where(gallery => gallery.StudioId.HasValue && ids.Contains(gallery.StudioId.Value))
            .GroupBy(gallery => gallery.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var groupCounts = Set<Group>().AsNoTracking().Where(groupEntity => groupEntity.StudioId.HasValue && ids.Contains(groupEntity.StudioId.Value))
            .GroupBy(groupEntity => groupEntity.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var performerCounts = Set<ScenePerformer>().AsNoTracking().Where(scenePerformer => scenePerformer.Scene!.StudioId.HasValue && ids.Contains(scenePerformer.Scene.StudioId.Value))
            .GroupBy(scenePerformer => scenePerformer.Scene!.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Select(scenePerformer => scenePerformer.PerformerId).Distinct().Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var childCounts = Studios.AsNoTracking().Where(studio => studio.ParentId.HasValue && ids.Contains(studio.ParentId.Value))
            .GroupBy(studio => studio.ParentId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var tagCounts = Set<StudioTag>().AsNoTracking().Where(studioTag => ids.Contains(studioTag.StudioId))
            .GroupBy(studioTag => studioTag.StudioId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionary(x => x.Key, x => x.Count);

        foreach (var studio in studios.Values)
        {
            studio.SceneCount = sceneCounts.GetValueOrDefault(studio.Id, 0);
            studio.ImageCount = imageCounts.GetValueOrDefault(studio.Id, 0);
            studio.GalleryCount = galleryCounts.GetValueOrDefault(studio.Id, 0);
            studio.GroupCount = groupCounts.GetValueOrDefault(studio.Id, 0);
            studio.PerformerCount = performerCounts.GetValueOrDefault(studio.Id, 0);
            studio.ChildStudioCount = childCounts.GetValueOrDefault(studio.Id, 0);
            studio.TagCount = tagCounts.GetValueOrDefault(studio.Id, 0);
        }
    }

    private async Task RefreshStudioCountsAsync(HashSet<int> affectedStudioIds, CancellationToken cancellationToken)
    {
        var studios = await Studios.Where(BuildIdContainsPredicate<Studio>(affectedStudioIds.ToArray())).ToDictionaryAsync(studio => studio.Id, cancellationToken);
        if (studios.Count == 0)
            return;

        var ids = studios.Keys.ToArray();
        var sceneCounts = await Scenes.AsNoTracking().Where(scene => scene.StudioId.HasValue && ids.Contains(scene.StudioId.Value))
            .GroupBy(scene => scene.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var imageCounts = await Images.AsNoTracking().Where(image => image.StudioId.HasValue && ids.Contains(image.StudioId.Value))
            .GroupBy(image => image.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var galleryCounts = await Galleries.AsNoTracking().Where(gallery => gallery.StudioId.HasValue && ids.Contains(gallery.StudioId.Value))
            .GroupBy(gallery => gallery.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var groupCounts = await Set<Group>().AsNoTracking().Where(groupEntity => groupEntity.StudioId.HasValue && ids.Contains(groupEntity.StudioId.Value))
            .GroupBy(groupEntity => groupEntity.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var performerCounts = await Set<ScenePerformer>().AsNoTracking().Where(scenePerformer => scenePerformer.Scene!.StudioId.HasValue && ids.Contains(scenePerformer.Scene.StudioId.Value))
            .GroupBy(scenePerformer => scenePerformer.Scene!.StudioId!.Value)
            .Select(group => new { group.Key, Count = group.Select(scenePerformer => scenePerformer.PerformerId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var childCounts = await Studios.AsNoTracking().Where(studio => studio.ParentId.HasValue && ids.Contains(studio.ParentId.Value))
            .GroupBy(studio => studio.ParentId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var tagCounts = await Set<StudioTag>().AsNoTracking().Where(studioTag => ids.Contains(studioTag.StudioId))
            .GroupBy(studioTag => studioTag.StudioId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        foreach (var studio in studios.Values)
        {
            studio.SceneCount = sceneCounts.GetValueOrDefault(studio.Id, 0);
            studio.ImageCount = imageCounts.GetValueOrDefault(studio.Id, 0);
            studio.GalleryCount = galleryCounts.GetValueOrDefault(studio.Id, 0);
            studio.GroupCount = groupCounts.GetValueOrDefault(studio.Id, 0);
            studio.PerformerCount = performerCounts.GetValueOrDefault(studio.Id, 0);
            studio.ChildStudioCount = childCounts.GetValueOrDefault(studio.Id, 0);
            studio.TagCount = tagCounts.GetValueOrDefault(studio.Id, 0);
        }
    }

    private static void CollectChangedIntKey<TEntity>(HashSet<int> ids, IEnumerable<EntityEntry<TEntity>> entries, Func<TEntity, int> currentSelector, string propertyName)
        where TEntity : class
    {
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            AddIfPositive(ids, currentSelector(entry.Entity));
            AddIfPositive(ids, entry.Property<int>(propertyName).OriginalValue);
        }
    }

    private static void CollectChangedNullableIntKey<TEntity>(HashSet<int> ids, IEnumerable<EntityEntry<TEntity>> entries, Func<TEntity, int?> currentSelector, string propertyName)
        where TEntity : class
    {
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            AddIfPositive(ids, currentSelector(entry.Entity));
            AddIfPositive(ids, entry.Property<int?>(propertyName).OriginalValue);
        }
    }

    private static void AddRelatedIdsFromDeletedParents(HashSet<int> ids, int[] deletedParentIds, Func<int[], IQueryable<int>> queryBuilder)
    {
        if (deletedParentIds.Length == 0)
            return;

        foreach (var tagId in queryBuilder(deletedParentIds).ToList())
            AddIfPositive(ids, tagId);
    }

    private static void AddIfPositive(HashSet<int> ids, int? value)
    {
        if (value is > 0)
            ids.Add(value.Value);
    }

    private HashSet<int> CollectChangedParentIds<TLink>(Func<TLink, int> parentId) where TLink : class
    {
        var ids = new HashSet<int>();
        foreach (var entry in ChangeTracker.Entries<TLink>())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
            {
                var id = parentId(entry.Entity);
                if (id > 0)
                    ids.Add(id);
            }
        }
        return ids;
    }

    private void InitializeAddedParentIdArrays()
    {
        foreach (var entry in ChangeTracker.Entries<Scene>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TagIds = entry.Entity.SceneTags
                .Select(sceneTag => sceneTag.TagId)
                .Where(tagId => tagId > 0)
                .Distinct()
                .OrderBy(tagId => tagId)
                .ToArray();
            entry.Entity.PerformerIds = entry.Entity.ScenePerformers
                .Select(scenePerformer => scenePerformer.PerformerId)
                .Where(performerId => performerId > 0)
                .Distinct()
                .OrderBy(performerId => performerId)
                .ToArray();
        }

        foreach (var entry in ChangeTracker.Entries<Image>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TagIds = entry.Entity.ImageTags
                .Select(imageTag => imageTag.TagId)
                .Where(tagId => tagId > 0)
                .Distinct()
                .OrderBy(tagId => tagId)
                .ToArray();
            entry.Entity.PerformerIds = entry.Entity.ImagePerformers
                .Select(imagePerformer => imagePerformer.PerformerId)
                .Where(performerId => performerId > 0)
                .Distinct()
                .OrderBy(performerId => performerId)
                .ToArray();
        }

        foreach (var entry in ChangeTracker.Entries<Gallery>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TagIds = entry.Entity.GalleryTags
                .Select(galleryTag => galleryTag.TagId)
                .Where(tagId => tagId > 0)
                .Distinct()
                .OrderBy(tagId => tagId)
                .ToArray();
            entry.Entity.PerformerIds = entry.Entity.GalleryPerformers
                .Select(galleryPerformer => galleryPerformer.PerformerId)
                .Where(performerId => performerId > 0)
                .Distinct()
                .OrderBy(performerId => performerId)
                .ToArray();
        }
    }

    private void RebuildArray<TParent, TLink>(
        HashSet<int> parentIds,
        System.Linq.Expressions.Expression<Func<TParent, int[]>> arrayProp,
        Expression<Func<TLink, int>> linkParentId,
        Expression<Func<TLink, int>> linkChildId)
        where TParent : class
        where TLink : class
    {
        // Build the new id-set per parent from the post-save state of the join table.
        // Use the change tracker to overlay pending Added/Deleted link rows on top of
        // whatever's in the database, so the array reflects the unit of work being saved
        // (NOT the pre-save DB state) and SaveChanges only does one INSERT/UPDATE pass.
        var ids = parentIds.ToArray();
        var linkParentFn = linkParentId.Compile();
        var linkChildFn = linkChildId.Compile();

        // Start from the DB rows for these parents.
        var dbLinks = Set<TLink>().AsNoTracking()
            .Where(BuildContainsPredicate(linkParentId, ids))
            .Select(link => new { Parent = linkParentFn(link), Child = linkChildFn(link) })
            .ToList();

        var byParent = new Dictionary<int, HashSet<int>>(parentIds.Count);
        foreach (var pid in parentIds)
            byParent[pid] = new HashSet<int>();
        foreach (var row in dbLinks)
        {
            if (byParent.TryGetValue(row.Parent, out var set))
                set.Add(row.Child);
        }

        // Overlay change tracker mutations on top of the DB snapshot.
        foreach (var entry in ChangeTracker.Entries<TLink>())
        {
            var pid = linkParentFn(entry.Entity);
            if (!byParent.TryGetValue(pid, out var set)) continue;
            var cid = linkChildFn(entry.Entity);
            switch (entry.State)
            {
                case EntityState.Added: set.Add(cid); break;
                case EntityState.Deleted: set.Remove(cid); break;
                // Modified on a composite-key link table is rare; treat as add.
                case EntityState.Modified: set.Add(cid); break;
            }
        }

        // Locate or load each parent and assign the new array.
        var arraySetter = BuildArraySetter(arrayProp);
        var trackedParents = ChangeTracker.Entries<TParent>()
            .Where(e => e.State != EntityState.Deleted)
            .ToDictionary(e => GetEntityId(e.Entity), e => e.Entity);

        var missingParentIds = parentIds.Where(pid => !trackedParents.ContainsKey(pid)).ToArray();
        var loadedParents = missingParentIds.Length > 0
            ? Set<TParent>().Where(BuildIdContainsPredicate<TParent>(missingParentIds)).ToList()
            : new List<TParent>();

        foreach (var parent in loadedParents)
            trackedParents[GetEntityId(parent)] = parent;

        foreach (var (pid, set) in byParent)
        {
            if (!trackedParents.TryGetValue(pid, out var parent)) continue;
            // Order for stable diffs and predictable serialization.
            var newArray = set.OrderBy(x => x).ToArray();
            arraySetter(parent, newArray);
        }
    }

    private static Expression<Func<TLink, bool>> BuildContainsPredicate<TLink>(
        Expression<Func<TLink, int>> selector, int[] ids)
    {
        var param = selector.Parameters[0];
        var contains = Expression.Call(
            typeof(System.Linq.Enumerable),
            nameof(System.Linq.Enumerable.Contains),
            new[] { typeof(int) },
            Expression.Constant(ids),
            selector.Body);
        return Expression.Lambda<Func<TLink, bool>>(contains, param);
    }

    private static Expression<Func<TParent, bool>> BuildIdContainsPredicate<TParent>(int[] ids) where TParent : class
    {
        var param = Expression.Parameter(typeof(TParent), "p");
        var idProperty = Expression.Property(param, nameof(BaseEntity.Id));
        var contains = Expression.Call(
            typeof(System.Linq.Enumerable),
            nameof(System.Linq.Enumerable.Contains),
            new[] { typeof(int) },
            Expression.Constant(ids),
            idProperty);
        return Expression.Lambda<Func<TParent, bool>>(contains, param);
    }

    private static int GetEntityId(object entity)
    {
        return entity switch
        {
            BaseEntity be => be.Id,
            _ => (int)(entity.GetType().GetProperty("Id")?.GetValue(entity) ?? 0)
        };
    }

    private static Action<TParent, int[]> BuildArraySetter<TParent>(Expression<Func<TParent, int[]>> arrayProp)
    {
        var memberExpr = (MemberExpression)arrayProp.Body;
        var prop = (System.Reflection.PropertyInfo)memberExpr.Member;
        return (parent, value) => prop.SetValue(parent, value);
    }

    private void ComputeFilePaths()
    {
        // Normalize any Added/Modified Folder.Path to forward-slash form so callers can
        // compare/sort/filter on the column directly without per-row REPLACE.
        foreach (var folderEntry in ChangeTracker.Entries<Folder>())
        {
            if (folderEntry.State != EntityState.Added && folderEntry.State != EntityState.Modified)
                continue;
            var folder = folderEntry.Entity;
            if (string.IsNullOrEmpty(folder.Path)) continue;
            var normalized = folder.Path.Replace('\\', '/');
            if (!ReferenceEquals(normalized, folder.Path) && normalized != folder.Path)
                folder.Path = normalized;
        }

        // Collect Added/Modified files whose denormalized Path needs to be (re)computed.
        var fileEntries = ChangeTracker.Entries<BaseFileEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .ToList();

        if (fileEntries.Count == 0)
        {
            CascadeFolderPathChanges();
            return;
        }

        // Build folder-path lookup. Prefer the in-memory navigation; for any file whose
        // ParentFolder navigation is null, batch-load just the folder paths we need.
        var folderPaths = new Dictionary<int, string>();
        var missingFolderIds = new HashSet<int>();
        foreach (var entry in fileEntries)
        {
            var file = entry.Entity;
            if (file.ParentFolder != null)
                folderPaths[file.ParentFolderId] = file.ParentFolder.Path;
            else if (file.ParentFolderId != 0 && !folderPaths.ContainsKey(file.ParentFolderId))
                missingFolderIds.Add(file.ParentFolderId);
        }

        if (missingFolderIds.Count > 0)
        {
            var ids = missingFolderIds.ToArray();
            var loaded = Folders
                .Where(f => ids.Contains(f.Id))
                .Select(f => new { f.Id, f.Path })
                .ToList();
            foreach (var f in loaded)
                folderPaths[f.Id] = f.Path;
        }

        foreach (var entry in fileEntries)
        {
            var file = entry.Entity;
            folderPaths.TryGetValue(file.ParentFolderId, out var folderPath);
            file.Path = BaseFileEntity.ComputePath(folderPath, file.Basename);
        }

        CascadeFolderPathChanges();
    }

    private void CascadeFolderPathChanges()
    {
        // When a Folder.Path is renamed, every child file's denormalized Path needs to
        // be refreshed. We update any tracked child files; folder renames at runtime
        // are rare today and untracked children should be migrated by an explicit job
        // when that feature is added.
        var folderEntries = ChangeTracker.Entries<Folder>()
            .Where(e => e.State == EntityState.Modified
                && e.Property(nameof(Folder.Path)).IsModified)
            .ToList();
        if (folderEntries.Count == 0) return;

        foreach (var entry in folderEntries)
        {
            var folder = entry.Entity;
            foreach (var fileEntry in ChangeTracker.Entries<BaseFileEntity>())
            {
                var file = fileEntry.Entity;
                if (file.ParentFolderId != folder.Id) continue;
                file.Path = BaseFileEntity.ComputePath(folder.Path, file.Basename);
                if (fileEntry.State == EntityState.Unchanged)
                    fileEntry.State = EntityState.Modified;
            }
        }
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.Entity is BaseEntity entity)
            {
                if (entry.State == EntityState.Added)
                {
                    if (entity.CreatedAt == default)
                        entity.CreatedAt = now;
                    if (entity.UpdatedAt == default)
                        entity.UpdatedAt = entity.CreatedAt;
                }
                else
                {
                    entity.UpdatedAt = now;
                }
            }
            else if (entry.Entity is BaseFileEntity file)
            {
                if (entry.State == EntityState.Added)
                {
                    if (file.CreatedAt == default)
                        file.CreatedAt = now;
                    if (file.UpdatedAt == default)
                        file.UpdatedAt = file.CreatedAt;
                }
                else
                {
                    file.UpdatedAt = now;
                }
            }
            else if (entry.Entity is Folder folder)
            {
                if (entry.State == EntityState.Added)
                {
                    if (folder.CreatedAt == default)
                        folder.CreatedAt = now;
                    if (folder.UpdatedAt == default)
                        folder.UpdatedAt = folder.CreatedAt;
                }
                else
                {
                    folder.UpdatedAt = now;
                }
            }
        }
    }
}

