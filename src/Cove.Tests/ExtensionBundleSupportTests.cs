using System.Text.Json;
using Cove.Api.Controllers;
using Cove.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Tests;

public class ExtensionBundleSupportTests
{
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

            var controller = new ExtensionsController(manager)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        RequestServices = new ServiceCollection().BuildServiceProvider(),
                    },
                },
            };

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
}
