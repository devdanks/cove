using Cove.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Sdk;

/// <summary>
/// Convenient base class for Cove extensions that provides sensible defaults
/// and reduces boilerplate. Override only the methods you need.
/// </summary>
public abstract class CoveExtensionBase : IUIExtension
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Version { get; }
    public virtual string? Description => null;
    public virtual string? Author => null;
    public virtual string? Url => null;
    public virtual string? IconUrl => null;
    public virtual IReadOnlyList<string> Categories => [];
    public virtual string? MinCoveVersion => null;
    public virtual IReadOnlyDictionary<string, string> Dependencies => new Dictionary<string, string>();

    /// <summary>
    /// Override to register services. Base implementation does nothing.
    /// </summary>
    public virtual void ConfigureServices(IServiceCollection services, ExtensionContext context) { }

    /// <summary>
    /// Override to contribute UI pages, settings tabs, settings panels, or other frontend manifest entries.
    /// Default implementation returns an empty manifest.
    /// </summary>
    public virtual UIManifest GetUIManifest() => new();

    /// <summary>Create a UIManifestBuilder pre-configured with this extension's ID.</summary>
    protected UIManifestBuilder ManifestBuilder() => new(Id);

    /// <summary>
    /// Override to perform async initialization after DI container is built.
    /// </summary>
    public virtual Task InitializeAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Publish every service of contract <typeparamref name="T"/> registered in THIS extension's
    /// container to the cross-extension <see cref="IExtensionServiceExchange"/>, so sibling extensions
    /// (which are isolated in their own containers) can consume them. Call this from
    /// <see cref="InitializeAsync"/>. The host withdraws these entries automatically when the extension
    /// is disabled or uninstalled, so there is nothing to clean up.
    /// </summary>
    protected void PublishContributions<T>(IServiceProvider services) where T : class
    {
        var exchange = services.GetService<IExtensionServiceExchange>();
        if (exchange is null)
            return;

        foreach (var instance in services.GetServices<T>())
            exchange.Publish(Id, typeof(T), instance);
    }

    public virtual Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task OnInstallAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task OnUninstallAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
}
