using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class InputClickTileSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCaseFields()
    {
        var req = JsonSerializer.Deserialize<InputClickTileRequest>(
            "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"button\":\"left\",\"require_current_location\":false,\"screen_offset_x\":16,\"screen_offset_y\":48,\"allow_event_input\":true,\"action_value\":\"Concessions\",\"radius\":10,\"layers\":[\"Buildings\"],\"properties\":[\"Action\"]}",
            ProtocolJson.Options)!;

        Assert.Equal("Frobby_CombatLab", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(8, req.Y);
        Assert.Equal("left", req.Button);
        Assert.False(req.RequireCurrentLocation);
        Assert.Equal(16, req.ScreenOffsetX);
        Assert.Equal(48, req.ScreenOffsetY);
        Assert.True(req.AllowEventInput);
        Assert.Equal("Concessions", req.ActionValue);
        Assert.Equal(10, req.Radius);
        Assert.Equal(new[] { "Buildings" }, req.Layers);
        Assert.Equal(new[] { "Action" }, req.Properties);
    }

    [Fact]
    public void Request_DefaultsToLeftCurrentLocationAndTileCenter()
    {
        var req = JsonSerializer.Deserialize<InputClickTileRequest>(
            "{\"x\":9,\"y\":8}",
            ProtocolJson.Options)!;

        Assert.Null(req.Location);
        Assert.Equal("left", req.Button);
        Assert.True(req.RequireCurrentLocation);
        Assert.Equal(32, req.ScreenOffsetX);
        Assert.Equal(32, req.ScreenOffsetY);
        Assert.False(req.AllowEventInput);
    }

    [Fact]
    public void Result_SerializesDiagnosticsAsSnakeCase()
    {
        var result = new InputClickTileResult
        {
            Ok = true,
            Tick = 99,
            Location = "Frobby_CombatLab",
            Tile = new TilePoint { X = 9, Y = 8 },
            Screen = new PixelPoint { X = 608, Y = 544 },
            World = new PixelPoint { X = 608, Y = 544 },
            SelectedItem = new PlayerItemSummary
            {
                Slot = 1,
                Id = "(O)287",
                ItemId = "287",
                QualifiedId = "(O)287",
                Name = "Bomb",
                Stack = 1,
                RuntimeType = "Object",
            },
            Handled = true,
            TargetNpcName = "Claire",
            NpcFallbackUsed = true,
            ResolvedActionValue = "Theater_Doors",
            ResolvedActionLayer = "Buildings",
            ResolvedActionProperty = "Action",
            ResolvedActionTile = new TilePoint { X = 14, Y = 16 },
            ScreenVisible = true,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"screen\":{\"x\":608,\"y\":544}", json);
        Assert.Contains("\"world\":{\"x\":608,\"y\":544}", json);
        Assert.Contains("\"selected_item\":", json);
        Assert.Contains("\"handled\":true", json);
        Assert.Contains("\"target_npc_name\":\"Claire\"", json);
        Assert.Contains("\"npc_fallback_used\":true", json);
        Assert.Contains("\"resolved_action_value\":\"Theater_Doors\"", json);
        Assert.Contains("\"resolved_action_layer\":\"Buildings\"", json);
        Assert.Contains("\"resolved_action_property\":\"Action\"", json);
        Assert.Contains("\"resolved_action_tile\":{\"x\":14,\"y\":16}", json);
        Assert.Contains("\"screen_visible\":true", json);
        Assert.DoesNotContain("SelectedItem", json);
    }
}
