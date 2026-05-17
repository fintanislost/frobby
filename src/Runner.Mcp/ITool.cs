using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>MCP tool contract. Each tool is stateless; state lives in <see cref="SdvLifecycle"/>.</summary>
public interface ITool
{
    /// <summary>Tool name — unique within a <see cref="ToolRegistry"/>.</summary>
    string Name { get; }

    /// <summary>Human-readable description, shown in tools/list responses.</summary>
    string Description { get; }

    /// <summary>JSON Schema describing the tool's <c>arguments</c> object.</summary>
    JsonElement InputSchema { get; }

    /// <summary>Invoke the tool. Context carries lifecycle plus optional request-scoped MCP utilities.</summary>
    Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct);
}
