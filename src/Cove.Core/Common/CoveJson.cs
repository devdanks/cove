using System.Text.Json;

namespace Cove.Core.Common;

public static class CoveJson
{
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web);
}