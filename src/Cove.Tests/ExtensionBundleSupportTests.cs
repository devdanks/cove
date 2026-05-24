using System.Text.Json;
using Cove.Api.Controllers;
using Cove.Api.Services;
using Cove.Core.Interfaces;
using Cove.Plugins;
using Cove.Sdk;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

public class ExtensionBundleSupportTests
{
    [Fact]
    public void CoveExtensionBase_CanContributeSettingsTabs()
    {
        var manager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = Path.GetTempPath(),
            CoveVersion = "1.0.0",
        });

        var extension = new SettingsTabContributionExtension();
        Assert.IsAssignableFrom<IUIExtension>(extension);

        manager.Register(extension, "local");

        var manifest = manager.GetAggregatedManifest();
        var settingsTab = Assert.Single(manifest.SettingsTabs);
        Assert.Equal("extensions/example", settingsTab.Key);
        Assert.Equal(SettingsTabContributionExtension.ExtensionId, settingsTab.ExtensionId);

        var settingsPanel = Assert.Single(manifest.SettingsPanels);
        Assert.Equal("extensions/example", settingsPanel.TargetTab);
    }

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

    [Fact]
    public async Task DisableExtensionAsync_DisablesEnabledTransitiveDependents()
    {
        var manager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = Path.GetTempPath(),
            CoveVersion = "1.0.0",
        });

        manager.Register(new TestExtension("base", "Base"), "local");
        manager.Register(new TestExtension("middle", "Middle", new Dictionary<string, string> { ["base"] = ">=1.0.0" }), "local");
        manager.Register(new TestExtension("leaf", "Leaf", new Dictionary<string, string> { ["middle"] = ">=1.0.0" }), "local");

        var disabled = await manager.DisableExtensionAsync("base");

        Assert.Equal(["base", "leaf", "middle"], disabled.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.False(manager.IsEnabled("base"));
        Assert.False(manager.IsEnabled("middle"));
        Assert.False(manager.IsEnabled("leaf"));
    }

    [Fact]
    public async Task EnableExtensionAsync_EnablesDisabledTransitiveDependenciesFirst()
    {
        var manager = new ExtensionManager(new ExtensionContext
        {
            Configuration = new ConfigurationBuilder().Build(),
            DataDirectory = Path.GetTempPath(),
            CoveVersion = "1.0.0",
        });

        manager.Register(new TestExtension("base", "Base"), "local");
        manager.Register(new TestExtension("middle", "Middle", new Dictionary<string, string> { ["base"] = ">=1.0.0" }), "local");
        manager.Register(new TestExtension("leaf", "Leaf", new Dictionary<string, string> { ["middle"] = ">=1.0.0" }), "local");

        await manager.DisableExtensionAsync("base");
        var enabled = await manager.EnableExtensionAsync("leaf");

        Assert.Equal(["base", "middle", "leaf"], enabled.ToArray());
        Assert.True(manager.IsEnabled("base"));
        Assert.True(manager.IsEnabled("middle"));
        Assert.True(manager.IsEnabled("leaf"));
    }

    [Fact]
    public async Task RegistryUninstall_RequiresConfirmationBeforeRemovingDependents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cove-dependent-uninstall-{Guid.NewGuid():N}");
        var dataDir = Path.Combine(root, "data");
        var extensionsDir = Path.Combine(root, "extensions");
        var baseDir = Path.Combine(extensionsDir, "base.pack");
        var dependentDir = Path.Combine(extensionsDir, "dependent.bundle");

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(dependentDir);

        await File.WriteAllTextAsync(Path.Combine(baseDir, "extension.json"), JsonSerializer.Serialize(new ExtensionManifestFile
        {
            Id = "base.pack",
            Name = "Base Pack",
            Version = "1.0.0",
            Kind = "scraper-pack",
        }));

        await File.WriteAllTextAsync(Path.Combine(dependentDir, "extension.json"), JsonSerializer.Serialize(new ExtensionManifestFile
        {
            Id = "dependent.bundle",
            Name = "Dependent Bundle",
            Version = "1.0.0",
            Kind = "bundle",
            Dependencies = new Dictionary<string, string> { ["base.pack"] = ">=1.0.0" },
        }));

        try
        {
            var manager = new ExtensionManager(new ExtensionContext
            {
                Configuration = new ConfigurationBuilder().Build(),
                DataDirectory = dataDir,
                CoveVersion = "1.0.0",
            });

            manager.DiscoverExtensions(extensionsDir);
            var controller = CreateController(manager, new ServiceCollection().BuildServiceProvider());

            var previewResult = await controller.RegistryUninstall(new RegistryUninstallRequest { ExtensionId = "base.pack" });
            var previewOk = Assert.IsType<OkObjectResult>(previewResult);
            var preview = JsonSerializer.SerializeToElement(previewOk.Value);
            Assert.True(preview.GetProperty("requiresDependents").GetBoolean());
            Assert.Equal("dependent.bundle", preview.GetProperty("dependents")[0].GetProperty("Id").GetString());
            Assert.True(Directory.Exists(baseDir));
            Assert.True(Directory.Exists(dependentDir));

            var uninstallResult = await controller.RegistryUninstall(new RegistryUninstallRequest
            {
                ExtensionId = "base.pack",
                UninstallDependents = true,
            });
            Assert.IsType<OkObjectResult>(uninstallResult);
            Assert.False(Directory.Exists(baseDir));
            Assert.False(Directory.Exists(dependentDir));
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

    private sealed class SettingsTabContributionExtension : CoveExtensionBase
    {
        public const string ExtensionId = "com.example.settings-tab";

        public override string Id => ExtensionId;
        public override string Name => "Settings Tab Extension";
        public override string Version => "1.0.0";

        public override UIManifest GetUIManifest()
            => ManifestBuilder()
                .AddSettingsTab(
                    "extensions/example",
                    "Example",
                    description: "Example settings tab from a normal extension.")
                .AddSettingsSection("extensions/example", "Example Settings", "ExampleSettingsPanel")
                .Build();
    }

    private sealed class TestExtension(
        string id,
        string name,
        IReadOnlyDictionary<string, string>? dependencies = null) : IExtension
    {
        public string Id => id;
        public string Name => name;
        public string Version => "1.0.0";
        public string? Description => null;
        public string? Author => null;
        public string? Url => null;
        public string? IconUrl => null;
        public IReadOnlyDictionary<string, string> Dependencies { get; } = dependencies ?? new Dictionary<string, string>();

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
