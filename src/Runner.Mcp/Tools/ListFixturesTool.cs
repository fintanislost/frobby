using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Enumerate fixtures under <c>tests/fixtures/</c>; return name + version + description from each <c>.meta.json</c>.</summary>
public sealed class ListFixturesTool : ITool
{
    public string Name => "list_fixtures";
    public string Description =>
        "List available save fixtures under tests/fixtures/ (reads each fixture's .meta.json).";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"root":{"type":"string","description":"Fixtures root (default: ./tests/fixtures)"}}}
        """).RootElement;

    public Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? lifecycle, CancellationToken ct)
    {
        var root = args.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()!
            : Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures");

        var arr = new JsonArray();
        if (Directory.Exists(root))
        {
            foreach (var fxDir in Directory.EnumerateDirectories(root))
            {
                var metaPath = Path.Combine(fxDir, ".meta.json");
                if (!File.Exists(metaPath)) continue;
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(metaPath))!;
                    var entry = new JsonObject
                    {
                        ["name"] = node["name"]?.GetValue<string>() ?? Path.GetFileName(fxDir),
                    };
                    if (node["sdv_version"] is { } v) entry["sdv_version"] = v.GetValue<string>();
                    if (node["description"] is { } d) entry["description"] = d.GetValue<string>();
                    arr.Add(entry);
                }
                catch { /* skip bad meta */ }
            }
        }
        var result = new JsonObject { ["fixtures"] = arr };
        return Task.FromResult(McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement));
    }
}
