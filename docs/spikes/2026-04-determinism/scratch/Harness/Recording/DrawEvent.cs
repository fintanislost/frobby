using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SdvTestFramework.SpikeHarness.Recording;

/// <summary>
/// Canonical draw-call event. All seven SpriteBatch.Draw overloads are normalized into this
/// shape. Fields that a given overload doesn't supply get their documented default:
/// rotation=0, origin=(0,0), effects=None, layerDepth=0.
/// </summary>
/// <remarks>
/// Struct (not class/record) by design — the hot path allocates ~thousands per tick and GC
/// pressure from boxing would dominate capture overhead. See draw-call-recorder.md.
/// </remarks>
public struct DrawEvent
{
    public int Tick;
    public int CallIndex;

    /// <summary>Per-process-stable texture identity (RuntimeHelpers.GetHashCode). Normalized out in the post-processing diff — see scratch/analyze.py.</summary>
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
}
