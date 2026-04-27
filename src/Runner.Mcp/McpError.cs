using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

internal static class McpError
{
    // MCP uses standard JSON-RPC 2.0 error codes — all defined as named members in JsonRpcErrorCode.
    public static JsonRpcError InvalidRequest(string message = "Invalid Request")
        => new(JsonRpcErrorCode.InvalidRequest, message);

    public static JsonRpcError MethodNotFound(string method)
        => new(JsonRpcErrorCode.MethodNotFound, $"Method not found: {method}");

    public static JsonRpcError InvalidParams(string message)
        => new(JsonRpcErrorCode.InvalidParams, $"Invalid params: {message}");

    public static JsonRpcError InternalError(string message)
        => new(JsonRpcErrorCode.InternalError, $"Internal error: {message}");
}
