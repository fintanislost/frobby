using System.Text.Json;

namespace SdvTestFramework.Protocol;

/// <summary>JSON-RPC 2.0 request. <c>id</c> is a <see cref="long"/>; we never generate string IDs.</summary>
public sealed class JsonRpcRequest
{
    public long Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public JsonElement? Params { get; set; }
}

/// <summary>JSON-RPC 2.0 notification (no <c>id</c>).</summary>
public sealed class JsonRpcNotification
{
    public string Method { get; set; } = string.Empty;
    public JsonElement? Params { get; set; }
}
