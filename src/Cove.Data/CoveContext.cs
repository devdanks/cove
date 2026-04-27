using Microsoft.EntityFrameworkCore;
using Cove.Core.Entities;
using Cove.Core.Entities.Auth;
using Cove.Plugins;
using System.Linq.Expressions;

namespace Cove.Data;

public class CoveContext : DbContext
{
    private static IReadOnlyList<IDataExtension> _dataExtensions = [];

    public static void SetDataExtensions(IEnumerable<IDataExtension> extensions)
    {
        _dataExtensions = extensions.ToList();
    }

    public CoveContext(DbContextOptions<CoveContext> options) : base(options) { }

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

        foreach (var ext in _dataExtensions)
        {
            ext.ConfigureModel(modelBuilder);
        }
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        ComputeFilePaths();
        MaintainDenormalizedIdArrays();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        ComputeFilePaths();
        MaintainDenormalizedIdArrays();
        return base.SaveChangesAsync(cancellationToken);
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

    private HashSet<int> CollectChangedParentIds<TLink>(Func<TLink, int> parentId) where TLink : class
    {
        var ids = new HashSet<int>();
        foreach (var entry in ChangeTracker.Entries<TLink>())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
                ids.Add(parentId(entry.Entity));
        }
        return ids;
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
