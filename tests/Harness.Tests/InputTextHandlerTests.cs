using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework.Input;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputTextHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => InputTextHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingText_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"submit\":true}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputTextHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("text", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"text\":\"OE\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputTextHandler.Handle(p, () => null, () => 0));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_MenuWithTextInputPath_ReceivesCharactersAndSubmitKey()
    {
        var menu = new TextCapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"OE\",\"submit\":true}").RootElement;

        var result = InputTextHandler.Handle(p, () => menu, () => 1234);
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal(new[] { 'O', 'E' }, menu.Chars);
        Assert.Equal(new[] { Keys.Enter }, menu.Keys);
    }

    [Fact]
    public void Handle_MenuWithInheritedTextInputPath_UsesTextInputInsteadOfKeyFallback()
    {
        var menu = new DerivedTextCapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"OE\",\"submit\":true}").RootElement;

        InputTextHandler.Handle(p, () => menu, () => 0);

        Assert.Equal(new[] { 'O', 'E' }, menu.Chars);
        Assert.Equal(new[] { Keys.Enter }, menu.Keys);
    }

    [Fact]
    public void Handle_MenuWithoutTextInputPath_FallsBackToKeyPresses()
    {
        var menu = new KeyCapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"Az 09\",\"submit\":true}").RootElement;

        InputTextHandler.Handle(p, () => menu, () => 0);

        Assert.Equal(
            new[] { Keys.A, Keys.Z, Keys.Space, Keys.D0, Keys.D9, Keys.Enter },
            menu.Keys);
    }

    [Fact]
    public void Handle_UnsupportedFallbackCharacter_ThrowsInvalidParams()
    {
        var menu = new KeyCapturingMenu();
        var p = JsonDocument.Parse("{\"text\":\"A!\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputTextHandler.Handle(p, () => menu, () => 0));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unsupported character", ex.Message);
        Assert.Empty(menu.Keys);
    }

    private sealed class TextCapturingMenu : IClickableMenu
    {
        public List<char> Chars { get; } = new();
        public List<Keys> Keys { get; } = new();

        public void receiveTextInput(char c)
        {
            Chars.Add(c);
        }

        public override void receiveKeyPress(Keys key)
        {
            Keys.Add(key);
        }
    }

    private sealed class DerivedTextCapturingMenu : InheritedTextCapturingMenu
    {
    }

    private abstract class InheritedTextCapturingMenu : IClickableMenu
    {
        public List<char> Chars { get; } = new();
        public List<Keys> Keys { get; } = new();

        public void receiveTextInput(char c)
        {
            Chars.Add(c);
        }

        public override void receiveKeyPress(Keys key)
        {
            Keys.Add(key);
        }
    }

    private sealed class KeyCapturingMenu : IClickableMenu
    {
        public List<Keys> Keys { get; } = new();

        public override void receiveKeyPress(Keys key)
        {
            Keys.Add(key);
        }
    }
}
