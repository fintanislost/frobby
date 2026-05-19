using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldUseToolHandlerTests
{
    [Fact]
    public void Handle_MissingTool_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("tool", ex.Message);
    }

    [Fact]
    public void Handle_UnsupportedTool_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Pickaxe\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("only supports Hoe", ex.Message);
    }

    [Fact]
    public void Handle_PartialTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("both x and y", ex.Message);
    }

    [Fact]
    public void Handle_NegativeTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":-1,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => WorldUseToolHandler.Handle(p, new FakeUseToolWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(">= 0", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldUseToolHandler.Handle(p, new FakeUseToolWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_LocationGuardMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"location\":\"Desert\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldUseToolHandler.Handle(p, new FakeUseToolWorld { CurrentLocationName = "Farm" }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("location guard expected Desert", ex.Message);
    }

    [Fact]
    public void Handle_MissingHoe_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"tool\":\"Hoe\",\"x\":9,\"y\":43}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            WorldUseToolHandler.Handle(p, new FakeUseToolWorld { HasHoe = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("could not find Hoe", ex.Message);
    }

    [Fact]
    public void Handle_HoeAtTileFacesAndInvokesNativeToolUse()
    {
        var world = new FakeUseToolWorld { CurrentLocationName = "Desert" };
        var p = JsonDocument.Parse("{\"tool\":\"hoe\",\"location\":\"Desert\",\"x\":9,\"y\":43,\"facing\":\"down\",\"power\":0}").RootElement;

        var result = WorldUseToolHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("down", world.FacedDirection);
        Assert.Equal(9, world.InvokedX);
        Assert.Equal(43, world.InvokedY);
        Assert.Equal(0, world.InvokedPower);
        Assert.Equal(1, world.SelectCount);
        Assert.Contains("\"tool\":\"Hoe\"", json);
        Assert.Contains("\"location\":\"Desert\"", json);
        Assert.Contains("\"selected_tool_index\":1", json);
        Assert.Contains("\"invoked\":true", json);
    }

    private sealed class FakeUseToolWorld : IUseToolWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public string CurrentLocationName { get; set; } = "Desert";
        public int Tick { get; set; } = 456;
        public bool HasHoe { get; set; } = true;
        public string? FacedDirection { get; private set; }
        public int? InvokedX { get; private set; }
        public int? InvokedY { get; private set; }
        public int? InvokedPower { get; private set; }
        public int SelectCount { get; private set; }

        public UseToolSelectedItem SelectTool(string tool)
        {
            SelectCount++;
            if (!HasHoe)
                throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "world.use_tool could not find Hoe in the farmer inventory");

            return new UseToolSelectedItem("Hoe", "(T)Hoe", "Hoe", "Hoe", 1);
        }

        public void FaceDirection(string direction) => FacedDirection = direction;

        public void UseToolAtTile(int x, int y, int power)
        {
            InvokedX = x;
            InvokedY = y;
            InvokedPower = power;
        }
    }
}
