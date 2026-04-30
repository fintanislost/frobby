namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.hover</c>.</summary>
public sealed class InputHoverRequest
{
    /// <summary>Screen-space X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Screen-space Y coordinate.</summary>
    public int? Y { get; set; }
}
