using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateFishingContextHandlerTests
{
    [Fact]
    public void Handle_ProjectsFishableTileWithFishAreaAndMapFish()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingContextRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
        });

        var result = StateFishingContextHandler.Handle(req, world);
        var state = JsonSerializer.Deserialize<FishingContextState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Equal("Custom_FerngillRepublicFrontier", state.Location);
        Assert.Equal("Ocean", state.FishAreaId);
        Assert.True(state.IsWater);
        Assert.True(state.IsFishable);
        Assert.False(state.HasNoFishing);
        Assert.Equal("128 .08 129 .2", state.MapFish);
        Assert.Collection(state.LocationFishAreas, area =>
        {
            Assert.Equal("Ocean", area.Id);
            Assert.Equal(0, area.Position!.X);
            Assert.Equal(140, area.Position.Y);
            Assert.Equal(155, area.Position.Width);
            Assert.Equal(15, area.Position.Height);
            Assert.Contains("ocean", area.CrabPotFishTypes);
        });
    }

    [Fact]
    public void Handle_ProjectsNoFishingBlockedReason()
    {
        var world = FakeFishingWorld.Sample();
        world.HasNoFishing = true;
        world.IsFishable = false;
        var req = ProtocolJson.ToElement(new FishingContextRequest { Location = "Mountain", X = 45, Y = 31 });

        var result = StateFishingContextHandler.Handle(req, world);
        var state = JsonSerializer.Deserialize<FishingContextState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.True(state.HasNoFishing);
        Assert.False(state.IsFishable);
        Assert.Equal("no_fishing", state.BlockedReason);
    }

    [Fact]
    public void Handle_RejectsNegativeTile()
    {
        var req = ProtocolJson.ToElement(new FishingContextRequest { Location = "Mountain", X = -1, Y = 0 });

        var ex = Assert.Throws<JsonRpcException>(() => StateFishingContextHandler.Handle(req, FakeFishingWorld.Sample()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }
}
