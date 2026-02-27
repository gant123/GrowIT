using System.Net.Http.Json;
using System.Text.Json;

namespace GrowIT.Backend.Tests.Infrastructure;

public static class TestJsonExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T> ReadRequiredJsonAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return value ?? throw new InvalidOperationException($"Expected JSON body of type {typeof(T).Name}.");
    }

    public static async Task<Guid> ReadGuidPropertyAsync(this HttpResponseMessage response, string propertyName)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var property = doc.RootElement.EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (property.Equals(default(JsonProperty)) || property.Value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Response does not contain string property '{propertyName}'.");
        }

        return Guid.Parse(property.Value.GetString()!);
    }
}
