namespace Cove.Core.Auth;

/// <summary>
/// In-memory registry of content-policy contributions from extensions. v1 stores
/// the policies for inspection and audit; v2 will compose them with the EF query
/// filter (Schema C Stage 2). Thread-safe via copy-on-write replacement.
/// </summary>
public static class ContentPolicyRegistry
{
    private static readonly object _lock = new();
    private static IReadOnlyDictionary<string, IReadOnlyList<object>> _byExtension =
        new Dictionary<string, IReadOnlyList<object>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a batch of policies for an extension. Policies are stored as <c>object</c>
    /// because the concrete <c>ContentPolicy</c> record lives in Cove.Sdk; consumers cast.
    /// </summary>
    public static void Register(string extensionId, IReadOnlyList<object> policies)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) throw new ArgumentException(null, nameof(extensionId));
        lock (_lock)
        {
            var copy = new Dictionary<string, IReadOnlyList<object>>(_byExtension, StringComparer.OrdinalIgnoreCase)
            {
                [extensionId] = policies
            };
            _byExtension = copy;
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<object>> All => _byExtension;

    /// <summary>Reset state (test helper).</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _byExtension = new Dictionary<string, IReadOnlyList<object>>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
