namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.click_menu_choice</c>.</summary>
public sealed class InputClickMenuChoiceRequest
{
    /// <summary>Exact response key to click.</summary>
    public string? Key { get; set; }

    /// <summary>Substring match against visible response text.</summary>
    public string? Text { get; set; }

    /// <summary>Exact match against visible response text.</summary>
    public string? TextEquals { get; set; }

    /// <summary>Regex match against visible response text.</summary>
    public string? TextMatches { get; set; }

    /// <summary>Whether text matching is case-sensitive.</summary>
    public bool CaseSensitive { get; set; } = true;

    /// <summary>Mouse button to send. Supported values are <c>left</c> and <c>right</c>.</summary>
    public string Button { get; set; } = "left";
}
