namespace Cove.Core.Common;

public static class SourceKeyConventions
{
    public const string ExtensionPrefix = "ext:";

    public static bool IsExtensionSource(string? sourceKey)
        => !string.IsNullOrWhiteSpace(sourceKey)
            && sourceKey.TrimStart().StartsWith(ExtensionPrefix, StringComparison.OrdinalIgnoreCase);
}