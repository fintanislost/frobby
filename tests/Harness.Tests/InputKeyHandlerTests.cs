using System.Text.Json;
using Microsoft.Xna.Framework.Input;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputKeyHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => InputKeyHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_WhitespaceKey_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"key\":\"  \"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputKeyHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("key", ex.Message);
    }

    [Fact]
    public void Handle_UnknownKey_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"key\":\"DefinitelyNotAKey\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputKeyHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unknown key", ex.Message);
    }

    [Fact]
    public void Handle_UndefinedNumericKey_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"key\":\"999999\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => InputKeyHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unknown key", ex.Message);
    }

    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"key\":\"Enter\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() =>
            InputKeyHandler.Handle(p, () => null, () => 0));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_ActiveMenu_ReceivesKeyAndReturnsTick()
    {
        var menu = new CapturingMenu();
        var p = JsonDocument.Parse("{\"key\":\"enter\"}").RootElement;

        var result = InputKeyHandler.Handle(p, () => menu, () => 1234);
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal(Keys.Enter, menu.LastKey);
    }

    private sealed class CapturingMenu : IClickableMenu
    {
        public Keys? LastKey { get; private set; }

        public override void receiveKeyPress(Keys key)
        {
            LastKey = key;
        }
    }
}
