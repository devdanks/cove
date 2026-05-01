using Cove.Core.Entities;
using Cove.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests;

public class FaceSuggestionModelConfigurationTests
{
    [Fact]
    public void FaceSuggestionDecision_UsesConfiguredTableAndIndexes()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new CoveContext(options);

        var entityType = context.Model.FindEntityType(typeof(FaceSuggestionDecision));

        Assert.NotNull(entityType);
        Assert.Equal("face_suggestion_decisions", entityType!.GetTableName());
        Assert.Equal(16, entityType.FindProperty(nameof(FaceSuggestionDecision.Decision))!.GetMaxLength());

        var indexes = entityType.GetIndexes()
            .Select(index => (Properties: string.Join(",", index.Properties.Select(property => property.Name)), index.IsUnique))
            .ToList();

        Assert.Contains(("FaceId,PerformerId,UserId", true), indexes);
        Assert.Contains(("FaceId,UserId", false), indexes);
        Assert.Contains(("UserId,Decision", false), indexes);
    }
}