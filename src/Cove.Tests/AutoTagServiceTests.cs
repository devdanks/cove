using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Events;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Plugins;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public class AutoTagServiceTests
{
    [Fact]
    public async Task StartAutoTag_HonorsIgnoreFlagsAcrossLibraryContent()
    {
        await using var environment = await CreateEnvironmentAsync();
        await SeedLibraryContentAsync(environment.Services, includeIgnoredNamesInPaths: true);

        var service = CreateService(environment.Services, environment.JobService);
        service.StartAutoTag();

        await using var verificationScope = environment.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<CoveContext>();

        var scene = await context.Scenes.Include(item => item.ScenePerformers).Include(item => item.SceneTags).SingleAsync();
        var image = await context.Images.Include(item => item.ImagePerformers).Include(item => item.ImageTags).SingleAsync();
        var gallery = await context.Galleries.Include(item => item.GalleryPerformers).Include(item => item.GalleryTags).SingleAsync();
        var performerIds = await context.Performers.ToDictionaryAsync(item => item.Name, item => item.Id);
        var studioIds = await context.Studios.ToDictionaryAsync(item => item.Name, item => item.Id);
        var tagIds = await context.Tags.ToDictionaryAsync(item => item.Name, item => item.Id);

        Assert.Equal(studioIds["Acme"], scene.StudioId);
        Assert.Equal(studioIds["Acme"], image.StudioId);
        Assert.Equal(studioIds["Acme"], gallery.StudioId);

        Assert.Contains(scene.ScenePerformers, item => item.PerformerId == performerIds["Alice"]);
        Assert.Contains(image.ImagePerformers, item => item.PerformerId == performerIds["Alice"]);
        Assert.Contains(gallery.GalleryPerformers, item => item.PerformerId == performerIds["Alice"]);

        Assert.DoesNotContain(scene.ScenePerformers, item => item.PerformerId == performerIds["Bob"]);
        Assert.DoesNotContain(image.ImagePerformers, item => item.PerformerId == performerIds["Bob"]);
        Assert.DoesNotContain(gallery.GalleryPerformers, item => item.PerformerId == performerIds["Bob"]);

        Assert.Contains(scene.SceneTags, item => item.TagId == tagIds["Summer"]);
        Assert.Contains(image.ImageTags, item => item.TagId == tagIds["Summer"]);
        Assert.Contains(gallery.GalleryTags, item => item.TagId == tagIds["Summer"]);

        Assert.DoesNotContain(scene.SceneTags, item => item.TagId == tagIds["Hidden"]);
        Assert.DoesNotContain(image.ImageTags, item => item.TagId == tagIds["Hidden"]);
        Assert.DoesNotContain(gallery.GalleryTags, item => item.TagId == tagIds["Hidden"]);

        Assert.NotEqual(studioIds["Hidden"], scene.StudioId);
        Assert.NotEqual(studioIds["Hidden"], image.StudioId);
        Assert.NotEqual(studioIds["Hidden"], gallery.StudioId);
    }

    [Fact]
    public async Task StartAutoTag_UsesSelectorFilteringAndWholeWordMatching()
    {
        await using var environment = await CreateEnvironmentAsync();
        var seeded = await SeedLibraryContentAsync(environment.Services, includeIgnoredNamesInPaths: false, includeJoanneOnlyPath: true);

        var service = CreateService(environment.Services, environment.JobService);
        service.StartAutoTag(new[] { "Alice", seeded.AnnId.ToString() }, new[] { seeded.AcmeId.ToString() }, new[] { "Summer" });

        await using var verificationScope = environment.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<CoveContext>();
        var scene = await context.Scenes.Include(item => item.ScenePerformers).Include(item => item.SceneTags).SingleAsync();
        var performerIds = await context.Performers.ToDictionaryAsync(item => item.Name, item => item.Id);
        var tagIds = await context.Tags.ToDictionaryAsync(item => item.Name, item => item.Id);

        Assert.Equal(seeded.AcmeId, scene.StudioId);
        Assert.Contains(scene.ScenePerformers, item => item.PerformerId == performerIds["Alice"]);
        Assert.DoesNotContain(scene.ScenePerformers, item => item.PerformerId == performerIds["Ann"]);
        Assert.DoesNotContain(scene.ScenePerformers, item => item.PerformerId == performerIds["Charlie"]);
        Assert.Contains(scene.SceneTags, item => item.TagId == tagIds["Summer"]);
    }

    private static AutoTagService CreateService(IServiceProvider services, ImmediateJobService jobService)
    {
        var extensionManager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = Path.GetTempPath(),
            CoveVersion = "test",
        });

        return new AutoTagService(
            jobService,
            services.GetRequiredService<IServiceScopeFactory>(),
            extensionManager,
            NoOpTagProvenanceService.Instance,
            NullLogger<AutoTagService>.Instance);
    }

    private static async Task<SeededIds> SeedLibraryContentAsync(IServiceProvider services, bool includeIgnoredNamesInPaths, bool includeJoanneOnlyPath = false)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CoveContext>();

        var alice = new Performer { Name = "Alice" };
        var bob = new Performer { Name = "Bob", IgnoreAutoTag = true };
        var ann = new Performer { Name = "Ann" };
        var charlie = new Performer { Name = "Charlie" };
        var acme = new Studio { Name = "Acme" };
        var hiddenStudio = new Studio { Name = "Hidden", IgnoreAutoTag = true };
        var summer = new Tag { Name = "Summer" };
        var hiddenTag = new Tag { Name = "Hidden", IgnoreAutoTag = true };

        var pathParts = new List<string> { "Acme", "Alice", "Summer" };
        if (includeIgnoredNamesInPaths)
        {
            pathParts.Add("Bob");
            pathParts.Add("Hidden");
        }

        if (includeJoanneOnlyPath)
            pathParts.Add("Joanne");
        else
            pathParts.Add("Charlie");

        var folderName = string.Join(' ', pathParts);
        var sceneFolder = new Folder { Path = Path.Combine("C:\\library", folderName), ModTime = DateTime.UtcNow };
        var imageFolder = new Folder { Path = Path.Combine("C:\\library", folderName, "images"), ModTime = DateTime.UtcNow };
        var galleryFolder = new Folder { Path = Path.Combine("C:\\library", folderName, "gallery"), ModTime = DateTime.UtcNow };

        var scene = new Scene { Title = includeJoanneOnlyPath ? "Joanne showcase" : "Alice Charlie showcase" };
        scene.Files.Add(new VideoFile { Basename = "alice-summer-scene.mp4", ParentFolder = sceneFolder, ModTime = DateTime.UtcNow });

        var image = new Image { Title = "Alice Summer still" };
        image.Files.Add(new ImageFile { Basename = "alice-summer-image.jpg", ParentFolder = imageFolder, ModTime = DateTime.UtcNow });

        var gallery = new Gallery { Title = "Alice Summer gallery" };
        gallery.Files.Add(new GalleryFile { Basename = "alice-summer-gallery.zip", ParentFolder = galleryFolder, ModTime = DateTime.UtcNow });

        context.AddRange(alice, bob, ann, charlie, acme, hiddenStudio, summer, hiddenTag, sceneFolder, imageFolder, galleryFolder, scene, image, gallery);
        await context.SaveChangesAsync();

        return new SeededIds(acme.Id, ann.Id);
    }

    private static async Task<TestEnvironment> CreateEnvironmentAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;
        services.AddScoped<CoveContext>(_ => new AutoTagTestContext(options));

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CoveContext>();
        await context.Database.EnsureCreatedAsync();

        return new TestEnvironment(provider, connection, new ImmediateJobService());
    }

    private sealed record SeededIds(int AcmeId, int AnnId);

    private sealed class AutoTagTestContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Scene>().Ignore(scene => scene.CustomFields);
            modelBuilder.Entity<Performer>().Ignore(performer => performer.CustomFields);
            modelBuilder.Entity<Tag>().Ignore(tag => tag.CustomFields);
            modelBuilder.Entity<Studio>().Ignore(studio => studio.CustomFields);
            modelBuilder.Entity<Gallery>().Ignore(gallery => gallery.CustomFields);
            modelBuilder.Entity<Image>().Ignore(image => image.CustomFields);
            modelBuilder.Entity<Group>().Ignore(group => group.CustomFields);
        }
    }

    private sealed class ImmediateJobService : IJobService
    {
        private int _nextId;

        public string Enqueue(string type, string description, Func<Cove.Core.Interfaces.IJobProgress, CancellationToken, Task> work, bool exclusive = true)
        {
            work(new ImmediateJobProgress(), CancellationToken.None).GetAwaiter().GetResult();
            return $"job-{Interlocked.Increment(ref _nextId)}";
        }

        public bool Cancel(string jobId) => false;

        public JobInfo? GetJob(string jobId) => null;

        public IReadOnlyList<JobInfo> GetAllJobs() => [];

        public IReadOnlyList<JobInfo> GetJobHistory() => [];
    }

    private sealed class ImmediateJobProgress : Cove.Core.Interfaces.IJobProgress
    {
        public void Report(double progress, string? subTask = null)
        {
        }
    }

    private sealed class TestEnvironment(ServiceProvider services, SqliteConnection connection, ImmediateJobService jobService) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;
        public ImmediateJobService JobService { get; } = jobService;

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NoOpTagProvenanceService : ITagProvenanceService
    {
        public static NoOpTagProvenanceService Instance { get; } = new();

        public Task RecordAsync(AffinityHostType hostType, int hostId, int tagId, string sourceKey, string? sourceRunId = null, string? modelKey = null, float? confidence = null, string? contextType = null, int? contextId = null, double? totalDurationSec = null, double? hostDurationSec = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordAsync(AffinityHostType hostType, int hostId, Tag tag, string sourceKey, string? sourceRunId = null, string? modelKey = null, float? confidence = null, string? contextType = null, int? contextId = null, double? totalDurationSec = null, double? hostDurationSec = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncTagSetAsync(AffinityHostType hostType, int hostId, IReadOnlyCollection<int> previousTagIds, IReadOnlyCollection<int> currentTagIds, string sourceKey = "user", CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveForHostAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyDictionary<int, List<TagProvenanceDto>>> GetLookupAsync(AffinityHostType hostType, int hostId, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, List<TagProvenanceDto>>>(new Dictionary<int, List<TagProvenanceDto>>());
    }
}