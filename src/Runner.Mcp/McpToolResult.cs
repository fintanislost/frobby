using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>Result of a single tool invocation. Wraps the MCP <c>{content, isError}</c> shape.</summary>
public readonly record struct McpToolResult(string Text, bool IsError)
{
    /// <summary>Success result — serialize <paramref name="obj"/> as JSON string.</summary>
    public static McpToolResult Success(JsonElement obj) => new(obj.GetRawText(), false);

    /// <summary>Success result from an already-serialized JSON string.</summary>
    public static McpToolResult SuccessText(string text) => new(text, false);

    /// <summary>Error result — the LLM sees <paramref name="message"/> in the tool output.</summary>
    public static McpToolResult Error(string message) => new(message, true);
}
