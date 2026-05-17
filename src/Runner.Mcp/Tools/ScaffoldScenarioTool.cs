using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Write a starter <c>.test.json</c> at the given path (default: <c>tests/samples/&lt;name&gt;.test.json</c>).</summary>
public sealed class ScaffoldScenarioTool : ITool
{
    public string Name => "scaffold_scenario";
    public string Description =>
        "Generate a starter .test.json skeleton. Optional 'template' (shop|menu|warp|npc_interaction|shop_purchase|tool_use|inventory_check|furniture_menu) pre-fills steps and assertions.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object",
         "properties":{
           "name":{"type":"string","description":"Scenario name"},
           "fixture":{"type":"string","description":"Optional fixture name"},
           "template":{"type":"string","enum":["shop","menu","warp","npc_interaction","shop_purchase","tool_use","inventory_check","furniture_menu"],"description":"Optional step + assertion template"},
           "output":{"type":"string","description":"Explicit output path (default: tests/samples/<name>.test.json)"}
         },
         "required":["name"]}
        """).RootElement;

    public Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
    {
        if (!args.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String)
            return Task.FromResult(McpToolResult.Error("'name' is required"));
        var name = n.GetString()!;
        string? fixture = args.TryGetProperty("fixture", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        string? template = args.TryGetProperty("template", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        string output = args.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String
            ? o.GetString()!
            : Path.Combine(Directory.GetCurrentDirectory(), "tests", "samples", $"{name}.test.json");

        var content = BuildTemplate(template);
        var obj = new JsonObject
        {
            ["name"] = name,
            ["config"] = new JsonObject { ["seed"] = 42 },
            ["steps"] = content.Steps,
            ["assertions"] = content.Assertions,
        };
        if (fixture is not null) obj["fixture"] = fixture;

        var dir = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(output, obj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));

        var result = new JsonObject { ["path"] = output };
        return Task.FromResult(McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement));
    }

    private sealed record TemplateContent(JsonArray Steps, JsonArray Assertions);

    private static TemplateContent BuildTemplate(string? template) => template switch
    {
        "shop" => new TemplateContent(
            new JsonArray
            {
                Step("player.warp", new JsonObject { ["location"] = "SeedShop", ["x"] = 4, ["y"] = 19 }),
                Step("wait.ms",     new JsonObject { ["ms"] = 500 }),
            },
            new JsonArray()),

        "menu" => new TemplateContent(
            new JsonArray
            {
                Step("draw.arm",     new JsonObject()),
                Step("wait.ms",      new JsonObject { ["ms"] = 500 }),
                Step("freeze.begin", new JsonObject()),
            },
            new JsonArray()),

        "warp" => new TemplateContent(
            new JsonArray
            {
                Step("player.warp", new JsonObject { ["location"] = "Farm", ["x"] = 64, ["y"] = 15 }),
            },
            new JsonArray()),

        "npc_interaction" => new TemplateContent(
            new JsonArray
            {
                Step("player.warp",        new JsonObject { ["location"] = "SeedShop", ["x"] = 4, ["y"] = 19 }),
                Step("time.advance",       new JsonObject { ["minutes"] = 60 }),
                Step("world.interact_npc", new JsonObject { ["name"] = "Pierre" }),
                Step("wait.ms",            new JsonObject { ["ms"] = 500 }),
            },
            new JsonArray()),

        "shop_purchase" => new TemplateContent(
            new JsonArray
            {
                Step("player.warp",        new JsonObject { ["location"] = "SeedShop", ["x"] = 4, ["y"] = 19 }),
                Step("player.set_money",   new JsonObject { ["amount"] = 5000 }),
                Step("time.advance",       new JsonObject { ["minutes"] = 60 }),
                Step("world.interact_npc", new JsonObject { ["name"] = "Pierre" }),
                Step("wait.ms",            new JsonObject { ["ms"] = 500 }),
            },
            new JsonArray
            {
                new JsonObject { ["type"] = "state", ["expr"] = "state.menu.type == 'ShopMenu'", ["message"] = "shop menu should be open after interacting with Pierre" },
            }),

        "tool_use" => new TemplateContent(
            new JsonArray
            {
                Step("fixture.load",     new JsonObject { ["name"] = "REPLACE_WITH_FIXTURE_NAME" }),
                Step("player.give_item", new JsonObject { ["id"] = "(T)0", ["count"] = 1 }),  // Watering Can
                Step("player.warp",      new JsonObject { ["location"] = "Farm", ["x"] = 64, ["y"] = 15 }),
                Step("wait.ms",          new JsonObject { ["ms"] = 200 }),
            },
            new JsonArray()),

        "inventory_check" => new TemplateContent(
            new JsonArray
            {
                Step("player.give_item", new JsonObject { ["id"] = "(O)74", ["count"] = 1 }),  // Prismatic Shard
                Step("player.set_money", new JsonObject { ["amount"] = 1000 }),
            },
            new JsonArray
            {
                new JsonObject { ["type"] = "state", ["expr"] = "state.player.money == 1000", ["message"] = "money should match what was set" },
                new JsonObject { ["type"] = "state", ["expr"] = "state.player.name != ''",   ["message"] = "player name should be populated" },
            }),

        "furniture_menu" => new TemplateContent(
            new JsonArray
            {
                Step("player.warp",           new JsonObject { ["location"] = "FarmHouse", ["x"] = 8, ["y"] = 10 }),
                Step("world.place_furniture", new JsonObject { ["id"] = "REPLACE_WITH_FURNITURE_ID", ["location"] = "FarmHouse", ["x"] = 8, ["y"] = 9, ["remove_existing"] = true }),
                Step("wait.ms",               new JsonObject { ["ms"] = 500 }),
                Step("world.interact_tile",   new JsonObject { ["x"] = 8, ["y"] = 9 }),
                Step("wait.ms",               new JsonObject { ["ms"] = 500 }),
                Step("draw.arm",              new JsonObject { ["ticks"] = 60 }),
                Step("wait.ms",               new JsonObject { ["ms"] = 1000 }),
                Step("freeze.begin",          new JsonObject()),
            },
            new JsonArray
            {
                new JsonObject { ["type"] = "state", ["expr"] = "state.menu.type == 'REPLACE_WITH_MENU_TYPE'", ["message"] = "custom furniture should open the expected menu" },
                new JsonObject { ["type"] = "draw.text_contains", ["filter"] = new JsonObject { ["text_contains"] = "REPLACE_WITH_VISIBLE_TITLE", ["case_sensitive"] = true }, ["message"] = "expected menu title should be visible" },
                new JsonObject { ["type"] = "draw.text_contains", ["filter"] = new JsonObject { ["text_contains"] = "REPLACE_WITH_EXPECTED_BODY_TEXT", ["case_sensitive"] = false }, ["message"] = "expected body text should be visible" },
            }),

        _ => new TemplateContent(new JsonArray(), new JsonArray()),
    };

    private static JsonObject Step(string action, JsonObject args) =>
        new() { ["action"] = action, ["args"] = args };
}
