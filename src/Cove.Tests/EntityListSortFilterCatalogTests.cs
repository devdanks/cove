using System.Reflection;
using Cove.Core.Interfaces;

namespace Cove.Tests;

public class EntityListSortFilterCatalogTests
{
    [Fact]
    public void CatalogListsEveryP1BEntity()
    {
        Assert.Equal(
        [
            "videos",
            "images",
            "audios",
            "texts",
            "galleries",
            "groups",
            "segments",
            "performers",
            "studios",
            "tags",
            "faces",
        ], EntityListSortFilterCatalog.Entities);
    }

    [Fact]
    public void PublishedSortRowsAreUniqueWithinEachEntity()
    {
        var duplicates = EntityListSortFilterCatalog.Sorts
            .GroupBy(row => $"{row.Entity}\0{row.Key}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Replace("\0", ":", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void PublishedFilterRowsAreUniqueWithinEachEntity()
    {
        var duplicates = EntityListSortFilterCatalog.Filters
            .GroupBy(row => $"{row.Entity}\0{row.Key}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Replace("\0", ":", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("videos", typeof(VideoFilter))]
    [InlineData("images", typeof(ImageFilter))]
    [InlineData("audios", typeof(AudioFilter))]
    [InlineData("texts", typeof(TextDocumentFilter))]
    [InlineData("galleries", typeof(GalleryFilter))]
    [InlineData("groups", typeof(GroupFilter))]
    [InlineData("performers", typeof(PerformerFilter))]
    [InlineData("studios", typeof(StudioFilter))]
    [InlineData("tags", typeof(TagFilter))]
    public void CoreFilterCriterionPropertiesHaveMatrixRows(string entity, Type filterType)
    {
        var matrixKeys = EntityListSortFilterCatalog.Filters
            .Where(row => row.Entity.Equals(entity, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = filterType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name is not nameof(VideoFilter.CustomFieldCriteria) and not nameof(VideoFilter.CustomFieldCriterion))
            .Where(property => IsCriterionProperty(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            .Select(property => property.Name)
            .Where(propertyName => !matrixKeys.Contains(propertyName))
            .OrderBy(propertyName => propertyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(missing);
    }

    private static bool IsCriterionProperty(Type type)
        => type == typeof(IntCriterion)
           || type == typeof(StringCriterion)
           || type == typeof(BoolCriterion)
           || type == typeof(MultiIdCriterion)
           || type == typeof(DateCriterion)
           || type == typeof(TimestampCriterion)
           || type == typeof(FingerprintCriterion)
           || type == typeof(TagDurationCriterion);
}
