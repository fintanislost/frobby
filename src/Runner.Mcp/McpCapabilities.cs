using System.Text;
using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp;

internal static class McpCapabilities
{
    public const string ProtocolVersion = "2024-11-05";
    public const string ServerName = "sdv-test-mcp";
    public const string ServerVersion = "0.1.0";

    /// <summary>Body of the <c>initialize</c> response's <c>result</c> field.</summary>
    public static JsonElement BuildInitializeResult()
    {
        var json = $"{{\"protocolVersion\":\"{ProtocolVersion}\"," +
                   $"\"serverInfo\":{{\"name\":\"{ServerName}\",\"version\":\"{ServerVersion}\"}}," +
                   $"\"capabilities\":{{\"tools\":{{}},\"resources\":{{}},\"prompts\":{{}}}}}}";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Serialize the registry as an MCP <c>tools/list</c> response body.</summary>
    public static JsonElement BuildToolsList(ToolRegistry registry)
    {
        var sb = new StringBuilder();
        sb.Append("{\"tools\":[");
        bool first = true;
        foreach (var t in registry.All())
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"name\":");
            sb.Append(JsonSerializer.Serialize(t.Name));
            sb.Append(",\"description\":");
            sb.Append(JsonSerializer.Serialize(t.Description));
            sb.Append(",\"inputSchema\":");
            sb.Append(t.InputSchema.GetRawText());
            sb.Append('}');
        }
        sb.Append("]}");
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }
}
