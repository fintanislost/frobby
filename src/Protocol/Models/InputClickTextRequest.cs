namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.click_text</c>.</summary>
public sealed class InputClickTextRequest
{
    /// <summary>Text fragment to find in captured draw-string events.</summary>
    public string? Text { get; set; }

    /// <summary>Exact text to find in captured draw-string events.</summary>
    public string? TextEquals { get; set; }

    /// <summary>Regular expression to match in captured draw-string events.</summary>
    public string? TextMatches { get; set; }

    /// <summary>Whether text matching is case-sensitive.</summary>
    public bool CaseSensitive { get; set; } = true;

    /// <summary>One-based occurrence after filtering. Defaults to the first match.</summary>
    public int Occurrence { get; set; } = 1;

    /// <summary>Mouse button to send. Supported values are <c>left</c> and <c>right</c>.</summary>
    public string Button { get; set; } = "left";

    public int[]? InRect { get; set; }
    public int[]? BoundsWithinRect { get; set; }
    public int[]? BoundsIntersectsRect { get; set; }
}
