using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>
/// Raw JSON-RPC passthrough. Forwards <c>args.method</c> + <c>args.params</c> to the
/// harness and returns the result. The escape hatch for workflows the curated tools
/// don't cover.
/// </summary>
public sealed class RpcCallTool : ITool
{
    public string Name => "rpc_call";

    public string Description =>
        "Raw passthrough to any harness JSON-RPC method. Use when no curated tool fits. " +
        "Example: {\"method\":\"state.player\"} returns the current player state.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {
          "type":"object",
          "properties":{
            "method":{"type":"string","description":"JSON-RPC method name (e.g. 'state.player', 'player.warp')"},
            "params":{"type":"object","description":"Optional method parameters"}
          },
          "required":["method"]
        }
        """).RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
    {
        var life = context.Lifecycle;
        if (!args.TryGetProperty("method", out var m) || m.ValueKind != JsonValueKind.String)
            return McpToolResult.Error("'method' is required");
        if (life is null)
            return McpToolResult.Error("SDV lifecycle not available — internal server misconfiguration");

        var method = m.GetString()!;
        JsonElement? p = args.TryGetProperty("params", out var pe) ? pe : null;

        try
        {
            var result = await life.InvokeAsync(method, p, ct);
            return McpToolResult.Success(result);
        }
        catch (SdvRpcException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
    }
}
