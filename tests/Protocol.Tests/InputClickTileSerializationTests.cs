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
            "{\"location\":\"Frobby_CombatLab\",\"x\":9,\"y\":8,\"button\":\"left\",\"require_current_location\":false,\"screen_offset_x\":16,\"screen_offset_y\":48,\"allow_event_input\":true}",
            ProtocolJson.Options)!;

        Assert.Equal("Frobby_CombatLab", req.Location);
        Assert.Equal(9, req.X);
        Assert.Equal(8, req.Y);
        Assert.Equal("left", req.Button);
        Assert.False(req.RequireCurrentLocation);
        Assert.Equal(16, req.ScreenOffsetX);
        Assert.Equal(48, req.ScreenOffsetY);
        Assert.True(req.AllowEventInput);
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
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"location\":\"Frobby_CombatLab\"", json);
        Assert.Contains("\"tile\":{\"x\":9,\"y\":8}", json);
        Assert.Contains("\"screen\":{\"x\":608,\"y\":544}", json);
        Assert.Contains("\"world\":{\"x\":608,\"y\":544}", json);
        Assert.Contains("\"selected_item\":", json);
        Assert.Contains("\"handled\":true", json);
        Assert.DoesNotContain("SelectedItem", json);
    }
}
