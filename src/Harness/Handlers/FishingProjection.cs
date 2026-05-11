using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.GameData.Locations;
using xTile.Layers;

namespace SdvTestFramework.Harness.Handlers;

internal interface IFishingWorld
{
    IFishingLocation ResolveLocation(string? locationName);

    TilePoint ResolveTile(IFishingLocation location, int? x, int? y);

    string Season { get; }

    int TimeOfDay { get; }

    string Weather { get; }

    double DailyLuck { get; }
}

internal interface IFishingLocation
{
    string Location { get; }

    string LocationName { get; }

    string LocationType { get; }

    string MapFish { get; }

    bool IsWater(TilePoint tile);

    bool IsFishable(TilePoint tile);

    bool HasNoFishing(TilePoint tile);

    string ResolveFishAreaId(TilePoint tile);

    IReadOnlyList<FishingTileLayerProperties> TileProperties(TilePoint tile);

    IReadOnlyList<FishingAreaSummary> FishAreas { get; }

    IReadOnlyList<FishingCatchCandidate> DataLocationCandidates { get; }

    string DisplayNameForItem(string itemIdOrQualifiedId);
}

internal static class FishingProjection
{
    private const string DataLocationsSource = "data_locations";
    private const string MapFishSource = "map_fish";
    private const int MaxRawLength = 256;

    public static FishingContextState BuildContext(IFishingWorld world, FishingContextRequest request)
    {
        ValidateTile(request.X, request.Y);

        var location = world.ResolveLocation(request.Location);
        var tile = world.ResolveTile(location, request.X, request.Y);
        var hasNoFishing = location.HasNoFishing(tile);
        var isWater = location.IsWater(tile);
        var isFishable = !hasNoFishing && location.IsFishable(tile);
        var blockedReason = isFishable
            ? string.Empty
            : hasNoFishing
                ? "no_fishing"
                : isWater
                    ? "not_fishable"
                    : "not_water";

        return new FishingContextState
        {
            Location = location.Location,
            LocationName = location.LocationName,
            LocationType = location.LocationType,
            Tile = tile,
            Season = request.Season ?? world.Season,
            TimeOfDay = request.TimeOfDay ?? world.TimeOfDay,
            Weather = request.Weather ?? world.Weather,
            DailyLuck = request.Luck ?? world.DailyLuck,
            IsWater = isWater,
            IsFishable = isFishable,
            HasNoFishing = hasNoFishing,
            FishAreaId = location.ResolveFishAreaId(tile),
            BlockedReason = blockedReason,
            MapFish = location.MapFish,
            TileProperties = request.IncludeTileLayers ? location.TileProperties(tile).ToList() : [],
            LocationFishAreas = location.FishAreas.ToList(),
        };
    }

    public static FishingTableState BuildTable(IFishingWorld world, FishingTableRequest request)
    {
        if (request.Limit <= 0)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.limit must be > 0");
        }

        var location = world.ResolveLocation(request.Location);
        var context = BuildContext(world, request);
        var dataLocationCandidates = location.DataLocationCandidates;
        var candidates = new List<FishingCatchCandidate>();
        if (context.IsFishable)
        {
            candidates.AddRange(dataLocationCandidates
                .Where(candidate => MatchesContext(candidate, context))
                .Select(candidate => WithRaw(candidate, request.IncludeRaw)));
            candidates.AddRange(ParseMapFish(location.MapFish, location, request.IncludeRaw));
        }

        var sources = new List<string>();
        if (dataLocationCandidates.Count > 0)
        {
            sources.Add(DataLocationsSource);
        }

        if (!string.IsNullOrWhiteSpace(location.MapFish))
        {
            sources.Add(MapFishSource);
        }

        return new FishingTableState
        {
            Context = context,
            Candidates = candidates.Take(request.Limit).ToList(),
            RawSources = sources.Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    private static bool MatchesContext(FishingCatchCandidate candidate, FishingContextState context)
    {
        return MatchesFishArea(candidate.FishAreaId, context.FishAreaId)
            && MatchesText(candidate.Season, context.Season)
            && MatchesText(candidate.Weather, context.Weather)
            && MatchesTimeRange(candidate.TimeRange, context.TimeOfDay);
    }

    private static bool MatchesFishArea(string candidateFishAreaId, string contextFishAreaId)
    {
        if (string.IsNullOrWhiteSpace(candidateFishAreaId))
        {
            return true;
        }

        return string.Equals(candidateFishAreaId, contextFishAreaId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesText(string candidateValue, string contextValue)
    {
        if (string.IsNullOrWhiteSpace(candidateValue) || string.IsNullOrWhiteSpace(contextValue))
        {
            return true;
        }

        var values = candidateValue.Split([' ', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Any(value => string.Equals(value, contextValue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesTimeRange(string candidateTimeRange, int contextTime)
    {
        if (string.IsNullOrWhiteSpace(candidateTimeRange) || contextTime <= 0)
        {
            return true;
        }

        var parts = candidateTimeRange.Split(['-', '/', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return true;
        }

        return start <= end
            ? contextTime >= start && contextTime <= end
            : contextTime >= start || contextTime <= end;
    }

    private static FishingCatchCandidate WithRaw(FishingCatchCandidate candidate, bool includeRaw)
    {
        return new FishingCatchCandidate
        {
            Id = candidate.Id,
            ItemId = candidate.ItemId,
            QualifiedId = candidate.QualifiedId,
            DisplayName = candidate.DisplayName,
            Type = candidate.Type,
            FishAreaId = candidate.FishAreaId,
            Chance = candidate.Chance,
            Condition = candidate.Condition,
            Season = candidate.Season,
            TimeRange = candidate.TimeRange,
            Weather = candidate.Weather,
            Source = candidate.Source,
            Raw = includeRaw ? candidate.Raw : string.Empty,
        };
    }

    internal static string BuildDataLocationRaw(object spawn)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = DataLocationsSource,
            ["id"] = ReflectionValue.ReadString(spawn, "Id", "ID"),
            ["item_id"] = ReflectionValue.ReadString(spawn, "ItemId", "ItemID", "QualifiedItemId"),
            ["fish_area_id"] = ReflectionValue.ReadString(spawn, "FishAreaId", "FishAreaID"),
            ["chance"] = ReflectionValue.ReadDouble(spawn, "Chance"),
            ["precedence"] = ReflectionValue.ReadInt(spawn, "Precedence"),
            ["can_be_inherited"] = ReflectionValue.ReadBool(spawn, "CanBeInherited"),
            ["condition"] = ReflectionValue.ReadString(spawn, "Condition"),
            ["season"] = ReflectionValue.ReadString(spawn, "Season"),
            ["time_range"] = ReflectionValue.ReadString(spawn, "TimeRange"),
            ["weather"] = ReflectionValue.ReadString(spawn, "Weather"),
        };

        var raw = JsonSerializer.Serialize(payload.Where(pair => pair.Value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true,
        }).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        return raw.Length <= MaxRawLength ? raw : raw[..(MaxRawLength - 3)] + "...";
    }

    internal static IReadOnlyList<FishingCatchCandidate> ProjectDataLocationCandidates(
        object? locationData,
        object? defaultLocationData,
        IFishingLocation location)
    {
        return EnumerateSpawns(defaultLocationData)
            .Concat(EnumerateSpawns(locationData))
            .OrderBy(spawn => ReflectionValue.ReadInt(spawn.Value, "Precedence") ?? 0)
            .ThenBy(spawn => spawn.Index)
            .Select(spawn => ProjectDataLocationCandidate(spawn.Value, location))
            .Where(candidate => candidate is not null)
            .Cast<FishingCatchCandidate>()
            .ToList();
    }

    private static IEnumerable<(object Value, int Index)> EnumerateSpawns(object? locationData)
    {
        var rawFish = ReflectionValue.ReadRaw(locationData, "Fish");
        var index = 0;
        foreach (var spawn in ReflectionValue.ReadEnumerable(rawFish))
        {
            if (spawn is not null)
            {
                yield return (spawn, index);
            }

            index++;
        }
    }

    private static FishingCatchCandidate? ProjectDataLocationCandidate(object spawn, IFishingLocation location)
    {
        var id = ReflectionValue.ReadString(spawn, "Id", "ID");
        var itemId = ReflectionValue.ReadString(spawn, "ItemId", "ItemID", "QualifiedItemId");
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        return new FishingCatchCandidate
        {
            Id = string.IsNullOrWhiteSpace(id) ? itemId : id,
            ItemId = itemId,
            QualifiedId = NormalizeQualifiedObjectId(itemId),
            DisplayName = location.DisplayNameForItem(itemId),
            Type = "fish",
            FishAreaId = ReflectionValue.ReadString(spawn, "FishAreaId", "FishAreaID"),
            Chance = ReflectionValue.ReadDouble(spawn, "Chance"),
            Season = ReflectionValue.ReadString(spawn, "Season"),
            Weather = ReflectionValue.ReadString(spawn, "Weather"),
            TimeRange = ReflectionValue.ReadString(spawn, "TimeRange"),
            Condition = ReflectionValue.ReadString(spawn, "Condition"),
            Source = DataLocationsSource,
            Raw = BuildDataLocationRaw(spawn),
        };
    }

    private static List<FishingCatchCandidate> ParseMapFish(string mapFish, IFishingLocation location, bool includeRaw)
    {
        if (string.IsNullOrWhiteSpace(mapFish))
        {
            return [];
        }

        var parts = mapFish.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = new List<FishingCatchCandidate>();
        for (var index = 0; index < parts.Length; index += 2)
        {
            var id = parts[index];
            var chance = index + 1 < parts.Length && double.TryParse(parts[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (double?)null;
            var raw = index + 1 < parts.Length ? $"{id} {parts[index + 1]}" : id;

            candidates.Add(new FishingCatchCandidate
            {
                Id = id,
                ItemId = id,
                QualifiedId = NormalizeQualifiedObjectId(id),
                DisplayName = location.DisplayNameForItem(id),
                Type = "fish",
                Chance = chance,
                Source = MapFishSource,
                Raw = includeRaw ? raw : string.Empty,
            });
        }

        return candidates;
    }

    private static void ValidateTile(int? x, int? y)
    {
        if (x is < 0 || y is < 0)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x and params.y must be >= 0");
        }
    }

    private static string NormalizeQualifiedObjectId(string raw)
    {
        return string.IsNullOrWhiteSpace(raw) || raw.StartsWith("(", StringComparison.Ordinal)
            ? raw
            : $"(O){raw}";
    }
}

internal sealed class SdvFishingWorld : IFishingWorld
{
    public string Season => Game1.currentSeason ?? string.Empty;

    public int TimeOfDay => Game1.timeOfDay;

    public string Weather
    {
        get
        {
            if (Game1.isLightning)
            {
                return "storm";
            }

            if (Game1.isRaining)
            {
                return "rain";
            }

            if (Game1.isSnowing)
            {
                return "snow";
            }

            return "sunny";
        }
    }

    public double DailyLuck => Game1.player?.DailyLuck ?? 0d;

    public IFishingLocation ResolveLocation(string? locationName)
    {
        RpcPreconditions.RequireWorldReady();

        var location = string.IsNullOrWhiteSpace(locationName)
            ? Game1.currentLocation
            : Game1.getLocationFromName(locationName);

        if (location is null)
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"No location named '{locationName}' is loaded");
        }

        return new SdvFishingLocation(location);
    }

    public TilePoint ResolveTile(IFishingLocation location, int? x, int? y)
    {
        if (x.HasValue && y.HasValue)
        {
            return new TilePoint { X = x.Value, Y = y.Value };
        }

        var tile = Game1.player?.TilePoint;
        if (tile is null)
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "A tile is required before the player is loaded");
        }

        return new TilePoint { X = x ?? tile.Value.X, Y = y ?? tile.Value.Y };
    }
}

internal sealed class SdvFishingLocation(GameLocation location) : IFishingLocation
{
    private readonly GameLocation _location = location;

    public string Location => _location.NameOrUniqueName ?? _location.Name ?? string.Empty;

    public string LocationName
    {
        get
        {
            var displayName = ReflectionValue.ReadString(_location, "DisplayName", "displayName");
            return string.IsNullOrWhiteSpace(displayName) ? Location : displayName;
        }
    }

    public string LocationType => _location.GetType().Name;

    public string MapFish => ReadMapProperty("Fish");

    public IReadOnlyList<FishingAreaSummary> FishAreas
    {
        get
        {
            var data = GetLocationData();
            var rawAreas = ReflectionValue.ReadRaw(data, "FishAreas");
            return ReflectionValue.ReadDictionary(rawAreas)
                .Select(entry => ProjectFishArea(entry.Key, entry.Value))
                .ToList();
        }
    }

    public IReadOnlyList<FishingCatchCandidate> DataLocationCandidates
    {
        get
        {
            return FishingProjection.ProjectDataLocationCandidates(GetLocationData(), GetDefaultLocationData(), this);
        }
    }

    public bool IsWater(TilePoint tile)
    {
        if (ReflectionValue.TryInvokeBool(_location, "isOpenWater", [tile.X, tile.Y], out var openWater))
        {
            return openWater;
        }

        try
        {
            return _location.isWaterTile(tile.X, tile.Y);
        }
        catch
        {
            return TileProperties(tile).Any(layer => layer.Properties.ContainsKey("Water"));
        }
    }

    public bool IsFishable(TilePoint tile)
    {
        if (ReflectionValue.TryInvokeBool(_location, "isTileFishable", [new Vector2(tile.X, tile.Y)], out var fishable))
        {
            return fishable;
        }

        if (ReflectionValue.TryInvokeBool(_location, "isTileFishable", [tile.X, tile.Y], out fishable))
        {
            return fishable;
        }

        return IsWater(tile) && !HasNoFishing(tile);
    }

    public bool HasNoFishing(TilePoint tile)
    {
        return TileProperties(tile).Any(layer => layer.Properties.ContainsKey("NoFishing"));
    }

    public string ResolveFishAreaId(TilePoint tile)
    {
        try
        {
            if (_location.TryGetFishAreaForTile(new Vector2(tile.X, tile.Y), out var fishAreaId, out _))
            {
                return fishAreaId ?? string.Empty;
            }
        }
        catch
        {
        }

        foreach (var area in FishAreas)
        {
            var position = area.Position;
            if (position is not null
                && tile.X >= position.X
                && tile.X < position.X + position.Width
                && tile.Y >= position.Y
                && tile.Y < position.Y + position.Height)
            {
                return area.Id;
            }
        }

        return string.Empty;
    }

    public IReadOnlyList<FishingTileLayerProperties> TileProperties(TilePoint tile)
    {
        var map = _location.Map;
        if (map is null)
        {
            return [];
        }

        var layers = new List<FishingTileLayerProperties>();
        foreach (var layerId in new[] { "Back", "Buildings", "Front" })
        {
            var layer = FindLayer(map, layerId);
            if (layer is null || tile.X < 0 || tile.Y < 0 || tile.X >= layer.LayerWidth || tile.Y >= layer.LayerHeight)
            {
                continue;
            }

            var layerTile = layer.Tiles[tile.X, tile.Y];
            if (layerTile is null)
            {
                continue;
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in layerTile.Properties.Keys)
            {
                properties[key] = layerTile.Properties[key]?.ToString() ?? string.Empty;
            }

            layers.Add(new FishingTileLayerProperties
            {
                Layer = layer.Id,
                Properties = properties,
            });
        }

        return layers;
    }

    public string DisplayNameForItem(string itemIdOrQualifiedId)
    {
        var qualifiedId = NormalizeQualifiedObjectId(itemIdOrQualifiedId);
        if (string.IsNullOrWhiteSpace(qualifiedId))
        {
            return string.Empty;
        }

        try
        {
            if (!ItemRegistry.Exists(qualifiedId))
            {
                return string.Empty;
            }

            var item = ItemRegistry.Create(qualifiedId);
            return item?.DisplayName ?? item?.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Layer? FindLayer(xTile.Map map, string layerId)
    {
        try
        {
            return map.GetLayer(layerId);
        }
        catch
        {
            return null;
        }
    }

    private LocationData? GetLocationData()
    {
        try
        {
            var locations = Game1.content.Load<Dictionary<string, LocationData>>("Data/Locations");
            foreach (var key in new[] { Location, _location.Name, _location.NameOrUniqueName }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
            {
                if (locations.TryGetValue(key, out var data))
                {
                    return data;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private LocationData? GetDefaultLocationData()
    {
        try
        {
            var locations = Game1.content.Load<Dictionary<string, LocationData>>("Data/Locations");
            return locations.TryGetValue("Default", out var data) ? data : null;
        }
        catch
        {
            return null;
        }
    }

    private string ReadMapProperty(string propertyName)
    {
        var map = _location.Map;
        if (map is null)
        {
            return string.Empty;
        }

        foreach (var key in map.Properties.Keys)
        {
            if (string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return map.Properties[key]?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static FishingAreaSummary ProjectFishArea(string id, object? data)
    {
        return new FishingAreaSummary
        {
            Id = id,
            DisplayName = ReflectionValue.ReadString(data, "DisplayName", "Name"),
            Position = ReflectionValue.ReadRectangleSummary(ReflectionValue.ReadRaw(data, "Position")),
            CrabPotFishTypes = ReflectionValue.ReadStringList(ReflectionValue.ReadRaw(data, "CrabPotFishTypes", "CrabPotFishType")).ToList(),
        };
    }

    private static string NormalizeQualifiedObjectId(string raw)
    {
        return string.IsNullOrWhiteSpace(raw) || raw.StartsWith("(", StringComparison.Ordinal)
            ? raw
            : $"(O){raw}";
    }
}

internal static class ReflectionValue
{
    public static object? ReadRaw(object? source, params string[] names)
    {
        if (source is null)
        {
            return null;
        }

        var type = source.GetType();
        foreach (var name in names)
        {
            try
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is not null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(source);
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field is not null)
                {
                    return field.GetValue(source);
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public static string ReadString(object? source, params string[] names)
    {
        var raw = ReadRaw(source, names);
        return raw?.ToString() ?? string.Empty;
    }

    public static double? ReadDouble(object? source, params string[] names)
    {
        var raw = ReadRaw(source, names);
        if (raw is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }
        catch
        {
            return double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
    }

    public static int? ReadInt(object? source, params string[] names)
    {
        var raw = ReadRaw(source, names);
        if (raw is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }
        catch
        {
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
    }

    public static bool? ReadBool(object? source, params string[] names)
    {
        var raw = ReadRaw(source, names);
        if (raw is null)
        {
            return null;
        }

        if (raw is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(raw.ToString(), out var parsed) ? parsed : null;
    }

    public static IReadOnlyList<string> ReadStringList(object? raw)
    {
        if (raw is null)
        {
            return [];
        }

        if (raw is string text)
        {
            return string.IsNullOrWhiteSpace(text) ? [] : [text];
        }

        if (raw is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>()
                .Select(value => value?.ToString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        return [];
    }

    public static RectangleSummary? ReadRectangleSummary(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw is Rectangle rectangle)
        {
            return new RectangleSummary
            {
                X = rectangle.X,
                Y = rectangle.Y,
                Width = rectangle.Width,
                Height = rectangle.Height,
            };
        }

        var x = ReadInt(raw, "X", "x");
        var y = ReadInt(raw, "Y", "y");
        var width = ReadInt(raw, "Width", "width");
        var height = ReadInt(raw, "Height", "height");
        return x.HasValue && y.HasValue && width.HasValue && height.HasValue
            ? new RectangleSummary
            {
                X = x.Value,
                Y = y.Value,
                Width = width.Value,
                Height = height.Value,
            }
            : null;
    }

    public static IEnumerable<(string Key, object? Value)> ReadDictionary(object? raw)
    {
        if (raw is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                yield return (entry.Key?.ToString() ?? string.Empty, entry.Value);
            }
        }
        else if (raw is IEnumerable enumerable and not string)
        {
            foreach (var entry in enumerable)
            {
                var key = ReadRaw(entry, "Key")?.ToString() ?? string.Empty;
                var value = ReadRaw(entry, "Value");
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return (key, value);
                }
            }
        }
    }

    public static IEnumerable<object?> ReadEnumerable(object? raw)
    {
        if (raw is IEnumerable enumerable and not string)
        {
            foreach (var value in enumerable)
            {
                yield return value;
            }
        }
    }

    public static bool TryInvokeBool(object source, string methodName, object?[] args, out bool value)
    {
        foreach (var method in source.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) || method.GetParameters().Length != args.Length)
            {
                continue;
            }

            try
            {
                var result = method.Invoke(source, args);
                if (result is bool boolResult)
                {
                    value = boolResult;
                    return true;
                }
            }
            catch
            {
            }
        }

        value = false;
        return false;
    }

}
