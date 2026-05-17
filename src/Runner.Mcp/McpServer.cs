using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>
/// MCP stdio server. Reads newline-delimited JSON-RPC 2.0 requests from <c>stdin</c>,
/// dispatches to the registered tool or built-in handler, writes NDJSON responses to
/// <c>stdout</c>. <c>stderr</c> is available for diagnostic logs (not used by MVP).
/// </summary>
public sealed class McpServer
{
    private readonly ToolRegistry _tools;
    private readonly SdvLifecycle? _lifecycle;

    public McpServer(ToolRegistry tools, SdvLifecycle? lifecycle)
    {
        _tools = tools;
        _lifecycle = lifecycle;
    }

    /// <summary>Build the default tool registry — all MVP tools registered.</summary>
    public static ToolRegistry BuildRegistry()
    {
        var reg = new ToolRegistry();
        reg.Register(new Tools.RpcCallTool());
        reg.Register(new Tools.ListScenariosTool());
        reg.Register(new Tools.ListFixturesTool());
        reg.Register(new Tools.ScaffoldScenarioTool());
        reg.Register(new Tools.RunScenarioTool());
        reg.Register(new Tools.WarpAndAssertDrawTool());
        reg.Register(new Tools.CaptureStateTool());
        return reg;
    }

    public async Task RunAsync(Stream stdin, Stream stdout, CancellationToken ct)
    {
        using var reader = new StreamReader(stdin, Encoding.UTF8, leaveOpen: true);
        var ndWriter = new NdJsonWriter(stdout);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            // Try parse as a request first; notifications have no id.
            JsonRpcRequest? req = null;
            bool isNotification = false;

            try
            {
                // MCP sends notifications (no id) — detect these before trying ParseRequest.
                // ParseRequest throws if id is missing. We attempt notification parse first.
                req = TryParseRequest(line, out isNotification);
            }
            catch (Exception)
            {
                await WriteErrorAsync(ndWriter, 0, McpError.InvalidRequest(), ct);
                continue;
            }

            if (isNotification)
            {
                // Notifications expect no response. Handle silently.
                continue;
            }

            if (req is null)
            {
                await WriteErrorAsync(ndWriter, 0, McpError.InvalidRequest(), ct);
                continue;
            }

            await DispatchAsync(ndWriter, req, ct);
        }

        if (_lifecycle is not null) await _lifecycle.DisposeAsync();
    }

    /// <summary>
    /// Tries to parse a line as a JSON-RPC request. Sets <paramref name="isNotification"/>
    /// if the line has no <c>id</c> (i.e. it's a notification). Returns null on parse failure.
    /// </summary>
    private static JsonRpcRequest? TryParseRequest(string line, out bool isNotification)
    {
        isNotification = false;

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        // Validate jsonrpc field.
        if (!root.TryGetProperty("jsonrpc", out var versionEl) || versionEl.GetString() != "2.0")
            throw new InvalidOperationException("missing or wrong 'jsonrpc' field");

        if (!root.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("missing 'method'");

        // No id = notification.
        if (!root.TryGetProperty("id", out var idEl))
        {
            isNotification = true;
            return null;
        }

        // id must be a number we can represent as long.
        if (idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt64(out var id))
            throw new InvalidOperationException("'id' must be an integer");

        JsonElement? paramsEl = root.TryGetProperty("params", out var p) ? p.Clone() : null;

        return new JsonRpcRequest
        {
            Id = id,
            Method = methodEl.GetString()!,
            Params = paramsEl,
        };
    }

    private async Task DispatchAsync(NdJsonWriter writer, JsonRpcRequest req, CancellationToken ct)
    {
        try
        {
            switch (req.Method)
            {
                case "initialize":
                    await WriteResultAsync(writer, req.Id, McpCapabilities.BuildInitializeResult(), ct);
                    return;

                case "ping":
                    await WriteResultAsync(writer, req.Id, JsonDocument.Parse("{}").RootElement, ct);
                    return;

                case "tools/list":
                    await WriteResultAsync(writer, req.Id, McpCapabilities.BuildToolsList(_tools), ct);
                    return;

                case "tools/call":
                    await DispatchToolCallAsync(writer, req, ct);
                    return;

                default:
                    await WriteErrorAsync(writer, req.Id, McpError.MethodNotFound(req.Method), ct);
                    return;
            }
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(writer, req.Id, McpError.InternalError(ex.Message), ct);
        }
    }

    private async Task DispatchToolCallAsync(NdJsonWriter writer, JsonRpcRequest req, CancellationToken ct)
    {
        if (req.Params is not { ValueKind: JsonValueKind.Object } p)
        {
            await WriteErrorAsync(writer, req.Id, McpError.InvalidParams("'params' must be an object"), ct);
            return;
        }
        if (!p.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
        {
            await WriteErrorAsync(writer, req.Id, McpError.InvalidParams("'name' is required"), ct);
            return;
        }
        var name = nameEl.GetString()!;
        var tool = _tools.Get(name);
        if (tool is null)
        {
            await WriteErrorAsync(writer, req.Id, McpError.MethodNotFound(name), ct);
            return;
        }
        var args = p.TryGetProperty("arguments", out var a) ? a : JsonDocument.Parse("{}").RootElement;

        var progress = new McpProgressReporter(
            TryGetProgressToken(p),
            (notification, token) => writer.WriteAsync(JsonRpcCodec.Serialize(notification), token));
        var context = new ToolInvocationContext(_lifecycle, progress);

        McpToolResult result;
        try { result = await tool.InvokeAsync(args, context, ct); }
        catch (Exception ex) { result = McpToolResult.Error($"tool '{name}' threw: {ex.Message}"); }

        var wrappedJson = "{\"content\":[{\"type\":\"text\",\"text\":" +
            JsonSerializer.Serialize(result.Text) + "}]" +
            (result.IsError ? ",\"isError\":true" : "") + "}";
        await WriteResultAsync(writer, req.Id, JsonDocument.Parse(wrappedJson).RootElement, ct);
    }

    private static JsonElement? TryGetProgressToken(JsonElement toolParams)
    {
        if (!toolParams.TryGetProperty("_meta", out var meta) ||
            meta.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!meta.TryGetProperty("progressToken", out var token))
            return null;

        return token.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? token.Clone()
            : null;
    }

    private static Task WriteResultAsync(NdJsonWriter writer, long id, JsonElement result, CancellationToken ct)
    {
        var resp = JsonRpcResponse.Ok(id, result);
        return writer.WriteAsync(JsonRpcCodec.Serialize(resp), ct);
    }

    private static Task WriteErrorAsync(NdJsonWriter writer, long id, JsonRpcError error, CancellationToken ct)
    {
        var resp = JsonRpcResponse.Fail(id, error);
        return writer.WriteAsync(JsonRpcCodec.Serialize(resp), ct);
    }
}
