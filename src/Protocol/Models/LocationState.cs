using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of a <c>GameLocation</c>. Response shape of <c>state.location</c>.</summary>
public sealed class LocationState
{
    /// <summary>Location name, e.g. <c>Farm</c>, <c>SeedShop</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>True if the location is an outdoor area.</summary>
    public bool IsOutdoors { get; set; }

    /// <summary>NPCs currently in this location.</summary>
    public List<NpcSummary> Npcs { get; set; } = new();

    /// <summary>Placeable objects (crops, crafted items, debris) in this location.</summary>
    public List<ObjectSummary> Objects { get; set; } = new();

    /// <summary>Furniture placed in this location.</summary>
    public List<FurnitureSummary> Furniture { get; set; } = new();

    /// <summary>Terrain features (tilled dirt, grass, trees) in this location.</summary>
    public List<TerrainSummary> Terrain { get; set; } = new();
}

/// <summary>Minimal NPC descriptor for a location snapshot.</summary>
public sealed class NpcSummary
{
    public string Name { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
}

/// <summary>Minimal placeable-object descriptor for a location snapshot.</summary>
public sealed class ObjectSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Name { get; set; } = string.Empty;
}

/// <summary>Minimal furniture descriptor for a location snapshot.</summary>
public sealed class FurnitureSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Minimal terrain-feature descriptor for a location snapshot. <see cref="Kind"/> is the CLR type name.</summary>
public sealed class TerrainSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Kind { get; set; } = string.Empty;
}
