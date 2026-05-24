using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Cove.Data.Services;

public sealed class EmbeddingService(CoveContext db, IEnumerable<ITextEncoder> encoders) : IEmbeddingService, ITextEncoderRegistry
{
    private readonly Dictionary<string, ITextEncoder> _encoders = encoders.ToDictionary(
        encoder => encoder.KindFamily,
        StringComparer.OrdinalIgnoreCase);

    public ITextEncoder? Resolve(string kindFamily)
    {
        if (string.IsNullOrWhiteSpace(kindFamily))
            return null;

        return _encoders.GetValueOrDefault(kindFamily);
    }

    public async Task<IReadOnlyList<EmbeddingSearchResult>> KnnAsync(
        Vector query,
        int k,
        EmbeddingSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (k <= 0)
            return [];

        options ??= new EmbeddingSearchOptions();
        var dimensions = query.ToArray().Length;

        var embeddings = ApplyFilters(db.Embeddings.AsNoTracking(), options)
            .Where(embedding => embedding.Dim == dimensions);

        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            var ranked = await embeddings
                .OrderBy(embedding => embedding.Vector.CosineDistance(query))
                .Take(k)
                .Select(embedding => new
                {
                    Embedding = embedding,
                    Distance = embedding.Vector.CosineDistance(query),
                })
                .ToListAsync(cancellationToken);

            return ranked
                .Select(item => new EmbeddingSearchResult(item.Embedding, (float)item.Distance))
                .ToList();
        }

        var candidates = await embeddings.ToListAsync(cancellationToken);
        return candidates
            .Select(embedding => new EmbeddingSearchResult(embedding, ComputeCosineDistance(embedding.Vector, query)))
            .OrderBy(result => result.Distance)
            .Take(k)
            .ToList();
    }

    private static IQueryable<Embedding> ApplyFilters(IQueryable<Embedding> query, EmbeddingSearchOptions options)
    {
        if (options.HostType.HasValue)
            query = query.Where(embedding => embedding.HostType == options.HostType.Value);

        if (options.HostId.HasValue)
            query = query.Where(embedding => embedding.HostId == options.HostId.Value);

        if (!string.IsNullOrWhiteSpace(options.Kind))
            query = query.Where(embedding => embedding.Kind == options.Kind);

        if (!string.IsNullOrWhiteSpace(options.KindFamily))
            query = query.Where(embedding => embedding.KindFamily == options.KindFamily);

        if (options.Modality.HasValue)
            query = query.Where(embedding => embedding.Modality == options.Modality.Value);

        if (options.IsSemantic.HasValue)
            query = query.Where(embedding => embedding.IsSemantic == options.IsSemantic.Value);

        if (!string.IsNullOrWhiteSpace(options.SourceKey))
            query = query.Where(embedding => embedding.SourceKey == options.SourceKey);

        return query;
    }

    private static float ComputeCosineDistance(Vector left, Vector right)
    {
        var leftValues = left.ToArray();
        var rightValues = right.ToArray();

        if (leftValues.Length != rightValues.Length)
            throw new InvalidOperationException("Embedding dimensions do not match.");

        if (leftValues.Length == 0)
            return 1f;

        var dot = 0f;
        var leftNorm = 0f;
        var rightNorm = 0f;

        for (var index = 0; index < leftValues.Length; index++)
        {
            dot += leftValues[index] * rightValues[index];
            leftNorm += leftValues[index] * leftValues[index];
            rightNorm += rightValues[index] * rightValues[index];
        }

        if (leftNorm <= 0f || rightNorm <= 0f)
            return 1f;

        var similarity = dot / (MathF.Sqrt(leftNorm) * MathF.Sqrt(rightNorm));
        return 1f - similarity;
    }
}