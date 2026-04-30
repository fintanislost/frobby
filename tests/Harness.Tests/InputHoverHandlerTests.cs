using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class InputHoverHandlerTests
{
    public InputHoverHandlerTests()
    {
        ControlledCursor.Clear();
    }

    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => InputHoverHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"y\":134}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputHoverHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":144}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputHoverHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact]
    public void Handle_NegativeCoordinates_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":-1,\"y\":134}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputHoverHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":144,\"y\":134}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputHoverHandler.Handle(p, () => null, () => 0));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_HoversActiveMenuAndReturnsTick()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"x\":144,\"y\":134}").RootElement;

        var result = InputHoverHandler.Handle(p, () => menu, () => 1234);
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal((144, 134), menu.LastHover);
        Assert.True(ControlledCursor.TryGet(out var x, out var y));
        Assert.Equal((144, 134), (x, y));
    }

    private sealed class CapturingMenu : IClickableMenu
    {
        public (int X, int Y)? LastHover { get; private set; }

        public override void performHoverAction(int x, int y)
            => LastHover = (x, y);
    }
}
