using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
using Cove.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using GalleriesController = Cove.Api.Controllers.GalleriesController;
using ScenesController = Cove.Api.Controllers.ScenesController;

namespace Cove.Tests;

public sealed class Phase10TagProvenanceTests
{
    [Fact]
    public async Task ScenesController_GetById_IncludesProvenance()
    {
        await using var context = CreateContext();

        var tag = new Tag { Name = "Detected" };
        var scene = new Scene { Title = "Scene with provenance" };
        scene.SceneTags.Add(new SceneTag { Scene = scene, Tag = tag });

        context.AddRange(tag, scene);
        await context.SaveChangesAsync();

        context.TagApplications.Add(new TagApplication
        {
            HostType = AffinityHostType.Scene,
            HostId = scene.Id,
            TagId = tag.Id,
            SourceKey = "ext:ai.tagging",
            SourceRunId = "run-scene",
            ModelKey = "model-scene",
            Confidence = 0.82f,
        });
        await context.SaveChangesAsync();

        var controller = new ScenesController(
            new SceneRepository(context),
            context,
            null!,
            null!,
            null!,
            new MemoryCache(new MemoryCacheOptions()),
            null!,
            null!,
            null!,
            new NoOpUserEngagementService(),
            new TagProvenanceService(context));

        var result = await controller.GetById(scene.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<SceneDto>(ok.Value);
        var dtoTag = Assert.Single(dto.Tags);
        var provenance = Assert.Single(dtoTag.Provenance!);

        Assert.Equal("ext:ai.tagging", provenance.SourceKey);
        Assert.Equal("run-scene", provenance.SourceRunId);
        Assert.Equal("model-scene", provenance.ModelKey);
        Assert.Equal(0.82f, provenance.Confidence);
    }

    [Fact]
    public async Task GalleriesController_GetById_IncludesProvenance()
    {
        await using var context = CreateContext();

        var tag = new Tag { Name = "Generated" };
        var gallery = new Gallery { Title = "Gallery with provenance" };
        gallery.GalleryTags.Add(new GalleryTag { Gallery = gallery, Tag = tag });

        context.AddRange(tag, gallery);
        await context.SaveChangesAsync();

        context.TagApplications.Add(new TagApplication
        {
            HostType = AffinityHostType.Gallery,
            HostId = gallery.Id,
            TagId = tag.Id,
            SourceKey = "ext:ai.tagging",
            SourceRunId = "run-gallery",
            ModelKey = "model-gallery",
            Confidence = 0.67f,
        });
        await context.SaveChangesAsync();

        var controller = new GalleriesController(
            new GalleryRepository(context),
            context,
            new TagProvenanceService(context));

        var result = await controller.GetById(gallery.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<GalleryDto>(ok.Value);
        var dtoTag = Assert.Single(dto.Tags);
        var provenance = Assert.Single(dtoTag.Provenance!);

        Assert.Equal("ext:ai.tagging", provenance.SourceKey);
        Assert.Equal("run-gallery", provenance.SourceRunId);
        Assert.Equal("model-gallery", provenance.ModelKey);
        Assert.Equal(0.67f, provenance.Confidence);
    }

    [Fact]
    public async Task StartAutoTag_RecordsGalleryTagProvenance()
    {
        await using var environment = await CreateAutoTagEnvironmentAsync();
        await SeedAutoTagLibraryContentAsync(environment.Services);
        var provenanceRecorder = new RecordingTagProvenanceService();

        var service = new AutoTagService(
            environment.JobService,
            environment.Services.GetRequiredService<IServiceScopeFactory>(),
            new ExtensionManager(new ExtensionContext
            {
                Configuration = new ConfigurationBuilder().Build(),
                DataDirectory = Path.GetTempPath(),
                CoveVersion = "test",
            }),
            provenanceRecorder,
            NullLogger<AutoTagService>.Instance);

        service.StartAutoTag();

        Assert.Contains(provenanceRecorder.RecordCalls, call => call.HostType == AffinityHostType.Gallery && call.SourceKey == "system");
    }

    private static CoveContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"phase10-tag-provenance-{Guid.NewGuid():N}")
            .Options;

        return new TestCoveContext(options);
    }

    private static async Task<AutoTagTestEnvironment> CreateAutoTagEnvironmentAsync()
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

        return new AutoTagTestEnvironment(provider, connection, new ImmediateJobService());
    }

    private static async Task SeedAutoTagLibraryContentAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CoveContext>();

        var performer = new Performer { Name = "Alice" };
        var studio = new Studio { Name = "Acme" };
        var tag = new Tag { Name = "Summer" };

        var sceneFolder = new Folder { Path = Path.Combine("C:\\library", "Acme Alice Summer"), ModTime = DateTime.UtcNow };
        var imageFolder = new Folder { Path = Path.Combine("C:\\library", "Acme Alice Summer", "images"), ModTime = DateTime.UtcNow };
        var galleryFolder = new Folder { Path = Path.Combine("C:\\library", "Acme Alice Summer", "gallery"), ModTime = DateTime.UtcNow };

        var scene = new Scene { Title = "Alice showcase" };
        scene.Files.Add(new VideoFile { Basename = "alice-summer-scene.mp4", ParentFolder = sceneFolder, ModTime = DateTime.UtcNow });

        var image = new Image { Title = "Alice still" };
        image.Files.Add(new ImageFile { Basename = "alice-summer-image.jpg", ParentFolder = imageFolder, ModTime = DateTime.UtcNow });

        var gallery = new Gallery { Title = "Alice gallery" };
        gallery.Files.Add(new GalleryFile { Basename = "alice-summer-gallery.zip", ParentFolder = galleryFolder, ModTime = DateTime.UtcNow });

        context.AddRange(performer, studio, tag, sceneFolder, imageFolder, galleryFolder, scene, image, gallery);
        await context.SaveChangesAsync();
    }

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Scene>().Ignore(scene => scene.CustomFields);
            modelBuilder.Entity<Image>().Ignore(image => image.CustomFields);
            modelBuilder.Entity<Tag>().Ignore(tag => tag.CustomFields);
            modelBuilder.Entity<Studio>().Ignore(studio => studio.CustomFields);
            modelBuilder.Entity<Performer>().Ignore(performer => performer.CustomFields);
            modelBuilder.Entity<Gallery>().Ignore(gallery => gallery.CustomFields);
            modelBuilder.Entity<Group>().Ignore(group => group.CustomFields);
            modelBuilder.Entity<Face>().Ignore(face => face.CustomFields);
        }
    }

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

    private sealed class AutoTagTestEnvironment(ServiceProvider services, SqliteConnection connection, ImmediateJobService jobService) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;
        public ImmediateJobService JobService { get; } = jobService;

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await connection.DisposeAsync();
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

        public Cove.Core.Interfaces.JobInfo? GetJob(string jobId) => null;

        public IReadOnlyList<Cove.Core.Interfaces.JobInfo> GetAllJobs() => [];

        public IReadOnlyList<Cove.Core.Interfaces.JobInfo> GetJobHistory() => [];
    }

    private sealed class ImmediateJobProgress : Cove.Core.Interfaces.IJobProgress
    {
        public void Report(double progress, string? subTask = null)
        {
        }
    }

    private sealed class RecordingTagProvenanceService : ITagProvenanceService
    {
        public List<(AffinityHostType HostType, int HostId, int TagId, string SourceKey)> RecordCalls { get; } = [];

        public Task RecordAsync(AffinityHostType hostType, int hostId, int tagId, string sourceKey, string? sourceRunId = null, string? modelKey = null, float? confidence = null, CancellationToken cancellationToken = default)
        {
            RecordCalls.Add((hostType, hostId, tagId, sourceKey));
            return Task.CompletedTask;
        }

        public Task RecordAsync(AffinityHostType hostType, int hostId, Tag tag, string sourceKey, string? sourceRunId = null, string? modelKey = null, float? confidence = null, CancellationToken cancellationToken = default)
            => RecordAsync(hostType, hostId, tag.Id, sourceKey, sourceRunId, modelKey, confidence, cancellationToken);

        public Task SyncTagSetAsync(AffinityHostType hostType, int hostId, IReadOnlyCollection<int> previousTagIds, IReadOnlyCollection<int> currentTagIds, string sourceKey = "user", CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveForHostAsync(AffinityHostType hostType, int hostId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyDictionary<int, List<TagProvenanceDto>>> GetLookupAsync(AffinityHostType hostType, int hostId, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, List<TagProvenanceDto>>>(new Dictionary<int, List<TagProvenanceDto>>());
    }
}