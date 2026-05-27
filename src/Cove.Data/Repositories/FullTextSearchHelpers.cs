using System.Linq.Expressions;
using Cove.Core.Entities;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Cove.Data.Repositories;

public static class FullTextSearchHelpers
{
    private const string SearchVectorProperty = "SearchVector";
    private const string SearchConfig = "simple";

    public static bool IsActive(CoveContext db, string? search)
        => SupportsPostgresFullText(db) && !string.IsNullOrWhiteSpace(search);

    public static IQueryable<T> Apply<T>(
        CoveContext db,
        IQueryable<T> query,
        string? search,
        params Expression<Func<T, string?>>[] fallbackSelectors)
        where T : BaseEntity
    {
        var normalized = Normalize(search);
        if (normalized is null)
            return query;

        if (!SupportsPostgresFullText(db))
            return FilterHelpers.ApplyBooleanKeywordSearch(query, normalized, fallbackSelectors);

        return query.Where(entity => EF.Property<NpgsqlTsVector>(entity, SearchVectorProperty)
            .Matches(EF.Functions.WebSearchToTsQuery(SearchConfig, normalized)));
    }

    public static bool ShouldOrderByRelevance(CoveContext db, string? search, string? explicitSort)
        => IsActive(db, search) && string.IsNullOrWhiteSpace(explicitSort);

    public static IQueryable<T> OrderByRelevance<T>(CoveContext db, IQueryable<T> query, string? search)
        where T : BaseEntity
    {
        var normalized = Normalize(search);
        if (normalized is null || !SupportsPostgresFullText(db))
            return query;

        return query
            .OrderByDescending(entity => EF.Property<NpgsqlTsVector>(entity, SearchVectorProperty)
                .Rank(EF.Functions.WebSearchToTsQuery(SearchConfig, normalized)))
            .ThenByDescending(entity => entity.UpdatedAt)
            .ThenBy(entity => entity.Id);
    }

    private static bool SupportsPostgresFullText(CoveContext db)
        => db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;

    private static string? Normalize(string? search)
    {
        var normalized = search?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
