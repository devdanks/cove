using Microsoft.EntityFrameworkCore;
using Cove.Core.Entities;
using Cove.Core.Entities.Auth;
using Cove.Plugins;

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
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        ComputeFilePaths();
        return base.SaveChangesAsync(cancellationToken);
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
