using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateMapTileHandlerTests
{
    [Fact]
    public void Handle_MalformedX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Farm\",\"x\":\"not-a-number\",\"y\":0}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => StateMapTileHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NegativeX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Farm\",\"x\":-1,\"y\":0}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => StateMapTileHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_NegativeY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"Farm\",\"x\":0,\"y\":-1}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => StateMapTileHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (current location map layers).")]
    public void Handle_NoArgs_SnapshotsCurrentFarmerTile() { /* integration */ }
}
