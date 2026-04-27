namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.text</c>.</summary>
public sealed class InputTextRequest
{
    /// <summary>Text to send to the active menu.</summary>
    public string? Text { get; set; }

    /// <summary>Whether to send <c>Enter</c> after the text.</summary>
    public bool Submit { get; set; }
}
