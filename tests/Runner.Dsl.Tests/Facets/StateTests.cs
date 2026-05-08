using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class StateTests
{
    private sealed class StubInvoker : ISdvTestInvoker
    {
        public string? LastMethod { get; private set; }
        public string? LastParams { get; private set; }
        public string NextJson { get; set; } = "{}";
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            LastMethod = m;
            LastParams = p?.GetRawText();
            return Task.FromResult(JsonDocument.Parse(NextJson).RootElement.Clone());
        }
    }

    [Fact]
    public async Task Player_InvokesStatePlayerAndDeserializes()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"name\":\"Alice\",\"money\":5000,\"stamina\":200,\"max_stamina\":270,\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15}}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var p = await State.Player();
            Assert.Equal("state.player", inv.LastMethod);
            Assert.Equal("Alice", p.Name);
            Assert.Equal(5000, p.Money);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task Npc_InvokesStateNpcWithName()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker { NextJson = "{\"name\":\"Pierre\"}" };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var n = await State.Npc("Pierre");
            Assert.Equal("state.npc", inv.LastMethod);
            Assert.Contains("\"name\":\"Pierre\"", inv.LastParams);
            Assert.Equal("Pierre", n.Name);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task Npcs_InvokesStateNpcsWithOptions()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"npcs\":[]}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var npcs = await State.Npcs(includeOffscreen: false, limit: 25);

            Assert.Equal("state.npcs", inv.LastMethod);
            Assert.Contains("\"include_offscreen\":false", inv.LastParams);
            Assert.Contains("\"limit\":25", inv.LastParams);
            Assert.Empty(npcs.Npcs);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task Locations_InvokesStateLocations()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"locations\":[{\"name\":\"Custom_TownEast\",\"unique_name\":\"Custom_TownEast\",\"is_outdoors\":true,\"map_width\":90,\"map_height\":64,\"warp_count\":5}]}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var locations = await State.Locations();

            Assert.Equal("state.locations", inv.LastMethod);
            Assert.Null(inv.LastParams);
            var location = Assert.Single(locations.Locations);
            Assert.Equal("Custom_TownEast", location.Name);
            Assert.Equal(90, location.MapWidth);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task MapTile_InvokesStateMapTileWithSnakeCaseArgs()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"location\":\"Custom_TownEast\",\"x\":10,\"y\":20,\"layers\":[{\"name\":\"Back\",\"tile_index\":471,\"tile_sheet\":\"outdoors\",\"properties\":{\"TouchAction\":\"MagicWarp Custom_EnchantedGrove\"}}]}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var tile = await State.MapTile(location: "Custom_TownEast", x: 10, y: 20, layers: new[] { "Back" });

            Assert.Equal("state.map_tile", inv.LastMethod);
            Assert.Contains("\"location\":\"Custom_TownEast\"", inv.LastParams);
            Assert.Contains("\"x\":10", inv.LastParams);
            Assert.Contains("\"y\":20", inv.LastParams);
            Assert.Contains("\"layers\":[\"Back\"]", inv.LastParams);
            Assert.Equal("Custom_TownEast", tile.Location);
            Assert.Equal("Back", Assert.Single(tile.Layers).Name);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task MapTile_WithNoArgs_InvokesCurrentTileSnapshot()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"location\":\"Farm\",\"x\":64,\"y\":15,\"layers\":[]}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var tile = await State.MapTile();

            Assert.Equal("state.map_tile", inv.LastMethod);
            Assert.Equal("{}", inv.LastParams);
            Assert.Equal("Farm", tile.Location);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task TileActions_InvokesStateTileActionsWithFilters()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"location\":\"Custom_BlueMoonVineyard\",\"x\":56,\"y\":48,\"radius\":1,\"actions\":[{\"tile\":{\"x\":56,\"y\":48},\"layer\":\"Back\",\"property\":\"TouchAction\",\"value\":\"LoadMap Town 50 114 0\",\"distance\":0}]}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            TileActionsState actions = await State.TileActions(
                location: "Custom_BlueMoonVineyard",
                x: 56,
                y: 48,
                radius: 1,
                layers: new[] { "Back" },
                properties: new[] { "TouchAction" });

            Assert.Equal("state.tile_actions", inv.LastMethod);
            Assert.Contains("\"location\":\"Custom_BlueMoonVineyard\"", inv.LastParams);
            Assert.Contains("\"x\":56", inv.LastParams);
            Assert.Contains("\"y\":48", inv.LastParams);
            Assert.Contains("\"radius\":1", inv.LastParams);
            Assert.Contains("\"layers\":[\"Back\"]", inv.LastParams);
            Assert.Contains("\"properties\":[\"TouchAction\"]", inv.LastParams);
            var action = Assert.Single(actions.Actions);
            Assert.Equal("TouchAction", action.Property);
            Assert.Equal("LoadMap Town 50 114 0", action.Value);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task Event_InvokesStateEventAndDeserializes()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"active\":true,\"event_up\":true,\"location\":\"BusStop\",\"id\":\"520702\",\"is_festival\":false,\"is_skippable\":true,\"player_control_locked\":true,\"actors\":[{\"name\":\"Krobus\",\"tile\":{\"x\":16,\"y\":23},\"pixel\":{\"x\":1024,\"y\":1472},\"facing_direction\":3,\"current_frame\":0}],\"dialogue\":null,\"viewport\":{\"x\":896,\"y\":1472,\"width\":1280,\"height\":720}}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var state = await State.Event();

            Assert.Equal("state.event", inv.LastMethod);
            Assert.Null(inv.LastParams);
            Assert.True(state.Active);
            Assert.Equal("520702", state.Id);
            Assert.Equal("Krobus", Assert.Single(state.Actors).Name);
            Assert.Equal(1280, state.Viewport?.Width);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task Shop_InvokesStateShopAndDeserializes()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"present\":true,\"menu_type\":\"ShopMenu\",\"shop_id\":\"ExampleMod.CustomVendor\",\"currency\":0,\"items\":[{\"item_id\":\"ExampleMod.CustomDrink\",\"qualified_id\":\"(O)ExampleMod.CustomDrink\",\"display_name\":\"Custom Drink\",\"price\":4000,\"stock\":5,\"category\":0,\"quality\":0,\"runtime_type\":\"Object\"}]}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var shop = await State.Shop();

            Assert.Equal("state.shop", inv.LastMethod);
            Assert.Null(inv.LastParams);
            Assert.True(shop.Present);
            Assert.Equal("ExampleMod.CustomVendor", shop.ShopId);
            Assert.Equal("ExampleMod.CustomDrink", Assert.Single(shop.Items).ItemId);
        }
        finally { SdvTestSession.ResetForTests(); }
    }
}
