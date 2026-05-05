using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class InputHoverTextHandlerTests
{
    public InputHoverTextHandlerTests()
    {
        ControlledCursor.Clear();
    }

    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => InputHoverTextHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingText_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputHoverTextHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("text", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"text\":\"CONTINUE\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputHoverTextHandler.Handle(p, () => null, () => 0, () => Events(Label("CONTINUE"))));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("input.hover_text", ex.Message);
    }

    [Fact]
    public void Handle_NoMatchingText_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"text\":\"CONTINUE\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputHoverTextHandler.Handle(p, () => new CapturingMenu(), () => 0, () => Events(Label("CANCEL"))));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("CONTINUE", ex.Message);
    }

    [Fact]
    public void Handle_HoversCenterOfMatchingTextBounds()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"CONTINUE\"}").RootElement;

        var result = InputHoverTextHandler.Handle(p, () => menu, () => 1234, () => Events(Label("CONTINUE")));
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal((140, 214), menu.LastHover);
        Assert.True(ControlledCursor.TryGet(out var x, out var y));
        Assert.Equal((140, 214), (x, y));
    }

    [Fact]
    public void Handle_TextEquals_HoversExactTextInsteadOfInstructionSubstring()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text_equals\":\"CONTINUE\"}").RootElement;

        InputHoverTextHandler.Handle(
            p,
            () => menu,
            () => 0,
            () => Events(Label("Click CONTINUE to begin.", x: 10, y: 20), Label("CONTINUE", x: 100, y: 200)));

        Assert.Equal((140, 214), menu.LastHover);
    }

    [Fact]
    public void Handle_TextMatches_HoversRegexMatch()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text_matches\":\"^CASH [0-9,]+g$\"}").RootElement;

        InputHoverTextHandler.Handle(
            p,
            () => menu,
            () => 0,
            () => Events(Label("CASH", x: 10, y: 20), Label("CASH 1,000,000g", x: 100, y: 200)));

        Assert.Equal((140, 214), menu.LastHover);
    }

    [Fact]
    public void Handle_UsesOccurrenceAfterFiltering()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"BUY\",\"occurrence\":2}").RootElement;

        InputHoverTextHandler.Handle(
            p,
            () => menu,
            () => 0,
            () => Events(Label("BUY", x: 10, y: 20), Label("BUY", x: 100, y: 200)));

        Assert.Equal((140, 214), menu.LastHover);
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
        public (int X, int Y)? LastHover { get; private set; }

        public override void performHoverAction(int x, int y)
            => LastHover = (x, y);
    }
}
