using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Canonical draw-call event. All seven <see cref="SpriteBatch.Draw"/> overloads are
/// normalized into this shape. Fields that a given overload doesn't supply get their
/// documented defaults: rotation=0, origin=(0,0), effects=None, layerDepth=0.
/// </summary>
/// <remarks>
/// Struct (not record-class) by design — hot path allocates thousands per tick; boxing
/// pressure from a reference-type event would dominate capture overhead.
/// See <c>.claude/rules/draw-call-recorder.md</c>.
/// </remarks>
public struct DrawEvent
{
    public int Tick;
    public int CallIndex;

    /// <summary>
    /// Live reference to the source <see cref="Texture2D"/>, held so
    /// <c>TextureAssetRegistry</c> can resolve its asset path at snapshot time (D1.5 Tier 1).
    /// Null is legal (e.g. event constructed in tests). The reference pins the texture from
    /// GC while the ring buffer holds it — acceptable because SDV textures are long-lived.
    /// </summary>
    public Texture2D? Texture;

    /// <summary>Per-process-stable texture identity (<c>RuntimeHelpers.GetHashCode</c>).
    /// Normalized out in cross-run diffs — see the analyzer.</summary>
    public int TextureRefId;
    public int TextureWidth;
    public int TextureHeight;

    public Rectangle? SourceRect;
    public Rectangle DestRect;
    public Color Color;
    public float Rotation;
    public Vector2 Origin;
    public SpriteEffects Effects;
    public float LayerDepth;

    /// <summary>16-hex-char prefix of SHA-256(texture pixels). Populated by Tier 2 hash fallback. Nullable.</summary>
    public string? ContentHash;

    /// <summary>Texture dimensions as [width, height]. Matches TextureWidth/TextureHeight but as array for filter convenience. Nullable until backfilled.</summary>
    public int[]? TextureSize;
}
