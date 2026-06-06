using Microsoft.Extensions.Primitives;

namespace Cove.Plugins;

/// <summary>
/// A host-owned exchange where extensions PUBLISH shared-contract services/contributions and CONSUME
/// those published by sibling extensions.
///
/// <para>This is how extensions interact across the isolation boundary. Each extension lives in its own
/// service container (so its state is stable for its own lifetime and unaffected by other extensions'
/// install/uninstall), which means one extension can no longer resolve another's services through a
/// shared DI container. Instead, a producing extension publishes a typed instance here (keyed by its
/// extension id), and a consuming extension asks for all instances of a contract type. The contract
/// types live in a package both extensions reference (e.g. an AI abstractions assembly); the host never
/// needs to know them, because the exchange is generic.</para>
///
/// <para>The exchange is a single root singleton, forwarded into every extension container, so any
/// extension can publish to or read from it. Entries are withdrawn automatically when an extension is
/// unloaded or disabled. Consumers may read live on each use, or cache and refresh on
/// <see cref="GetChangeToken"/>.</para>
/// </summary>
public interface IExtensionServiceExchange
{
    /// <summary>Publish a contribution instance under <paramref name="contractType"/>, owned by the extension.</summary>
    void Publish(string extensionId, Type contractType, object instance);

    /// <summary>Publish a contribution instance under contract <typeparamref name="T"/>, owned by the extension.</summary>
    void Publish<T>(string extensionId, T instance) where T : notnull;

    /// <summary>All published instances assignable to <typeparamref name="T"/>, in publish order, de-duplicated.</summary>
    IReadOnlyList<T> GetAll<T>() where T : class;

    /// <summary>Remove every contribution owned by the extension (called by the host on unload/disable).</summary>
    void WithdrawAll(string extensionId);

    /// <summary>A change token that fires whenever the set of published contributions changes.</summary>
    IChangeToken GetChangeToken();
}

/// <summary>Default thread-safe <see cref="IExtensionServiceExchange"/>.</summary>
public sealed class ExtensionServiceExchange : IExtensionServiceExchange
{
    private readonly record struct Entry(string ExtensionId, Type Contract, object Instance);

    private readonly object _lock = new();
    private readonly List<Entry> _entries = new();
    private CancellationTokenSource _cts = new();

    public void Publish(string extensionId, Type contractType, object instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentNullException.ThrowIfNull(instance);

        lock (_lock)
            _entries.Add(new Entry(extensionId, contractType, instance));
        Invalidate();
    }

    public void Publish<T>(string extensionId, T instance) where T : notnull
        => Publish(extensionId, typeof(T), instance);

    public IReadOnlyList<T> GetAll<T>() where T : class
    {
        lock (_lock)
        {
            List<T>? result = null;
            HashSet<object>? seen = null;
            foreach (var entry in _entries)
            {
                if (entry.Instance is not T typed)
                    continue;
                result ??= new List<T>();
                seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
                if (seen.Add(typed))
                    result.Add(typed);
            }
            return (IReadOnlyList<T>?)result ?? Array.Empty<T>();
        }
    }

    public void WithdrawAll(string extensionId)
    {
        bool removed;
        lock (_lock)
            removed = _entries.RemoveAll(e => string.Equals(e.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
            Invalidate();
    }

    public IChangeToken GetChangeToken()
    {
        lock (_lock)
            return new CancellationChangeToken(_cts.Token);
    }

    private void Invalidate()
    {
        CancellationTokenSource old;
        lock (_lock)
        {
            old = _cts;
            _cts = new CancellationTokenSource();
        }
        old.Cancel();
        old.Dispose();
    }
}
