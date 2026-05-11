using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateFishingTableHandlerTests
{
    [Fact]
    public void Handle_ProjectsDataLocationsAndMapFishCandidates()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingTableRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
            IncludeRaw = true,
            Limit = 20,
        });

        var result = StateFishingTableHandler.Handle(req, world);
        var table = JsonSerializer.Deserialize<FishingTableState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Contains("data_locations", table.RawSources);
        Assert.Contains("map_fish", table.RawSources);
        Assert.Contains(table.Candidates, c =>
            c.QualifiedId == "(O)FlashShifter.StardewValleyExpandedCP_Starfish"
            && c.FishAreaId == "Ocean"
            && c.Source == "data_locations"
            && c.Raw == "starfish raw");
        Assert.Contains(table.Candidates, c => c.ItemId == "128" && c.Source == "map_fish" && c.Raw == "128 .08");
    }

    [Fact]
    public void Handle_FiltersStructuredDataLocationCandidatesToRequestedContext()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingTableRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
            Season = "spring",
            TimeOfDay = 900,
            Weather = "sunny",
            Limit = 20,
        });

        var result = StateFishingTableHandler.Handle(req, world);
        var table = JsonSerializer.Deserialize<FishingTableState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Contains(table.Candidates, c => c.Id == "FlashShifter.FrontierFarm_Starfish");
        Assert.DoesNotContain(table.Candidates, c => c.Id == "FlashShifter.FrontierFarm_Riverfish");
        Assert.DoesNotContain(table.Candidates, c => c.Id == "FlashShifter.FrontierFarm_Winterfish");
        Assert.DoesNotContain(table.Candidates, c => c.Id == "FlashShifter.FrontierFarm_Rainfish");
        Assert.DoesNotContain(table.Candidates, c => c.Id == "FlashShifter.FrontierFarm_Nightfish");
    }

    [Fact]
    public void Handle_LeavesRawEmptyUnlessRequested()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingTableRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
            Limit = 20,
        });

        var result = StateFishingTableHandler.Handle(req, world);
        var table = JsonSerializer.Deserialize<FishingTableState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.All(table.Candidates, candidate => Assert.Equal(string.Empty, candidate.Raw));
    }

    [Fact]
    public void Handle_AppliesCandidateLimit()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingTableRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
            Limit = 1,
        });

        var result = StateFishingTableHandler.Handle(req, world);
        var table = JsonSerializer.Deserialize<FishingTableState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Single(table.Candidates);
    }

    [Fact]
    public void Handle_RejectsNonPositiveLimit()
    {
        var req = ProtocolJson.ToElement(new FishingTableRequest { Location = "Beach", X = 1, Y = 1, Limit = 0 });

        var ex = Assert.Throws<SdvTestFramework.Protocol.JsonRpcException>(() =>
            StateFishingTableHandler.Handle(req, FakeFishingWorld.Sample()));

        Assert.Contains("limit", ex.Message);
    }

    [Fact]
    public void BuildDataLocationRaw_ReturnsBoundedStructuredSnippet()
    {
        var spawn = new FakeRawSpawn
        {
            Id = "Example.LongRaw",
            ItemId = "(O)Example.LongRaw",
            FishAreaId = "Ocean",
            Chance = 0.25,
            Season = "spring",
            Weather = "sunny",
            TimeRange = "600-2600",
            Condition = new string('x', 300),
        };

        var raw = FishingProjection.BuildDataLocationRaw(spawn);

        Assert.Contains("\"id\":\"Example.LongRaw\"", raw);
        Assert.Contains("\"source\":\"data_locations\"", raw);
        Assert.True(raw.Length <= 256);
    }

    [Fact]
    public void ProjectDataLocationCandidates_MergesDefaultAndLocationFishByPrecedence()
    {
        var world = FakeFishingWorld.Sample();
        var defaultData = new FakeRawLocationData
        {
            Fish =
            {
                new FakeRawSpawn { Id = "DefaultLow", ItemId = "128", Precedence = 10 },
            },
        };
        var locationData = new FakeRawLocationData
        {
            Fish =
            {
                new FakeRawSpawn { Id = "LocationHigh", ItemId = "129", Precedence = -100 },
            },
        };

        var candidates = FishingProjection.ProjectDataLocationCandidates(locationData, defaultData, world.Location);

        Assert.Collection(candidates,
            candidate => Assert.Equal("LocationHigh", candidate.Id),
            candidate => Assert.Equal("DefaultLow", candidate.Id));
    }

    private sealed class FakeRawSpawn
    {
        public string Id { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string FishAreaId { get; set; } = string.Empty;
        public double Chance { get; set; }
        public int Precedence { get; set; }
        public bool CanBeInherited { get; set; } = true;
        public string Season { get; set; } = string.Empty;
        public string Weather { get; set; } = string.Empty;
        public string TimeRange { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
    }

    private sealed class FakeRawLocationData
    {
        public List<FakeRawSpawn> Fish { get; } = new();
    }
}
