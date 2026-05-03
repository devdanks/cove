namespace Cove.Core.Common;

public static class QueryParsing
{
    public static IReadOnlyList<int>? ParseIntList(string? raw)
        => string.IsNullOrEmpty(raw) ? null : raw.Split(',').Select(int.Parse).ToList();
}