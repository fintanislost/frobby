using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of one map tile across one or more layers. Response shape of <c>state.map_tile</c>.</summary>
public sealed class MapTileState
{
    public string Location { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public List<MapTileLayerState> Layers { get; set; } = new();
}

/// <summary>Tile state for one xTile layer at the requested coordinate.</summary>
public sealed class MapTileLayerState
{
    public string Name { get; set; } = string.Empty;
    public int TileIndex { get; set; } = -1;
    public string TileSheet { get; set; } = string.Empty;

    [JsonConverter(typeof(VerbatimStringDictionaryJsonConverter))]
    public Dictionary<string, string> Properties { get; set; } = new();
}

internal sealed class VerbatimStringDictionaryJsonConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for string dictionary.");

        var result = new Dictionary<string, string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name in string dictionary.");

            var key = reader.GetString() ?? string.Empty;
            if (!reader.Read())
                throw new JsonException("Expected property value in string dictionary.");

            result[key] = reader.TokenType == JsonTokenType.Null
                ? string.Empty
                : reader.GetString() ?? reader.GetRawStringValue();
        }

        throw new JsonException("Unterminated string dictionary object.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        Dictionary<string, string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, item) in value)
            writer.WriteString(key, item);
        writer.WriteEndObject();
    }
}

internal static class Utf8JsonReaderExtensions
{
    public static string GetRawStringValue(this Utf8JsonReader reader)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => reader.TryGetInt64(out var integer)
                ? integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => string.Empty,
        };
}
