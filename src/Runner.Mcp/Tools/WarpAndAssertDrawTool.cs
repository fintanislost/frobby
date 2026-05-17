using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Atomic: warp → draw.arm → wait 500ms → freeze.begin → draw.assert_contains → freeze.end.</summary>
public sealed class WarpAndAssertDrawTool : ITool
{
    public string Name => "warp_and_assert_draw";
    public string Description =>
        "Warp, arm draw-capture, wait 500ms for the scene to settle, enter FREEZE, " +
        "assert that the given texture was drawn, then THAW. Returns {passed, matched}.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object",
         "properties":{
           "location":{"type":"string"},
           "x":{"type":"integer"},
           "y":{"type":"integer"},
           "texture_asset":{"type":"string"},
           "min_count":{"type":"integer","minimum":1,"default":1}
         },
         "required":["location","x","y","texture_asset"]}
        """).RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
    {
        var life = context.Lifecycle;
        if (life is null) return McpToolResult.Error("lifecycle unavailable");
        string? location = args.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
        if (location is null) return McpToolResult.Error("'location' is required");
        if (!args.TryGetProperty("x", out var xe) || xe.ValueKind != JsonValueKind.Number) return McpToolResult.Error("'x' is required");
        if (!args.TryGetProperty("y", out var ye) || ye.ValueKind != JsonValueKind.Number) return McpToolResult.Error("'y' is required");
        int x = xe.GetInt32();
        int y = ye.GetInt32();
        string? texture = args.TryGetProperty("texture_asset", out var te) && te.ValueKind == JsonValueKind.String ? te.GetString() : null;
        if (texture is null) return McpToolResult.Error("'texture_asset' is required");
        int minCount = args.TryGetProperty("min_count", out var mc) && mc.ValueKind == JsonValueKind.Number ? mc.GetInt32() : 1;

        var warpParams = JsonDocument.Parse($"{{\"location\":{System.Text.Json.JsonSerializer.Serialize(location)},\"x\":{x},\"y\":{y}}}").RootElement;
        var filterJson = $"{{\"filter\":{{\"texture_asset\":{System.Text.Json.JsonSerializer.Serialize(texture)}}},\"min_count\":{minCount}}}";

        try
        {
            await life.InvokeAsync("player.warp", warpParams, ct);
            await life.InvokeAsync("draw.arm", null, ct);
            await Task.Delay(500, ct);
            await life.InvokeAsync("freeze.begin", null, ct);

            var assertResult = await life.InvokeAsync("draw.assert_contains",
                JsonDocument.Parse(filterJson).RootElement, ct);

            try { await life.InvokeAsync("freeze.end", null, ct); }
            catch { /* best-effort thaw */ }

            return McpToolResult.Success(assertResult);
        }
        catch (SdvRpcException ex)
        {
            try { await life.InvokeAsync("freeze.end", null, ct); } catch { }
            return McpToolResult.Error(ex.Message);
        }
    }
}
