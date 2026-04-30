namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.click_menu_button</c>.</summary>
public sealed class InputClickMenuButtonRequest
{
    /// <summary>Exact internal button id to click.</summary>
    public string? Id { get; set; }

    /// <summary>Exact button label to click.</summary>
    public string? Label { get; set; }

    /// <summary>Alias for <see cref="Label"/> for consistency with text-click steps.</summary>
    public string? TextEquals { get; set; }

    /// <summary>Whether label matching is case-sensitive.</summary>
    public bool CaseSensitive { get; set; } = true;

    /// <summary>Mouse button to send. Supported values are <c>left</c> and <c>right</c>.</summary>
    public string Button { get; set; } = "left";

    /// <summary>Number of times to click the resolved button region.</summary>
    public int Repeat { get; set; } = 1;
}
