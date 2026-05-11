using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class FishingStateSerializationTests
{
    [Fact]
    public void Serialize_FishingContext_UsesSnakeCase()
    {
        var state = new FishingContextState
        {
            Location = "Custom_FerngillRepublicFrontier",
            LocationName = "Frontier Farm",
            LocationType = "GameLocation",
            Tile = new TilePoint { X = 12, Y = 144 },
            Season = "spring",
            TimeOfDay = 900,
            Weather = "sunny",
            DailyLuck = 0.025,
            IsWater = true,
            IsFishable = true,
            FishAreaId = "Ocean",
            MapFish = "128 .08 129 .2",
            HasNoFishing = false,
            TileProperties =
            {
                new FishingTileLayerProperties
                {
                    Layer = "Back",
                    Properties = { ["Water"] = "T" },
                },
            },
            LocationFishAreas =
            {
                new FishingAreaSummary
                {
                    Id = "Ocean",
                    DisplayName = "Ocean",
                    Position = new RectangleSummary { X = 0, Y = 140, Width = 155, Height = 15 },
                    CrabPotFishTypes = { "ocean" },
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"location_name\":\"Frontier Farm\"", json);
        Assert.Contains("\"time_of_day\":900", json);
        Assert.Contains("\"daily_luck\":0.025", json);
        Assert.Contains("\"is_fishable\":true", json);
        Assert.Contains("\"fish_area_id\":\"Ocean\"", json);
        Assert.Contains("\"has_no_fishing\":false", json);
        Assert.Contains("\"Water\":\"T\"", json);
        Assert.Contains("\"location_fish_areas\"", json);
        Assert.Contains("\"crab_pot_fish_types\":[\"ocean\"]", json);
    }

    [Fact]
    public void Serialize_FishingTable_IncludesCandidatesAndSources()
    {
        var table = new FishingTableState
        {
            Context = new FishingContextState { Location = "Beach", Tile = new TilePoint { X = 45, Y = 12 } },
            RawSources = { "map_fish", "data_fish" },
            Candidates =
            {
                new FishingCatchCandidate
                {
                    Id = "FlashShifter.StardewValleyExpandedCP_Starfish",
                    ItemId = "FlashShifter.StardewValleyExpandedCP_Starfish",
                    QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Starfish",
                    DisplayName = "Starfish",
                    Type = "fish",
                    FishAreaId = "Ocean",
                    Chance = 0.4,
                    Condition = "LOCATION_Season Here Spring Summer Fall",
                    Source = "data_locations",
                    Raw = "{\"ItemId\":\"(O)FlashShifter.StardewValleyExpandedCP_Starfish\"}",
                },
            },
        };

        var json = JsonSerializer.Serialize(table, ProtocolJson.Options);

        Assert.Contains("\"raw_sources\":[\"map_fish\",\"data_fish\"]", json);
        Assert.Contains("\"qualified_id\":\"(O)FlashShifter.StardewValleyExpandedCP_Starfish\"", json);
        Assert.Contains("\"fish_area_id\":\"Ocean\"", json);
        Assert.Contains("\"source\":\"data_locations\"", json);
    }

    [Fact]
    public void Serialize_FishingSample_IncludesResults()
    {
        var sample = new FishingSampleCatchResult
        {
            Context = new FishingContextState { Location = "Desert", Tile = new TilePoint { X = 28, Y = 6 } },
            Attempts = 2,
            StateRestored = true,
            Results =
            {
                new FishingCatchResult
                {
                    Attempt = 1,
                    ItemId = "2334",
                    QualifiedId = "(F)2334",
                    DisplayName = "Pyramid Decal",
                    Type = "furniture",
                    Stack = 1,
                    Quality = 0,
                    Category = 0,
                    RuntimeType = "Furniture",
                    Source = "runtime",
                    RawId = "2334",
                },
            },
        };

        var json = JsonSerializer.Serialize(sample, ProtocolJson.Options);

        Assert.Contains("\"state_restored\":true", json);
        Assert.Contains("\"attempts\":2", json);
        Assert.Contains("\"display_name\":\"Pyramid Decal\"", json);
        Assert.Contains("\"runtime_type\":\"Furniture\"", json);
    }
}
