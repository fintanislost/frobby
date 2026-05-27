using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputClickTileHandlerTests
{
    [Fact]
    public void Handle_MissingX_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params.x required", ex.Message);
    }

    [Fact]
    public void Handle_MissingY_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":9}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params.y required", ex.Message);
    }

    [Theory]
    [InlineData("{\"x\":-1,\"y\":8}")]
    [InlineData("{\"x\":9,\"y\":-1}")]
    public void Handle_NegativeTile_ThrowsInvalidParams(string json)
    {
        var p = JsonDocument.Parse(json).RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("must be non-negative", ex.Message);
    }

    [Theory]
    [InlineData("{\"x\":9,\"y\":8,\"screen_offset_x\":64}", "screen_offset_x")]
    [InlineData("{\"x\":9,\"y\":8,\"screen_offset_y\":-1}", "screen_offset_y")]
    public void Handle_InvalidScreenOffset_ThrowsInvalidParams(string json, string field)
    {
        var p = JsonDocument.Parse(json).RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Handle_RightClickConvertsTileToWorldAndScreenCoordinates()
    {
        var world = new FakeTileClickWorld
        {
            CurrentLocationName = "Frobby_CombatLab",
            ViewportX = 64,
            ViewportY = 128,
        };
        var p = JsonDocument.Parse(
            "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"button\":\"right\",\"screen_offset_x\":16,\"screen_offset_y\":48}")
            .RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.Equal("right", world.ClickedButton);
        Assert.Equal(592, world.ClickedWorldX);
        Assert.Equal(560, world.ClickedWorldY);
        Assert.Equal(528, world.ClickedScreenX);
        Assert.Equal(432, world.ClickedScreenY);
        Assert.True(world.ClickInvoked);
        Assert.True(result.Handled);
    }

    [Fact]
    public void Handle_RightClickOnNpcWithBlankDialogueMenu_UsesNpcFallback()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Claire",
            HasBlankDialogueMenuAfterClick = true,
        };
        var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.NpcFallbackInvoked);
        Assert.Equal(7, world.NpcFallbackTileX);
        Assert.Equal(5, world.NpcFallbackTileY);
        Assert.Equal("Claire", result.TargetNpcName);
        Assert.True(result.NpcFallbackUsed);
        Assert.True(result.Handled);
    }

    [Fact]
    public void Handle_RightClickOnNpcWithNoMenuAfterHandledClick_UsesNpcFallback()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Claire",
            HasActiveMenu = false,
        };
        var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.NpcFallbackInvoked);
        Assert.Equal("Claire", result.TargetNpcName);
        Assert.True(result.NpcFallbackUsed);
        Assert.True(result.Handled);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_ActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { HasActiveMenu = true }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("active menu", ex.Message);
    }

    [Fact]
    public void Handle_Warping_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { IsWarping = true }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("isWarping", ex.Message);
    }

    [Fact]
    public void Handle_Fading_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { IsFading = true }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("fade", ex.Message);
    }

    [Fact]
    public void Handle_EventUp_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { EventUp = true }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("eventUp", ex.Message);
    }

    [Fact]
    public void Handle_EventUpWithAllowEventInput_InvokesClick()
    {
        var world = new FakeTileClickWorld { EventUp = true };
        var p = JsonDocument.Parse("{\"x\":9,\"y\":8,\"allow_event_input\":true}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.ClickInvoked);
        Assert.True(result.Handled);
        Assert.Equal(9, result.Tile.X);
        Assert.Equal(8, result.Tile.Y);
    }

    [Fact]
    public void Handle_LocationMismatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"location\":\"Farm\",\"x\":9,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(
                p,
                new FakeTileClickWorld { CurrentLocationName = "Frobby_CombatLab" }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("location guard expected Farm", ex.Message);
    }

    [Fact]
    public void Handle_OutOfMapTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":40,\"y\":8}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { MapWidth = 20, MapHeight = 14 }));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("outside map bounds", ex.Message);
    }

    [Fact]
    public void Handle_LeftClickConvertsTileToWorldAndScreenCoordinates()
    {
        var world = new FakeTileClickWorld
        {
            CurrentLocationName = "Frobby_CombatLab",
            ViewportX = 64,
            ViewportY = 128,
        };
        var p = JsonDocument.Parse(
            "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"screen_offset_x\":16,\"screen_offset_y\":48}")
            .RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.Equal(592, world.ClickedWorldX);
        Assert.Equal(560, world.ClickedWorldY);
        Assert.Equal(528, world.ClickedScreenX);
        Assert.Equal(432, world.ClickedScreenY);
        Assert.True(world.ClickInvoked);
        Assert.True(result.Handled);
        Assert.Equal("Frobby_CombatLab", result.Location);
        Assert.Equal(9, result.Tile.X);
        Assert.Equal(8, result.Tile.Y);
        Assert.Equal(528, result.Screen.X);
        Assert.Equal(432, result.Screen.Y);
        Assert.Equal("(O)287", result.SelectedItem!.QualifiedId);
    }

    private sealed class FakeTileClickWorld : IInputTileClickWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public bool HasActiveMenu { get; set; }
        public bool IsWarping { get; set; }
        public bool IsFading { get; set; }
        public bool EventUp { get; set; }
        public int Tick { get; set; } = 55;
        public string CurrentLocationName { get; set; } = "Frobby_CombatLab";
        public int? MapWidth { get; set; } = 20;
        public int? MapHeight { get; set; } = 14;
        public int ViewportX { get; set; }
        public int ViewportY { get; set; }
        public bool ClickInvoked { get; private set; }
        public int? ClickedWorldX { get; private set; }
        public int? ClickedWorldY { get; private set; }
        public int? ClickedScreenX { get; private set; }
        public int? ClickedScreenY { get; private set; }
        public string? ClickedButton { get; private set; }
        public string? TargetNpcName { get; set; }
        public bool HasBlankDialogueMenuAfterClick { get; set; }
        public bool NpcFallbackInvoked { get; private set; }
        public int? NpcFallbackTileX { get; private set; }
        public int? NpcFallbackTileY { get; private set; }

        public ISelectableInventoryItem? SelectedItem { get; set; }
            = new SelectableInventoryItem(1, "(O)287", "287", "Bomb", 1, -95, 0, "Object");

        public bool ClickLeftTile(int worldX, int worldY, int screenX, int screenY)
        {
            RecordClick("left", worldX, worldY, screenX, screenY);
            return true;
        }

        public bool ClickRightTile(int worldX, int worldY, int screenX, int screenY)
        {
            RecordClick("right", worldX, worldY, screenX, screenY);
            return true;
        }

        private void RecordClick(string button, int worldX, int worldY, int screenX, int screenY)
        {
            ClickInvoked = true;
            ClickedButton = button;
            ClickedWorldX = worldX;
            ClickedWorldY = worldY;
            ClickedScreenX = screenX;
            ClickedScreenY = screenY;
        }

        public string? FindNpcAtTile(int tileX, int tileY) => TargetNpcName;

        public bool HasBlankDialogueMenu => HasBlankDialogueMenuAfterClick;

        public bool InteractNpcAtTile(int tileX, int tileY)
        {
            NpcFallbackInvoked = true;
            NpcFallbackTileX = tileX;
            NpcFallbackTileY = tileY;
            return TargetNpcName is not null;
        }
    }
}
