using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using StardewValley.GameData;
using StardewValley.GameData.Locations;
using StardewValley.GameData.Movies;
using xTile;

namespace SdvTestFramework.Harness.Assets;

public static class ContentAssetProjector
{
    private const int MaxObjectDepth = 3;
    private const int DefaultNestedItemsLimit = 25;
    private const int MaxNestedItemsLimit = 100;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "map", "texture", "data", "string", "unknown",
    };

    public static ContentAssetResult Project(IContentAssetLoader loader, ContentAssetRequest req)
    {
        Validate(req);

        var type = string.IsNullOrWhiteSpace(req.AssetType) ? null : req.AssetType;
        if (type is "map")
        {
            return loader.TryLoad<Map>(req.Name, out var map) && map is not null
                ? Found(req.Name, "map", map.GetType(), SummarizeMap(map))
                : Missing(req.Name);
        }

        if (type is "texture")
        {
            return loader.TryLoad<Texture2D>(req.Name, out var texture) && texture is not null
                ? Found(req.Name, "texture", texture.GetType(), SummarizeTexture(texture, req.HashTexture))
                : Missing(req.Name);
        }

        if (type is "string")
        {
            return loader.TryLoad<string>(req.Name, out var text) && text is not null
                ? Found(req.Name, "string", text.GetType(), new JsonObject
                {
                    ["text"] = text,
                    ["length"] = text.Length,
                })
                : Missing(req.Name);
        }

        if (type is "data")
            return TryProjectData(loader, req) ?? Missing(req.Name);

        return TryProjectAuto(loader, req) ?? Missing(req.Name);
    }

    private static void Validate(ContentAssetRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "content.asset requires params.name");
        if (req.AssetType is { Length: > 0 } type && !AllowedTypes.Contains(type))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unsupported asset_type: {type}");
        if (req.KeysLimit is < 1 or > 500)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "keys_limit must be between 1 and 500");
        if (req.NestedItemsLimit is < 1 or > MaxNestedItemsLimit)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "nested_items_limit must be between 1 and 100");
    }

    private static ContentAssetResult? TryProjectAuto(IContentAssetLoader loader, ContentAssetRequest req)
    {
        if (loader.TryLoad<Map>(req.Name, out var map) && map is not null)
            return Found(req.Name, "map", map.GetType(), SummarizeMap(map));
        if (loader.TryLoad<Texture2D>(req.Name, out var texture) && texture is not null)
            return Found(req.Name, "texture", texture.GetType(), SummarizeTexture(texture, req.HashTexture));
        if (loader.TryLoad<string>(req.Name, out var text) && text is not null)
            return Found(req.Name, "string", text.GetType(), new JsonObject { ["text"] = text, ["length"] = text.Length });
        return TryProjectData(loader, req);
    }

    private static ContentAssetResult? TryProjectData(IContentAssetLoader loader, ContentAssetRequest req)
    {
        if (loader.TryLoad<Dictionary<string, string>>(req.Name, out var stringDict) && stringDict is not null)
            return Found(req.Name, "data", stringDict.GetType(), SummarizeDictionary(stringDict, req));

        if (loader.TryLoad<Dictionary<string, object>>(req.Name, out var objectDict) && objectDict is not null)
            return Found(req.Name, "data", objectDict.GetType(), SummarizeDictionary(objectDict, req));

        if (loader.TryLoad<Dictionary<string, LocationData>>(req.Name, out var locationDict) && locationDict is not null)
            return Found(req.Name, "data", locationDict.GetType(), SummarizeDictionary(locationDict, req));

        if (loader.TryLoad<Dictionary<string, ModFarmType>>(req.Name, out var modFarmDict) && modFarmDict is not null)
            return Found(req.Name, "data", modFarmDict.GetType(), SummarizeDictionary(modFarmDict, req));

        if (loader.TryLoad<List<MovieCharacterReaction>>(req.Name, out var movieReactionList) && movieReactionList is not null)
            return Found(req.Name, "data", movieReactionList.GetType(), SummarizeKeyedList(
                movieReactionList,
                reaction => FirstNonEmpty(reaction.Id, reaction.NPCName),
                req));

        if (loader.TryLoad<List<ConcessionTaste>>(req.Name, out var concessionTasteList) && concessionTasteList is not null)
            return Found(req.Name, "data", concessionTasteList.GetType(), SummarizeKeyedList(
                concessionTasteList,
                taste => FirstNonEmpty(taste.Id, taste.Name),
                req));

        if (loader.TryLoad<Dictionary<string, MovieCharacterReaction>>(req.Name, out var movieReactionDict) && movieReactionDict is not null)
            return Found(req.Name, "data", movieReactionDict.GetType(), SummarizeDictionary(movieReactionDict, req));

        if (loader.TryLoad<Dictionary<string, ConcessionTaste>>(req.Name, out var concessionTasteDict) && concessionTasteDict is not null)
            return Found(req.Name, "data", concessionTasteDict.GetType(), SummarizeDictionary(concessionTasteDict, req));

        return null;
    }

    private static JsonObject SummarizeKeyedList<T>(
        IReadOnlyCollection<T> data,
        Func<T, string?> keySelector,
        ContentAssetRequest req)
    {
        var keyed = data
            .Select(item => (Key: keySelector(item), Item: item))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .GroupBy(entry => entry.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Item, StringComparer.Ordinal);

        return SummarizeDictionary(keyed, req);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static JsonObject SummarizeMap(Map map)
    {
        var firstLayer = map.Layers.FirstOrDefault();
        var summary = new JsonObject
        {
            ["width"] = firstLayer?.LayerWidth ?? 0,
            ["height"] = firstLayer?.LayerHeight ?? 0,
            ["layers"] = new JsonArray(map.Layers.Select(layer => new JsonObject
            {
                ["name"] = layer.Id,
                ["width"] = layer.LayerWidth,
                ["height"] = layer.LayerHeight,
            }).ToArray<JsonNode?>()),
            ["tilesheets"] = new JsonArray(map.TileSheets.Select(sheet => new JsonObject
            {
                ["id"] = sheet.Id,
                ["image_source"] = sheet.ImageSource,
                ["tile_width"] = sheet.TileWidth,
                ["tile_height"] = sheet.TileHeight,
                ["sheet_width"] = sheet.SheetWidth,
                ["sheet_height"] = sheet.SheetHeight,
            }).ToArray<JsonNode?>()),
        };

        var properties = new JsonObject();
        foreach (var key in map.Properties.Keys)
            properties[key] = map.Properties[key]?.ToString() ?? string.Empty;
        summary["properties"] = properties;
        return summary;
    }

    private static JsonObject SummarizeTexture(Texture2D texture, bool hashTexture)
    {
        var summary = new JsonObject
        {
            ["width"] = texture.Width,
            ["height"] = texture.Height,
        };

        if (hashTexture)
        {
            try { summary["content_hash"] = TextureHasher.ComputeHashHexPrefix(texture); }
            catch { }
        }

        return summary;
    }

    private static JsonObject SummarizeDictionary<T>(IDictionary<string, T> data, ContentAssetRequest req)
    {
        var limit = req.KeysLimit ?? 50;
        var nestedItemsLimit = req.NestedItemsLimit ?? DefaultNestedItemsLimit;
        var summary = new JsonObject
        {
            ["count"] = data.Count,
        };

        if (req.IncludeKeys)
            summary["keys"] = new JsonArray(data.Keys.Take(limit).Select(k => (JsonNode?)k).ToArray());

        if (req.EntryKeys is { Length: > 0 })
        {
            var entries = new JsonObject();
            foreach (var key in req.EntryKeys)
            {
                if (data.TryGetValue(key, out var value))
                {
                    entries[key] = new JsonObject
                    {
                        ["exists"] = true,
                        ["value"] = SummarizeValue(value, nestedItemsLimit),
                    };
                }
                else
                {
                    entries[key] = new JsonObject { ["exists"] = false };
                }
            }
            summary["entries"] = entries;
        }

        return summary;
    }

    private static JsonNode? SummarizeValue(object? value, int nestedItemsLimit, int depth = 0)
    {
        if (value is null) return null;
        if (value is string s) return s;
        if (value is bool b) return b;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is float f) return f;
        if (value is double d) return d;
        if (value is decimal m) return (double)m;
        if (value is IEnumerable enumerable and not string)
        {
            var count = 0;
            var items = new JsonArray();
            var includeItems = ShouldSummarizeEnumerableItems(value);
            foreach (var item in enumerable)
            {
                if (includeItems && count < nestedItemsLimit)
                    items.Add(SummarizeValue(item, nestedItemsLimit, depth + 1));
                count++;
            }

            var collection = new JsonObject
            {
                ["runtime_type"] = value.GetType().FullName ?? value.GetType().Name,
                ["count"] = count,
            };

            if (includeItems)
            {
                collection["items_limit"] = nestedItemsLimit;
                collection["items_truncated"] = count > nestedItemsLimit;
                collection["items"] = items;
            }

            return collection;
        }

        var text = value.ToString();
        var preview = text is null
            ? string.Empty
            : text.Length <= 160 ? text : text[..160];
        var obj = new JsonObject
        {
            ["runtime_type"] = value.GetType().FullName ?? value.GetType().Name,
            ["string_preview"] = preview,
        };

        foreach (var prop in value.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (prop.GetIndexParameters().Length != 0)
                continue;

            object? propValue;
            try { propValue = prop.GetValue(value); }
            catch { continue; }

            if (IsScalar(propValue))
            {
                obj[ToSnakeCase(prop.Name)] = SummarizeValue(propValue, nestedItemsLimit);
            }
            else if (propValue is IEnumerable and not string)
            {
                obj[ToSnakeCase(prop.Name)] = SummarizeValue(propValue, nestedItemsLimit, depth);
            }
            else if (ShouldSummarizeNestedObject(propValue, depth))
            {
                obj[ToSnakeCase(prop.Name)] = SummarizeValue(propValue, nestedItemsLimit, depth + 1);
            }
        }

        foreach (var field in value.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            object? fieldValue;
            try { fieldValue = field.GetValue(value); }
            catch { continue; }

            if (IsScalar(fieldValue))
            {
                obj[ToSnakeCase(field.Name)] = SummarizeValue(fieldValue, nestedItemsLimit);
            }
            else if (fieldValue is IEnumerable and not string)
            {
                obj[ToSnakeCase(field.Name)] = SummarizeValue(fieldValue, nestedItemsLimit, depth);
            }
            else if (ShouldSummarizeNestedObject(fieldValue, depth))
            {
                obj[ToSnakeCase(field.Name)] = SummarizeValue(fieldValue, nestedItemsLimit, depth + 1);
            }
        }

        return obj;
    }

    private static bool ShouldSummarizeEnumerableItems(object value)
    {
        if (value is IDictionary)
            return false;

        var type = value.GetType();
        if (type == typeof(string))
            return false;

        return true;
    }

    private static bool ShouldSummarizeNestedObject(object? value, int depth)
    {
        if (value is null || depth >= MaxObjectDepth)
            return false;
        if (value is string or IEnumerable)
            return false;

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(Type))
            return false;
        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return false;

        return true;
    }

    private static bool IsScalar(object? value)
        => value is null or string or bool or int or long or float or double or decimal;

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    chars.Add('_');
                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(c);
            }
        }

        return new string(chars.ToArray());
    }

    private static ContentAssetResult Found(string name, string kind, Type runtimeType, JsonObject summary)
        => new()
        {
            Name = name,
            Exists = true,
            Kind = kind,
            RuntimeType = runtimeType.FullName ?? runtimeType.Name,
            Summary = summary,
        };

    private static ContentAssetResult Missing(string name)
        => new()
        {
            Name = name,
            Exists = false,
            Kind = "missing",
            RuntimeType = string.Empty,
            Summary = new JsonObject(),
        };
}
