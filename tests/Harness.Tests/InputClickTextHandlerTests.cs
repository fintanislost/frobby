using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputClickTextHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => InputClickTextHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingText_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"button\":\"left\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputClickTextHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("text", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"text\":\"CONTINUE\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTextHandler.Handle(p, () => null, () => 0, () => Events(Label("CONTINUE"))));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_NoMatchingText_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"text\":\"CONTINUE\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTextHandler.Handle(p, () => new CapturingMenu(), () => 0, () => Events(Label("CANCEL"))));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("CONTINUE", ex.Message);
    }

    [Fact]
    public void Handle_ClicksCenterOfMatchingTextBounds()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"CONTINUE\"}").RootElement;

        var result = InputClickTextHandler.Handle(p, () => menu, () => 1234, () => Events(Label("CONTINUE")));
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal((140, 214), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_TextEquals_ClicksExactTextInsteadOfInstructionSubstring()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text_equals\":\"CONTINUE\"}").RootElement;

        InputClickTextHandler.Handle(
            p,
            () => menu,
            () => 0,
            () => Events(Label("Click CONTINUE to begin.", x: 10, y: 20), Label("CONTINUE", x: 100, y: 200)));

        Assert.Equal((140, 214), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_UsesOccurrenceAfterFiltering()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"BUY\",\"occurrence\":2}").RootElement;

        InputClickTextHandler.Handle(
            p,
            () => menu,
            () => 0,
            () => Events(Label("BUY", x: 10, y: 20), Label("BUY", x: 100, y: 200)));

        Assert.Equal((140, 214), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_RespectsBoundsIntersectsRect()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse(
            "{\"text\":\"BUY\",\"bounds_intersects_rect\":[90,190,120,40]}").RootElement;

        InputClickTextHandler.Handle(
            p,
            () => menu,
            () => 0,
            () => Events(Label("BUY", x: 10, y: 20), Label("BUY", x: 100, y: 200)));

        Assert.Equal((140, 214), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_RightClick_UsesRightClick()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"CONTINUE\",\"button\":\"right\"}").RootElement;

        InputClickTextHandler.Handle(p, () => menu, () => 0, () => Events(Label("CONTINUE")));

        Assert.Equal((140, 214), menu.LastRightClick);
        Assert.Null(menu.LastLeftClick);
    }

    private static TextDrawEvent Label(string text, int x = 100, int y = 200)
        => new()
        {
            Text = text,
            Position = new Vector2(x, y),
            Size = new Vector2(80, 28),
            Color = Color.White,
        };

    private static TextDrawEvent[] Events(params TextDrawEvent[] events) => events;

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
