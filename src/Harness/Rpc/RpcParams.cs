using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Harness.Rpc;

/// <summary>
/// Helpers for RPC handler parameter parsing. Centralizes the "required-params + typed
/// error" pattern so every mutator handler produces consistent <see cref="JsonRpcErrorCode"/>
/// mappings for bad input.
/// </summary>
public static class RpcParams
{
    /// <summary>
    /// Deserialize required params into a DTO of type <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="JsonRpcException">
    /// Thrown with <see cref="JsonRpcErrorCode.InvalidParams"/> when:
    /// <list type="bullet">
    ///   <item>params is null</item>
    ///   <item>deserialization returns null</item>
    ///   <item>the payload isn't valid JSON / has wrong field types (<c>JsonException</c> rewrapped)</item>
    /// </list>
    /// </exception>
    public static T Required<T>(JsonElement? paramsElement) where T : class, new()
    {
        if (paramsElement is not { } p)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params required");
        try
        {
            var req = JsonSerializer.Deserialize<T>(p.GetRawText(), ProtocolJson.Options);
            return req ?? throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "empty params");
        }
        catch (JsonException ex)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params parse error: " + ex.Message);
        }
    }

    /// <summary>
    /// Optional-params variant: null params returns <c>new T()</c>; malformed payloads still
    /// surface as <see cref="JsonRpcErrorCode.InvalidParams"/>. Used by handlers whose params
    /// are entirely optional (e.g. <c>draw.arm</c>).
    /// </summary>
    /// <exception cref="JsonRpcException">
    /// Thrown with <see cref="JsonRpcErrorCode.InvalidParams"/> when the payload is present but
    /// not valid JSON / has wrong field types (<c>JsonException</c> rewrapped).
    /// </exception>
    public static T Optional<T>(JsonElement? paramsElement) where T : class, new()
    {
        if (paramsElement is null) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(paramsElement.Value.GetRawText(), ProtocolJson.Options) ?? new T();
        }
        catch (JsonException ex)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params parse error: " + ex.Message);
        }
    }
}
