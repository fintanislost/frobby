namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.click</c>.</summary>
public sealed class InputClickRequest
{
    /// <summary>Screen-space X coordinate.</summary>
    public int? X { get; set; }

    /// <summary>Screen-space Y coordinate.</summary>
    public int? Y { get; set; }

    /// <summary>Mouse button to send. Supported values are <c>left</c> and <c>right</c>.</summary>
    public string Button { get; set; } = "left";
}
