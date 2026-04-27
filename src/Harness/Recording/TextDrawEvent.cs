using Microsoft.Xna.Framework;

namespace SdvTestFramework.Harness.Recording;

/// <summary>Canonical text draw-call event captured from <c>SpriteBatch.DrawString</c>.</summary>
public struct TextDrawEvent
{
    public int Tick;
    public int CallIndex;
    public string Text;
    public Vector2 Position;
    public Vector2 Size;
    public Color Color;
    public float LayerDepth;
}
