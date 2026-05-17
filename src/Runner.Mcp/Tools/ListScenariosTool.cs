using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Enumerate <c>*.test.json</c> files in a directory; return path + name + fixture.</summary>
public sealed class ListScenariosTool : ITool
{
    public string Name => "list_scenarios";
    public string Description =>
        "List .test.json scenario files in a directory (recursive). Returns path, name, and fixture for each.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"dir":{"type":"string","description":"Directory to scan (default: cwd)"}}}
        """).RootElement;

    public Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
    {
        var dir = args.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()!
            : Directory.GetCurrentDirectory();

        if (!Directory.Exists(dir))
            return Task.FromResult(McpToolResult.Error($"directory not found: {dir}"));

        var arr = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(dir, "*.test.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(path);
                var node = JsonNode.Parse(json)!;
                var name = node["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
                var fixture = node["fixture"]?.GetValue<string>();

                var entry = new JsonObject { ["path"] = path, ["name"] = name };
                if (fixture is not null) entry["fixture"] = fixture;
                arr.Add(entry);
            }
            catch { /* skip unparseable files */ }
        }
        var result = new JsonObject { ["scenarios"] = arr };
        return Task.FromResult(McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement));
    }
}
