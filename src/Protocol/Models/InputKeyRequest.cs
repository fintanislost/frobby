namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.key</c>.</summary>
public sealed class InputKeyRequest
{
    /// <summary>MonoGame key name, e.g. <c>"Enter"</c>, <c>"Escape"</c>, <c>"E"</c>.</summary>
    public string Key { get; set; } = string.Empty;
}
