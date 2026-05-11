using System.Collections.Generic;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Tests;

internal class FakeFishingWorld : IFishingWorld
{
    public bool IsWater { get; set; } = true;
    public bool IsFishable { get; set; } = true;
    public bool HasNoFishing { get; set; }
    public string Season { get; set; } = "spring";
    public int TimeOfDay { get; set; } = 900;
    public string Weather { get; set; } = "sunny";
    public double DailyLuck { get; set; } = 0.025;
    public FakeFishingLocation Location { get; } = new();

    public static FakeFishingWorld Sample()
    {
        var world = new FakeFishingWorld();
        world.Location.World = world;
        return world;
    }

    public IFishingLocation ResolveLocation(string? name) => Location;

    public TilePoint ResolveTile(IFishingLocation location, int? x, int? y)
        => new() { X = x ?? 0, Y = y ?? 0 };
}

internal sealed class FakeFishingLocation : IFishingLocation
{
    public FakeFishingWorld World { get; set; } = null!;
    public string Location => "Custom_FerngillRepublicFrontier";
    public string LocationName => "Frontier Farm";
    public string LocationType => "GameLocation";
    public string MapFish => "128 .08 129 .2";

    public IReadOnlyList<FishingAreaSummary> FishAreas { get; } =
    [
        new FishingAreaSummary
        {
            Id = "Ocean",
            DisplayName = "Ocean",
            Position = new RectangleSummary { X = 0, Y = 140, Width = 155, Height = 15 },
            CrabPotFishTypes = { "ocean" },
        },
    ];

    public IReadOnlyList<FishingCatchCandidate> DataLocationCandidates { get; } =
    [
        new FishingCatchCandidate
        {
            Id = "FlashShifter.FrontierFarm_Starfish",
            ItemId = "(O)FlashShifter.StardewValleyExpandedCP_Starfish",
            QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Starfish",
            DisplayName = "Starfish",
            Type = "fish",
            FishAreaId = "Ocean",
            Condition = "LOCATION_Season Here Spring Summer Fall",
            Season = "spring",
            Source = "data_locations",
            Raw = "starfish raw",
        },
        new FishingCatchCandidate
        {
            Id = "FlashShifter.FrontierFarm_Riverfish",
            ItemId = "(O)FlashShifter.StardewValleyExpandedCP_Riverfish",
            QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Riverfish",
            DisplayName = "Riverfish",
            Type = "fish",
            FishAreaId = "River",
            Season = "spring",
            Source = "data_locations",
            Raw = "river raw",
        },
        new FishingCatchCandidate
        {
            Id = "FlashShifter.FrontierFarm_Winterfish",
            ItemId = "(O)FlashShifter.StardewValleyExpandedCP_Winterfish",
            QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Winterfish",
            DisplayName = "Winterfish",
            Type = "fish",
            FishAreaId = "Ocean",
            Season = "winter",
            Source = "data_locations",
            Raw = "winter raw",
        },
        new FishingCatchCandidate
        {
            Id = "FlashShifter.FrontierFarm_Rainfish",
            ItemId = "(O)FlashShifter.StardewValleyExpandedCP_Rainfish",
            QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Rainfish",
            DisplayName = "Rainfish",
            Type = "fish",
            FishAreaId = "Ocean",
            Season = "spring",
            Weather = "rain",
            Source = "data_locations",
            Raw = "rain raw",
        },
        new FishingCatchCandidate
        {
            Id = "FlashShifter.FrontierFarm_Nightfish",
            ItemId = "(O)FlashShifter.StardewValleyExpandedCP_Nightfish",
            QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Nightfish",
            DisplayName = "Nightfish",
            Type = "fish",
            FishAreaId = "Ocean",
            Season = "spring",
            TimeRange = "1800-2200",
            Source = "data_locations",
            Raw = "night raw",
        },
    ];

    public bool IsWater(TilePoint tile) => World.IsWater;

    public bool IsFishable(TilePoint tile) => World.IsFishable;

    public bool HasNoFishing(TilePoint tile) => World.HasNoFishing;

    public string ResolveFishAreaId(TilePoint tile) => "Ocean";

    public IReadOnlyList<FishingTileLayerProperties> TileProperties(TilePoint tile) =>
    [
        new FishingTileLayerProperties { Layer = "Back", Properties = { ["Water"] = "T" } },
    ];

    public string DisplayNameForItem(string itemIdOrQualifiedId)
        => itemIdOrQualifiedId == "128" ? "Pufferfish" : string.Empty;
}
