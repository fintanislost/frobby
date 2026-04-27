namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Filter DSL for <c>draw.find</c> / <c>draw.assert_contains</c>. All supplied fields AND
/// together; a filter with no fields matches every event.
/// </summary>
public sealed class DrawFilter
{
    /// <summary>
    /// M1: match on stringified <see cref="DrawEventDto.TexRef"/>. D1.5 will resolve real
    /// asset paths via the SMAPI content-pipeline hook.
    /// </summary>
    public string? TextureAsset { get; set; }

    /// <summary>[x, y, w, h] — dest rect must be fully contained.</summary>
    public int[]? InRect { get; set; }

    /// <summary>[min, max] inclusive.</summary>
    public float[]? LayerDepthRange { get; set; }

    /// <summary>[r, g, b, a] — exact match.</summary>
    public int[]? Color { get; set; }

    /// <summary>[x, y, w, h] — exact match. Events with null source rect do NOT match this filter.</summary>
    public int[]? SourceRect { get; set; }

    /// <summary>Match draw events whose <c>ContentHash</c> starts with this hex string (prefix match — users can pass 8, 16, or full).</summary>
    public string? ContentHash { get; set; }

    /// <summary>Match draw events whose texture dimensions exactly equal this [width, height].</summary>
    public int[]? TextureSize { get; set; }
}
