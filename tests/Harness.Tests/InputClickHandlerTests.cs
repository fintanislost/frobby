using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputClickHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => InputClickHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"y\":134}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputClickHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":144}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputClickHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("y", ex.Message);
    }

    [Fact]
    public void Handle_NegativeCoordinates_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":-1,\"y\":134}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputClickHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void Handle_UnsupportedButton_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":144,\"y\":134,\"button\":\"middle\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputClickHandler.Handle(p, () => new CapturingMenu(), () => 0));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("button", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":144,\"y\":134}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickHandler.Handle(p, () => null, () => 0));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_LeftClick_ReceivesClickAndReturnsTick()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"x\":144,\"y\":134}").RootElement;

        var result = InputClickHandler.Handle(p, () => menu, () => 1234);
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal((144, 134), menu.LastLeftClick);
        Assert.Null(menu.LastRightClick);
    }

    [Fact]
    public void Handle_RightClick_ReceivesClickAndReturnsTick()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"x\":144,\"y\":134,\"button\":\"right\"}").RootElement;

        var result = InputClickHandler.Handle(p, () => menu, () => 1234);
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal((144, 134), menu.LastRightClick);
        Assert.Null(menu.LastLeftClick);
    }

    private sealed class CapturingMenu : IClickableMenu
    {
        public (int X, int Y)? LastLeftClick { get; private set; }
        public (int X, int Y)? LastRightClick { get; private set; }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
            => LastLeftClick = (x, y);

        public override void receiveRightClick(int x, int y, bool playSound = true)
            => LastRightClick = (x, y);
    }
}
