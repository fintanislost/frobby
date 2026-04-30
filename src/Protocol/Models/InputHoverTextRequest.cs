namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>input.hover_text</c>.</summary>
public sealed class InputHoverTextRequest
{
    /// <summary>Text fragment to find in captured draw-string events.</summary>
    public string? Text { get; set; }

    /// <summary>Exact text to find in captured draw-string events.</summary>
    public string? TextEquals { get; set; }

    /// <summary>Whether text matching is case-sensitive.</summary>
    public bool CaseSensitive { get; set; } = true;

    /// <summary>One-based occurrence after filtering. Defaults to the first match.</summary>
    public int Occurrence { get; set; } = 1;

    public int[]? InRect { get; set; }
    public int[]? BoundsWithinRect { get; set; }
    public int[]? BoundsIntersectsRect { get; set; }
}
