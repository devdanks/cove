using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;

namespace Cove.Tests;

public class PerformerScrapeServiceTests
{
    [Fact]
    public async Task ApplyAsync_MergesPerformerFieldsAndCreatesMissingTags()
    {
        await using var context = CreateContext();
        var performer = new Performer { Name = "Original Name" };
        context.Performers.Add(performer);
        await context.SaveChangesAsync();

        var service = new PerformerScrapeService(context, null!);
        var scraped = new ScrapedPerformerDto
        {
            Name = "Updated Name",
            Country = "US",
            Details = "Imported biography",
            Urls = ["https://site.example/models/updated-name"],
            Aliases = ["Alt Name"],
            TagNames = ["Tag One", "Tag Two"],
        };

        await service.ApplyAsync(performer, scraped, createMissingTags: true);
        await context.SaveChangesAsync();

        var updated = await context.Performers
            .Include(item => item.Urls)
            .Include(item => item.Aliases)
            .Include(item => item.PerformerTags)
            .ThenInclude(item => item.Tag)
            .SingleAsync();

        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("US", updated.Country);
        Assert.Equal("Imported biography", updated.Details);
        Assert.Contains(updated.Urls, item => item.Url == "https://site.example/models/updated-name");
        Assert.Contains(updated.Aliases, item => item.Alias == "Alt Name");
        Assert.Equal(2, updated.PerformerTags.Count);
        Assert.Equal(2, await context.Tags.CountAsync());
    }

    [Fact]
    public async Task ApplyAsync_SkipsMissingTags_WhenCreationIsDisabled()
    {
        await using var context = CreateContext();
        var performer = new Performer { Name = "Original Name" };
        context.Performers.Add(performer);
        await context.SaveChangesAsync();

        var service = new PerformerScrapeService(context, null!);
        var scraped = new ScrapedPerformerDto
        {
            TagNames = ["Uncreated Tag"],
            Urls = ["https://site.example/models/original-name"],
        };

        await service.ApplyAsync(performer, scraped, createMissingTags: false);
        await context.SaveChangesAsync();

        var updated = await context.Performers
            .Include(item => item.PerformerTags)
            .Include(item => item.Urls)
            .SingleAsync();

        Assert.Empty(updated.PerformerTags);
        Assert.Empty(context.Tags);
        Assert.Contains(updated.Urls, item => item.Url == "https://site.example/models/original-name");
    }

    [Fact]
    public async Task ApplyAsync_DownloadsAndReplacesPerformerImage()
    {
        await using var context = CreateContext();
        var performer = new Performer { Name = "Original Name", ImageBlobId = "old-blob" };
        context.Performers.Add(performer);
        await context.SaveChangesAsync();

        var blobService = new FakeBlobService();
        var httpClientFactory = new FakeHttpClientFactory(new HttpClient(new StubHttpMessageHandler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        })));

        var service = new PerformerScrapeService(context, null!, blobService, httpClientFactory);
        var scraped = new ScrapedPerformerDto
        {
            ImageUrl = "https://site.example/images/updated.jpg",
        };

        await service.ApplyAsync(performer, scraped, createMissingTags: false);

        Assert.Equal("blob-1", performer.ImageBlobId);
        Assert.Contains("old-blob", blobService.DeletedBlobIds);
        Assert.Equal("image/jpeg", blobService.StoredContentType);
        Assert.Equal([1, 2, 3, 4], blobService.StoredBytes);
    }

    [Fact]
    public void ConvertScrapeResult_ResolvesRelativeUrlsAndImage()
    {
        var scraped = PerformerScrapeService.ConvertScrapeResult(
            new Dictionary<string, object>
            {
                ["Name"] = "Jane Doe",
                ["URL"] = "/performer/jane-doe",
                ["Image"] = "/images/jane.jpg",
            },
            "https://example.com/performer/jane-doe");

        Assert.NotNull(scraped);
        Assert.Equal("https://example.com/images/jane.jpg", scraped!.ImageUrl);
        Assert.Contains("https://example.com/performer/jane-doe", scraped.Urls);
    }

    private static CoveContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"performer-scrape-service-{Guid.NewGuid():N}")
            .Options;

        return new TestCoveContext(options);
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
        }
    }

    private sealed class FakeBlobService : IBlobService
    {
        public byte[] StoredBytes { get; private set; } = [];
        public string StoredContentType { get; private set; } = string.Empty;
        public List<string> DeletedBlobIds { get; } = [];

        public async Task<string> StoreBlobAsync(Stream data, string contentType, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, ct);
            StoredBytes = buffer.ToArray();
            StoredContentType = contentType;
            return "blob-1";
        }

        public Task<(Stream Stream, string ContentType)?> GetBlobAsync(string blobId, CancellationToken ct = default)
            => Task.FromResult<(Stream Stream, string ContentType)?>(null);

        public Task DeleteBlobAsync(string blobId, CancellationToken ct = default)
        {
            DeletedBlobIds.Add(blobId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory());
    }
}
