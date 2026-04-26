using System.Net;
using System.Net.Http;
using Cove.Api.Extensions;
using Cove.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public class DirectFileDownloaderExtensionTests
{
    [Fact]
    public async Task MatchAsync_ReturnsSceneDownloader_ForVideoUrl()
    {
        var extension = new DirectFileDownloaderExtension();

        var match = await extension.MatchAsync("https://cdn.example.com/media/sample-video.mp4?token=abc", CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("builtin.direct-file/scene", match!.DownloaderId);
        Assert.Equal("sample-video.mp4", match.Label);
    }

    [Fact]
    public async Task MatchAsync_ReturnsImageDownloader_ForImageUrl()
    {
        var extension = new DirectFileDownloaderExtension();

        var match = await extension.MatchAsync("https://images.example.com/gallery/cover.jpeg", CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("builtin.direct-file/image", match!.DownloaderId);
        Assert.Equal("cover.jpeg", match.Label);
    }

    [Fact]
    public async Task DownloadAsync_WritesFileToHostTempDirectory()
    {
        var extension = new DirectFileDownloaderExtension();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "cove-direct-file-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var host = new FakeDownloaderHost(tempDirectory, new StubHttpClientFactory(new StubHttpMessageHandler()));
            var result = await extension.DownloadAsync(
                new DownloaderRequest(
                    "builtin.direct-file/scene",
                    "https://cdn.example.com/video/test-scene.mp4",
                    DownloaderEntity.Scene,
                    new DownloaderPermissions(["cdn.example.com"])),
                host,
                CancellationToken.None);

            Assert.NotNull(result);
            var localPath = Path.Combine(tempDirectory, result!.LocalPath);
            Assert.True(File.Exists(localPath));
            Assert.Equal("test-scene.mp4", result.OriginalFilename);
            Assert.Equal("video/mp4", result.Headers!["Content-Type"]);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class FakeDownloaderHost(string tempDirectory, IHttpClientFactory httpClients) : IDownloaderHost
    {
        public string TempDirectory => tempDirectory;
        public IHttpClientFactory HttpClients => httpClients;
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void ReportProgress(double progress, string? message = null)
        {
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4, 5]),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            response.Content.Headers.ContentLength = 5;
            return Task.FromResult(response);
        }
    }
}