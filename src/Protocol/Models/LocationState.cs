using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of a <c>GameLocation</c>. Response shape of <c>state.location</c>.</summary>
public sealed class LocationState
{
    /// <summary>Location name, e.g. <c>Farm</c>, <c>SeedShop</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable unique location name when Stardew exposes one; falls back to <see cref="Name"/>.</summary>
    public string UniqueName { get; set; } = string.Empty;

    /// <summary>True if the location is an outdoor area.</summary>
    public bool IsOutdoors { get; set; }

    /// <summary>Map width in tiles, or zero when no runtime map is loaded.</summary>
    public int MapWidth { get; set; }

    /// <summary>Map height in tiles, or zero when no runtime map is loaded.</summary>
    public int MapHeight { get; set; }

    /// <summary>Runtime warp exits declared on this location.</summary>
    public List<WarpSummary> Warps { get; set; } = new();

    /// <summary>NPCs currently in this location.</summary>
    public List<NpcSummary> Npcs { get; set; } = new();

    /// <summary>Placeable objects such as crops and crafted items in this location.</summary>
    public List<ObjectSummary> Objects { get; set; } = new();

    /// <summary>Transient world debris such as dropped item chunks and combat loot.</summary>
    public List<DebrisSummary> Debris { get; set; } = new();

    /// <summary>Furniture placed in this location.</summary>
    public List<FurnitureSummary> Furniture { get; set; } = new();

    /// <summary>Terrain features (tilled dirt, grass, trees) in this location.</summary>
    public List<TerrainSummary> Terrain { get; set; } = new();

    /// <summary>Resource clumps and other large world objects in this location.</summary>
    public List<ResourceClumpSummary> ResourceClumps { get; set; } = new();

    /// <summary>Hostile monsters currently in this location, separated from social NPCs.</summary>
    public List<MonsterSummary> Monsters { get; set; } = new();
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
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string RuntimeType { get; set; } = string.Empty;
    public bool BigCraftable { get; set; }
    public bool? ReadyForHarvest { get; set; }
    public string? HeldObjectId { get; set; }
    public string? HeldObjectQualifiedId { get; set; }
    public string? HeldObjectName { get; set; }
    public int? Category { get; set; }
    public int? Stack { get; set; }
    public int? Quality { get; set; }
    public bool IsChest { get; set; }
    public int? ItemCount { get; set; }
    public bool? ItemsTruncated { get; set; }
    public List<ContainedItemSummary> Items { get; set; } = new();
}

/// <summary>Item descriptor for an object-owned container such as a chest.</summary>
public sealed class ContainedItemSummary
{
    public int Slot { get; set; }
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Stack { get; set; }
    public int? Category { get; set; }
    public int? Quality { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}

/// <summary>Transient debris descriptor. Some fields are best-effort because Stardew debris can be non-item visual debris.</summary>
public sealed class DebrisSummary
{
    public TilePoint Tile { get; set; } = new();
    public PixelPoint? Pixel { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Stack { get; set; }
    public int? Quality { get; set; }
    public int? Category { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
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

/// <summary>Resource clump or large map object descriptor. <see cref="Kind"/> is the CLR type name.</summary>
public sealed class ResourceClumpSummary
{
    public TilePoint Tile { get; set; } = new();
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Health { get; set; }
}

/// <summary>Hostile creature descriptor for a location snapshot. <see cref="Type"/> is the CLR type name.</summary>
public sealed class MonsterSummary
{
    public TilePoint Tile { get; set; } = new();

    /// <summary>Run-local Frobby monster identity. Not save-stable.</summary>
    public string? MonsterId { get; set; }

    /// <summary>Optional Frobby lab label assigned by tests.</summary>
    public string? Label { get; set; }

    /// <summary>True when this monster was spawned by the Frobby Combat Lab.</summary>
    public bool? SpawnedByFrobby { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? Health { get; set; }
    public int? MaxHealth { get; set; }
    public int? Damage { get; set; }

    /// <summary>Optional vanilla monster revive countdown, exposed when the runtime monster has one.</summary>
    public int? ReviveTimer { get; set; }

    /// <summary>Runtime sprite texture asset path when Stardew or the mod exposes one.</summary>
    public string? SpriteTexture { get; set; }
}

/// <summary>Minimal runtime warp descriptor for a location snapshot.</summary>
public sealed class WarpSummary
{
    public TilePoint Source { get; set; } = new();
    public string TargetLocation { get; set; } = string.Empty;
    public TilePoint Target { get; set; } = new();
}
