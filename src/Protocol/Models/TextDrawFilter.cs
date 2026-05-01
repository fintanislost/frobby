namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Filter DSL for <c>draw.text_find</c> / text assertions. All supplied fields AND together;
/// a filter with no fields matches every captured text draw event.
/// </summary>
public sealed class TextDrawFilter
{
    public string? TextContains { get; set; }
    public string? TextEquals { get; set; }
    public string? TextMatches { get; set; }
    public bool CaseSensitive { get; set; } = true;
    public int[]? InRect { get; set; }
    public int[]? BoundsWithinRect { get; set; }
    public int[]? BoundsIntersectsRect { get; set; }
    public int[]? Color { get; set; }
    public int[][]? ColorAny { get; set; }
    public float[]? LayerDepthRange { get; set; }
}
