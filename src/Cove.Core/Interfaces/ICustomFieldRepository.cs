using Cove.Core.Entities;

namespace Cove.Core.Interfaces;

/// <summary>
/// Generic repository for custom field definitions and values.
/// Available to any extension that reads or writes custom field data on Cove entities.
/// </summary>
public interface ICustomFieldRepository
{
    Task<CustomFieldDefinition?> FindDefinitionAsync(string entityType, string key, CancellationToken ct = default);
    Task<CustomFieldDefinition> FindOrCreateDefinitionAsync(CustomFieldDefinition definition, CancellationToken ct = default);
    Task<IReadOnlyList<CustomFieldValue>> FindValuesAsync(string entityType, int entityId, CancellationToken ct = default);
    Task UpsertValueAsync(string entityType, int entityId, int definitionId, string value, CancellationToken ct = default);
    Task UpsertNumberValueAsync(string entityType, int entityId, int definitionId, decimal value, CancellationToken ct = default);
    Task<decimal?> FindNumberValueAsync(string entityType, int entityId, string definitionKey, CancellationToken ct = default);
}
