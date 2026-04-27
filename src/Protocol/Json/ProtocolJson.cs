using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Json;

/// <summary>Default <see cref="JsonSerializerOptions"/> for RPC payload DTOs.</summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance,
        DictionaryKeyPolicy = SnakeCaseNamingPolicy.Instance,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(SnakeCaseNamingPolicy.Instance, allowIntegerValues: false),
        },
    };

    /// <summary>Convenience: serialize a DTO and wrap the JSON text back into a <see cref="JsonElement"/>.</summary>
    public static JsonElement ToElement<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonDocument.Parse(json).RootElement;
    }
}
