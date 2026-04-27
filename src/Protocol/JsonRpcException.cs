using System;

namespace SdvTestFramework.Protocol;

/// <summary>
/// Thrown from codec parse paths. <see cref="Code"/> maps directly onto the JSON-RPC error
/// response code the caller should surface to the peer.
/// </summary>
public sealed class JsonRpcException : Exception
{
    public JsonRpcErrorCode Code { get; }

    public JsonRpcException(JsonRpcErrorCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }
}
