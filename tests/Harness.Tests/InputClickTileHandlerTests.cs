using System.Collections.Generic;
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
            SelectedItem = null,
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
            HasActiveMenuAfterClick = false,
            SelectedItem = null,
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
    public void Handle_RightClickOnNpcWithGenericDialogueAfterHandledClick_UsesNpcFallback()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Claire",
            HasActiveMenuAfterClick = true,
            HasDialogueMenuAfterClick = true,
            ActiveDialogueCharacterName = string.Empty,
            SelectedItem = new SelectableInventoryItem(0, "(T)Hoe", "Hoe", "Hoe", 1, null, null, "Hoe"),
        };
        var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.NpcFallbackInvoked);
        Assert.True(world.ActiveMenuCleared);
        Assert.Equal("Claire", result.TargetNpcName);
        Assert.True(result.NpcFallbackUsed);
        Assert.True(result.Handled);
    }

    [Fact]
    public void Handle_RightClickOnNpcWithSpeakerlessDialogueAfterHandledClick_UsesNpcFallback()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Claire",
            HasActiveMenuAfterClick = true,
            HasDialogueMenuAfterClick = true,
            ActiveDialogueCharacterName = null,
            SelectedItem = new SelectableInventoryItem(0, "(T)Hoe", "Hoe", "Hoe", 1, null, null, "Hoe"),
        };
        var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.NpcFallbackInvoked);
        Assert.True(world.ActiveMenuCleared);
        Assert.Equal("Claire", result.TargetNpcName);
        Assert.True(result.NpcFallbackUsed);
        Assert.True(result.Handled);
    }

    [Fact]
    public void Handle_RightClickOnNpcWithTargetDialogueAfterHandledClick_DoesNotUseNpcFallback()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Claire",
            HasActiveMenuAfterClick = true,
            HasDialogueMenuAfterClick = true,
            ActiveDialogueCharacterName = "Claire",
            SelectedItem = new SelectableInventoryItem(0, "(T)Hoe", "Hoe", "Hoe", 1, null, null, "Hoe"),
        };
        var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.False(world.NpcFallbackInvoked);
        Assert.Equal("Claire", result.TargetNpcName);
        Assert.False(result.NpcFallbackUsed);
        Assert.True(result.Handled);
    }

    [Fact]
    public void Handle_RightClickOnNpcWithSelectedObject_DoesNotUseNpcFallback()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Sophia",
            HasActiveMenuAfterClick = true,
            HasDialogueMenuAfterClick = true,
            ActiveDialogueCharacterName = "Sophia",
            SelectedItem = new SelectableInventoryItem(2, "(O)809", "809", "Movie Ticket", 1, 0, 0, "Object"),
        };
        var p = JsonDocument.Parse("{\"x\":18,\"y\":10,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.ClickInvoked);
        Assert.False(world.NpcFallbackInvoked);
        Assert.Equal("Sophia", result.TargetNpcName);
        Assert.False(result.NpcFallbackUsed);
        Assert.True(result.Handled);
        Assert.Equal("(O)809", result.SelectedItem!.QualifiedId);
    }

    [Fact]
    public void Handle_RightClickOnNpcWithSelectedObjectAndNonTargetDialogue_UsesNpcFallback()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Claire",
            HasActiveMenuAfterClick = true,
            HasDialogueMenuAfterClick = true,
            ActiveDialogueCharacterName = string.Empty,
            SelectedItem = new SelectableInventoryItem(2, "(O)809", "809", "Movie Ticket", 1, 0, 0, "Object"),
        };
        var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.NpcFallbackInvoked);
        Assert.True(world.ActiveMenuCleared);
        Assert.Equal("Claire", result.TargetNpcName);
        Assert.True(result.NpcFallbackUsed);
        Assert.True(result.Handled);
        Assert.Equal("(O)809", result.SelectedItem!.QualifiedId);
    }

    [Fact]
    public void Handle_RightClickOnNpcWithSelectedTool_StillUsesNpcFallbackForBlankDialogue()
    {
        var world = new FakeTileClickWorld
        {
            TargetNpcName = "Claire",
            HasBlankDialogueMenuAfterClick = true,
            SelectedItem = new SelectableInventoryItem(0, "(T)Hoe", "Hoe", "Hoe", 1, null, null, "Hoe"),
        };
        var p = JsonDocument.Parse("{\"x\":7,\"y\":5,\"button\":\"right\"}").RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.True(world.NpcFallbackInvoked);
        Assert.Equal("Claire", result.TargetNpcName);
        Assert.True(result.NpcFallbackUsed);
        Assert.True(result.Handled);
        Assert.Equal("(T)Hoe", result.SelectedItem!.QualifiedId);
    }

    [Fact]
    public void Handle_ActionValue_ClicksNearestMatchingTileWithinRadius()
    {
        var world = new FakeTileClickWorld
        {
            CurrentLocationName = "MovieTheater",
            ViewportX = 64,
            ViewportY = 128,
        };
        world.SetTileProperty(8, 4, "Buildings", "Action", "Concessions");
        world.SetTileProperty(12, 9, "Buildings", "Action", "Concessions");
        var p = JsonDocument.Parse(
                "{\"location\":\"MovieTheater\",\"x\":7,\"y\":7,\"button\":\"right\",\"action_value\":\"Concessions\",\"radius\":8}")
            .RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.Equal("right", world.ClickedButton);
        Assert.Equal(8, result.Tile.X);
        Assert.Equal(4, result.Tile.Y);
        Assert.Equal(544, world.ClickedWorldX);
        Assert.Equal(288, world.ClickedWorldY);
        Assert.Equal(480, world.ClickedScreenX);
        Assert.Equal(160, world.ClickedScreenY);
        Assert.True(result.Handled);
        Assert.Equal("Concessions", result.ResolvedActionValue);
        Assert.Equal("Buildings", result.ResolvedActionLayer);
        Assert.Equal("Action", result.ResolvedActionProperty);
        Assert.NotNull(result.ResolvedActionTile);
        Assert.Equal(8, result.ResolvedActionTile!.X);
        Assert.Equal(4, result.ResolvedActionTile.Y);
        Assert.True(result.ScreenVisible);
    }

    [Fact]
    public void Handle_ActionValueNoMatch_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse(
                "{\"x\":7,\"y\":7,\"button\":\"right\",\"action_value\":\"Concessions\",\"radius\":3}")
            .RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("Concessions", ex.Message);
    }

    [Fact]
    public void Handle_ActionValueNegativeRadius_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse(
                "{\"x\":7,\"y\":7,\"button\":\"right\",\"action_value\":\"Concessions\",\"radius\":-1}")
            .RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickTileHandler.Handle(p, new FakeTileClickWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("radius", ex.Message);
    }

    [Fact]
    public void Handle_ActionValueOffscreen_ReportsScreenVisibleFalse()
    {
        var world = new FakeTileClickWorld
        {
            CurrentLocationName = "MovieTheater",
            ViewportX = 0,
            ViewportY = 0,
            ViewportWidth = 1280,
            ViewportHeight = 720,
            MapWidth = 80,
            MapHeight = 80,
        };
        world.SetTileProperty(30, 30, "Buildings", "Action", "Theater_Doors");
        var p = JsonDocument.Parse(
                "{\"location\":\"MovieTheater\",\"x\":25,\"y\":25,\"button\":\"right\",\"action_value\":\"Theater_Doors\",\"radius\":10}")
            .RootElement;

        var json = InputClickTileHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<InputClickTileResult>(json, ProtocolJson.Options)!;

        Assert.Equal("Theater_Doors", result.ResolvedActionValue);
        Assert.Equal("Buildings", result.ResolvedActionLayer);
        Assert.Equal("Action", result.ResolvedActionProperty);
        Assert.NotNull(result.ResolvedActionTile);
        Assert.Equal(30, result.ResolvedActionTile!.X);
        Assert.Equal(30, result.ResolvedActionTile.Y);
        Assert.Equal(1952, result.Screen.X);
        Assert.Equal(1952, result.Screen.Y);
        Assert.False(result.ScreenVisible);
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
            InputClickTileHandler.Handle(p, new FakeTileClickWorld { HasActiveMenuBeforeClick = true }));

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
        private readonly Dictionary<(int X, int Y, string Layer, string Property), string> _tileProperties = new();

        public bool IsWorldReady { get; set; } = true;
        public bool HasActiveMenuBeforeClick { get; set; }
        public bool HasActiveMenuAfterClick { get; set; } = true;
        public bool HasActiveMenu => ClickInvoked ? HasActiveMenuAfterClick : HasActiveMenuBeforeClick;
        public bool HasDialogueMenuAfterClick { get; set; }
        public bool HasDialogueMenu => ClickInvoked && HasDialogueMenuAfterClick;
        public bool IsWarping { get; set; }
        public bool IsFading { get; set; }
        public bool EventUp { get; set; }
        public int Tick { get; set; } = 55;
        public string CurrentLocationName { get; set; } = "Frobby_CombatLab";
        public int? MapWidth { get; set; } = 20;
        public int? MapHeight { get; set; } = 14;
        public int ViewportX { get; set; }
        public int ViewportY { get; set; }
        public int ViewportWidth { get; set; } = 1280;
        public int ViewportHeight { get; set; } = 720;
        public IReadOnlyList<string> LayerNames { get; } = new[] { "Back", "Buildings" };
        public bool ClickInvoked { get; private set; }
        public int? ClickedWorldX { get; private set; }
        public int? ClickedWorldY { get; private set; }
        public int? ClickedScreenX { get; private set; }
        public int? ClickedScreenY { get; private set; }
        public string? ClickedButton { get; private set; }
        public string? TargetNpcName { get; set; }
        public bool HasBlankDialogueMenuAfterClick { get; set; }
        public string? ActiveDialogueCharacterName { get; set; }
        public bool ActiveMenuCleared { get; private set; }
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

        public void SetTileProperty(int x, int y, string layer, string property, string value)
            => _tileProperties[(x, y, layer, property)] = value;

        public string? GetTileProperty(int x, int y, string layer, string property)
            => _tileProperties.TryGetValue((x, y, layer, property), out var value) ? value : null;

        public bool HasBlankDialogueMenu => HasBlankDialogueMenuAfterClick;

        public void ClearActiveMenu()
        {
            ActiveMenuCleared = true;
            HasActiveMenuAfterClick = false;
            HasDialogueMenuAfterClick = false;
            ActiveDialogueCharacterName = null;
        }

        public bool InteractNpcAtTile(int tileX, int tileY)
        {
            NpcFallbackInvoked = true;
            NpcFallbackTileX = tileX;
            NpcFallbackTileY = tileY;
            return TargetNpcName is not null;
        }
    }
}
