namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.click_menu_advance</c>.</summary>
public sealed class InputClickMenuAdvanceRequest
{
    /// <summary>Mouse button to send. Supported values are <c>left</c> and <c>right</c>.</summary>
    public string Button { get; set; } = "left";
}
