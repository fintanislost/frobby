using System.Text.Json.Nodes;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for the <c>content.asset</c> RPC.</summary>
public sealed class ContentAssetResult
{
    public string Name { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public string Kind { get; set; } = "missing";
    public string RuntimeType { get; set; } = string.Empty;

    /// <summary>
    /// Asset-specific bounded metadata. JsonObject is intentional so runtime asset keys
    /// like <c>ExampleTownEast</c> are not transformed by the protocol dictionary naming policy.
    /// </summary>
    public JsonObject Summary { get; set; } = new();
}
