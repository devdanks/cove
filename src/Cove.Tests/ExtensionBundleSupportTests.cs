using System.Text.Json;
using Cove.Api.Controllers;
using Cove.Api.Services;
using Cove.Core.Interfaces;
using Cove.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public class ExtensionBundleSupportTests
{
    [Fact]
    public void GetExtensions_UsesManifestCategoriesForLoadedExtensions()
    {
        var manager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = Path.GetTempPath(),
            CoveVersion = "1.0.0",
        });

        manager.Register(new RuntimeCategoryFallbackExtension(), "local");

        var manifest = new ExtensionManifestFile
        {
            Id = RuntimeCategoryFallbackExtension.ExtensionId,
            Name = "Runtime Category Fallback",
            Version = "1.0.0",
            Categories = ["scraper", "metadata"],
        };

        var install = manager.GetInstallation(RuntimeCategoryFallbackExtension.ExtensionId);
        Assert.NotNull(install);
        install!.ManifestJson = JsonSerializer.Serialize(manifest);
        install.Categories = null;

        var controller = CreateController(manager);

        var allResult = controller.GetExtensions();
        var allOk = Assert.IsType<OkObjectResult>(allResult.Result);
        var extension = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ExtensionInfo>>(allOk.Value));
        Assert.Contains("scraper", extension.Categories);

        var filteredResult = controller.GetExtensions("scraper");
        var filteredOk = Assert.IsType<OkObjectResult>(filteredResult.Result);
        var filteredExtension = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ExtensionInfo>>(filteredOk.Value));
        Assert.Equal(RuntimeCategoryFallbackExtension.ExtensionId, filteredExtension.Id);

        var unmatchedResult = controller.GetExtensions("theme");
        var unmatchedOk = Assert.IsType<OkObjectResult>(unmatchedResult.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<ExtensionInfo>>(unmatchedOk.Value));
    }

    [Fact]
    public async Task ManifestOnlyBundles_AreDiscoverableListableAndUninstallable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cove-bundle-{Guid.NewGuid():N}");
        var dataDir = Path.Combine(root, "data");
        var extensionsDir = Path.Combine(root, "extensions");
        var bundleDir = Path.Combine(extensionsDir, "ai.full");

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(bundleDir);

        var manifest = new ExtensionManifestFile
        {
            Id = "ai.full",
            Name = "AI Full",
            Version = "1.2.3",
            Kind = "bundle",
            Description = "Installs the full AI stack.",
            Dependencies = new Dictionary<string, string>
            {
                ["cove.ai.core"] = ">=1.0.0",
                ["cove.ai.vlm"] = ">=1.0.0",
            },
            Categories = ["ai"],
        };

        await File.WriteAllTextAsync(
            Path.Combine(bundleDir, "extension.json"),
            JsonSerializer.Serialize(manifest));

        try
        {
            var manager = new ExtensionManager(new ExtensionContext
            {
                Configuration = new ConfigurationBuilder().Build(),
                DataDirectory = dataDir,
                CoveVersion = "1.0.0",
            });

            manager.DiscoverExtensions(extensionsDir);

            Assert.True(manager.IsManifestOnlyExtension("ai.full"));
            var install = Assert.IsType<ExtensionInstallation>(manager.GetInstallation("ai.full"));
            Assert.Equal("1.2.3", install.Version);

            var controller = CreateController(manager, new ServiceCollection().BuildServiceProvider());

            var listResult = controller.GetExtensions();
            var ok = Assert.IsType<OkObjectResult>(listResult.Result);
            var items = Assert.IsAssignableFrom<IEnumerable<ExtensionInfo>>(ok.Value);
            var bundle = Assert.Single(items);
            Assert.Equal("ai.full", bundle.Id);
            Assert.Equal("bundle", bundle.Kind);
            Assert.Equal(2, bundle.Dependencies.Count);

            var uninstallResult = await controller.RegistryUninstall(new RegistryUninstallRequest { ExtensionId = "ai.full" });
            Assert.IsType<OkObjectResult>(uninstallResult);
            Assert.Null(manager.GetInstallation("ai.full"));
            Assert.False(Directory.Exists(bundleDir));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class RuntimeCategoryFallbackExtension : IExtension
    {
        public const string ExtensionId = "com.example.runtime-category-fallback";

        public string Id => ExtensionId;
        public string Name => "Runtime Category Fallback";
        public string Version => "1.0.0";
        public string? Description => null;
        public string? Author => null;
        public string? Url => null;
        public string? IconUrl => null;

        public void ConfigureServices(IServiceCollection services, ExtensionContext context)
        {
        }
    }

    private static ExtensionsController CreateController(ExtensionManager manager, IServiceProvider? requestServices = null)
    {
        var controller = new ExtensionsController(
            manager,
            new ScraperService(new CoveConfiguration(), NullLogger<ScraperService>.Instance, new TestHttpClientFactory(), manager));

        if (requestServices != null)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = requestServices,
                },
            };
        }

        return controller;
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
