using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Parallel reads of state.player + state.location + state.time + state.menu.</summary>
public sealed class CaptureStateTool : ITool
{
    public string Name => "capture_state";
    public string Description =>
        "Snapshot the current game state: player, location, time, and active menu.";

    public JsonElement InputSchema { get; } =
        JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
    {
        var life = context.Lifecycle;
        if (life is null) return McpToolResult.Error("lifecycle unavailable");

        try
        {
            var player = life.InvokeAsync("state.player", null, ct);
            var location = life.InvokeAsync("state.location", null, ct);
            var time = life.InvokeAsync("state.time", null, ct);
            var menu = life.InvokeAsync("state.menu", null, ct);
            await Task.WhenAll(player, location, time, menu);

            var result = new JsonObject
            {
                ["player"]   = JsonNode.Parse(player.Result.GetRawText()),
                ["location"] = JsonNode.Parse(location.Result.GetRawText()),
                ["time"]     = JsonNode.Parse(time.Result.GetRawText()),
                ["menu"]     = JsonNode.Parse(menu.Result.GetRawText()),
            };
            return McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement);
        }
        catch (SdvRpcException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
    }
}
