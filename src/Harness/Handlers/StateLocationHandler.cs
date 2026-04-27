using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.location</c> RPC method. Runs on the game thread.</summary>
public static class StateLocationHandler
{
    public const string Method = "state.location";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        // Optional `name` param — defaults to current location.
        GameLocation? loc = Game1.currentLocation;
        if (paramsElement is { } p && p.TryGetProperty("name", out var nameEl))
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrEmpty(name))
                loc = Game1.getLocationFromName(name);
        }

        if (loc is null)
            return ProtocolJson.ToElement(new LocationState { Name = string.Empty });

        var state = new LocationState
        {
            Name = loc.Name ?? string.Empty,
            IsOutdoors = loc.IsOutdoors,
        };

        foreach (var npc in loc.characters)
        {
            state.Npcs.Add(new NpcSummary
            {
                Name = npc.Name ?? string.Empty,
                Tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y },
            });
        }

        foreach (var kv in loc.Objects.Pairs)
        {
            state.Objects.Add(new ObjectSummary
            {
                Tile = new TilePoint { X = (int)kv.Key.X, Y = (int)kv.Key.Y },
                Name = kv.Value.Name ?? kv.Value.GetType().Name,
            });
        }

        foreach (var furniture in loc.furniture)
        {
            state.Furniture.Add(new FurnitureSummary
            {
                Tile = new TilePoint
                {
                    X = (int)furniture.TileLocation.X,
                    Y = (int)furniture.TileLocation.Y,
                },
                Id = furniture.QualifiedItemId ?? string.Empty,
                Name = furniture.Name ?? furniture.GetType().Name,
            });
        }

        foreach (var kv in loc.terrainFeatures.Pairs)
        {
            state.Terrain.Add(new TerrainSummary
            {
                Tile = new TilePoint { X = (int)kv.Key.X, Y = (int)kv.Key.Y },
                Kind = kv.Value.GetType().Name,
            });
        }

        return ProtocolJson.ToElement(state);
    }
}
