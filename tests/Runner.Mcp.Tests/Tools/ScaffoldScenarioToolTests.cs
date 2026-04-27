using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp.Tools;
using SdvTestFramework.Protocol.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class ScaffoldScenarioToolTests
{
    [Fact]
    public async Task Scaffold_WritesStarterJsonAcceptedByScenarioLoader()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"probe_menu\",\"fixture\":\"m0spike_436515781\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.True(File.Exists(tmpOut));

            // Valid against schemas/scenario.schema.json.
            var spec = ScenarioLoader.Load(tmpOut);
            Assert.Equal("probe_menu", spec.Name);
            Assert.Equal("m0spike_436515781", spec.Fixture);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }

    [Fact]
    public async Task Scaffold_NpcInteractionTemplate_HasExpectedSteps()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"probe\",\"template\":\"npc_interaction\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            var json = File.ReadAllText(tmpOut);
            Assert.Contains("player.warp", json);
            Assert.Contains("time.advance", json);
            Assert.Contains("SeedShop", json);
            Assert.Contains("world.interact_npc", json);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }

    [Fact]
    public async Task Scaffold_ShopPurchaseTemplate_HasMenuAssertion()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"probe\",\"template\":\"shop_purchase\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            var json = File.ReadAllText(tmpOut);
            Assert.Contains("player.set_money", json);
            Assert.Contains("ShopMenu", json);
            Assert.Contains("world.interact_npc", json);
            Assert.Contains("Pierre", json);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }

    [Fact]
    public async Task Scaffold_ToolUseTemplate_HasGiveItem()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"probe\",\"template\":\"tool_use\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            var json = File.ReadAllText(tmpOut);
            Assert.Contains("player.give_item", json);
            Assert.Contains("(T)0", json);  // Watering Can
            Assert.Contains("Farm", json);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }

    [Fact]
    public async Task Scaffold_InventoryCheckTemplate_HasStateAssertions()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"probe\",\"template\":\"inventory_check\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            var json = File.ReadAllText(tmpOut);
            Assert.Contains("player.give_item", json);
            Assert.Contains("(O)74", json);  // Prismatic Shard
            Assert.Contains("state.player.money == 1000", json);
            Assert.Contains("state.player.name != ''", json);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }

    [Fact]
    public async Task Scaffold_StarbergTerminalTemplate_UsesFurnitureInteractionAndMenuAssertion()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"starberg_terminal\",\"template\":\"starberg_terminal\",\"fixture\":\"m0spike_436515781\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            var json = File.ReadAllText(tmpOut);
            Assert.Contains("world.place_furniture", json);
            Assert.Contains("(F)stonks_starberg_terminal_v1", json);
            Assert.Contains("world.interact_tile", json);
            Assert.Contains("state.menu.type == 'TerminalMenu'", json);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }

    [Fact]
    public async Task Scaffold_StarbergTerminalTemplate_IncludesTextDrawAssertion()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"starberg_terminal\",\"template\":\"starberg_terminal\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            var json = File.ReadAllText(tmpOut);
            Assert.Contains("draw.text_contains", json);
            Assert.Contains("STARBERG TERMINAL", json);
            Assert.Contains("CASH", json);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }
}
