namespace Cove.Core.Auth;

/// <summary>
/// In-memory catalog of all known permissions (core + extensions). Persisted to the
/// `permissions` table on startup so the role editor can reference rich metadata.
/// </summary>
public interface IPermissionRegistry
{
    IReadOnlyList<PermissionDefinition> All { get; }
    bool IsKnown(string key);
    PermissionDefinition? Get(string key);

    /// <summary>Expand permission grants (including wildcards / implies) into the full effective set.</summary>
    HashSet<string> Expand(IEnumerable<string> grantedKeys);

    /// <summary>Register additional permissions (called by extension loader).</summary>
    void RegisterExtensionPermissions(string extensionId, IEnumerable<PermissionDefinition> defs);
}

public sealed class PermissionRegistry : IPermissionRegistry
{
    private readonly Dictionary<string, PermissionDefinition> _byKey = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public PermissionRegistry()
    {
        foreach (var p in Permissions.CorePermissions)
            _byKey[p.Key] = p;
    }

    public IReadOnlyList<PermissionDefinition> All
    {
        get
        {
            lock (_lock) return _byKey.Values.ToList();
        }
    }

    public bool IsKnown(string key)
    {
        lock (_lock) return _byKey.ContainsKey(key);
    }

    public PermissionDefinition? Get(string key)
    {
        lock (_lock) return _byKey.TryGetValue(key, out var d) ? d : null;
    }

    public HashSet<string> Expand(IEnumerable<string> grantedKeys)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in grantedKeys)
        {
            result.Add(key);
            if (key == "*")
            {
                // superuser wildcard — leave as-is; CovePrincipal.Has shortcuts
                continue;
            }
            // expand "<resource>.*" — leave as-is so Has() can shortcut
            if (key.EndsWith(".*", StringComparison.Ordinal))
                continue;
            // expand implies recursively
            if (Get(key) is { } def && def.Implies is { Length: > 0 } implies)
            {
                foreach (var implied in implies)
                    foreach (var x in Expand([implied]))
                        result.Add(x);
            }
        }
        return result;
    }

    public void RegisterExtensionPermissions(string extensionId, IEnumerable<PermissionDefinition> defs)
    {
        var prefix = extensionId + ".";
        lock (_lock)
        {
            foreach (var raw in defs)
            {
                var key = raw.Key;
                // Defense-in-depth: enforce extension namespace prefix; reject "*" and core-namespaced keys.
                if (key == "*") continue;
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                _byKey[key] = raw with { Source = "extension:" + extensionId };
            }
        }
    }
}
