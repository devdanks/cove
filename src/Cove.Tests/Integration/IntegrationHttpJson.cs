using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cove.Tests.Integration;

internal static class IntegrationHttpJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static Task<T?> ReadApiJsonAsync<T>(this HttpContent content, CancellationToken cancellationToken = default)
        => content.ReadFromJsonAsync<T>(Options, cancellationToken);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}