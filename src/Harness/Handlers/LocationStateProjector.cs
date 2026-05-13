using System.Linq;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

internal static class LocationStateProjector
{
    public static LocationSummary ToSummary(GameLocation loc)
    {
        var (width, height) = GetMapSize(loc);
        var name = loc.Name ?? string.Empty;

        return new LocationSummary
        {
            Name = name,
            UniqueName = loc.NameOrUniqueName ?? name,
            IsOutdoors = loc.IsOutdoors,
            MapWidth = width,
            MapHeight = height,
            WarpCount = loc.warps?.Count ?? 0,
        };
    }

    public static LocationState ToState(GameLocation loc)
    {
        var (width, height) = GetMapSize(loc);
        var name = loc.Name ?? string.Empty;
        var state = new LocationState
        {
            Name = name,
            UniqueName = loc.NameOrUniqueName ?? name,
            IsOutdoors = loc.IsOutdoors,
            MapWidth = width,
            MapHeight = height,
            Warps = loc.warps?.Select(ToWarpSummary).ToList() ?? new(),
        };

        foreach (var npc in loc.characters)
        {
            if (LocationContentProjector.IsMonster(npc))
                continue;

            state.Npcs.Add(new NpcSummary
            {
                Name = npc.Name ?? string.Empty,
                Tile = new TilePoint { X = npc.TilePoint.X, Y = npc.TilePoint.Y },
            });
        }

        foreach (var kv in loc.Objects.Pairs)
        {
            state.Objects.Add(LocationContentProjector.ProjectObject(kv.Key, kv.Value));
        }

        state.Debris.AddRange(LocationContentProjector.ProjectDebris(loc));

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

        state.ResourceClumps.AddRange(LocationContentProjector.ProjectResourceClumps(loc));
        state.Monsters.AddRange(LocationContentProjector.ProjectMonsters(loc));

        return state;
    }

    private static WarpSummary ToWarpSummary(Warp warp)
    {
        return new WarpSummary
        {
            Source = new TilePoint { X = warp.X, Y = warp.Y },
            TargetLocation = warp.TargetName ?? string.Empty,
            Target = new TilePoint { X = warp.TargetX, Y = warp.TargetY },
        };
    }

    private static (int Width, int Height) GetMapSize(GameLocation loc)
    {
        var layer = loc.Map?.GetLayer("Back") ?? loc.Map?.Layers.FirstOrDefault();
        return layer is null
            ? (0, 0)
            : (layer.LayerWidth, layer.LayerHeight);
    }
}
