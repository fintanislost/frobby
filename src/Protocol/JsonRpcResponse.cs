using System.Text.Json;

namespace SdvTestFramework.Protocol;

/// <summary>JSON-RPC 2.0 response. Either <see cref="Result"/> or <see cref="Error"/> is set, not both.</summary>
public sealed class JsonRpcResponse
{
    public long Id { get; set; }
    public JsonElement? Result { get; set; }
    public JsonRpcError? Error { get; set; }

    public static JsonRpcResponse Ok(long id, JsonElement result) =>
        new() { Id = id, Result = result };

    public static JsonRpcResponse Fail(long id, JsonRpcError error) =>
        new() { Id = id, Error = error };
}

/// <summary>JSON-RPC 2.0 error object.</summary>
public sealed class JsonRpcError
{
    public JsonRpcErrorCode Code { get; }
    public string Message { get; }
    public JsonElement? Data { get; }

    public JsonRpcError(JsonRpcErrorCode code, string message, JsonElement? data = null)
    {
        Code = code;
        Message = message;
        Data = data;
    }
}
