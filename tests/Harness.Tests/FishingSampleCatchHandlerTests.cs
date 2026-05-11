using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FishingSampleCatchHandlerTests
{
    [Fact]
    public void Handle_ReturnsRuntimeResultsAndRestoresState()
    {
        var world = FakeFishingSamplerWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingSampleCatchRequest
        {
            Location = "Desert",
            X = 28,
            Y = 6,
            Attempts = 2,
            Seed = 1234,
            RestoreState = true,
        });

        var result = FishingSampleCatchHandler.Handle(req, world);
        var sample = JsonSerializer.Deserialize<FishingSampleCatchResult>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Equal(2, sample.Attempts);
        Assert.True(sample.StateRestored);
        Assert.True(world.RestoreCalled);
        Assert.Collection(sample.Results,
            first => Assert.Equal("Pyramid Decal", first.DisplayName),
            second => Assert.Equal("Sandfish", second.DisplayName));
    }

    [Fact]
    public void Handle_RejectsNonPositiveAttempts()
    {
        var req = ProtocolJson.ToElement(new FishingSampleCatchRequest { Location = "Desert", X = 28, Y = 6, Attempts = 0 });

        var ex = Assert.Throws<JsonRpcException>(() => FishingSampleCatchHandler.Handle(req, FakeFishingSamplerWorld.Sample()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("attempts", ex.Message);
    }

    [Fact]
    public void Handle_RejectsLargeAttemptCount()
    {
        var req = ProtocolJson.ToElement(new FishingSampleCatchRequest { Location = "Desert", X = 28, Y = 6, Attempts = 101 });

        var ex = Assert.Throws<JsonRpcException>(() => FishingSampleCatchHandler.Handle(req, FakeFishingSamplerWorld.Sample()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("attempts", ex.Message);
    }
}
