using System;
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

    public readonly Rectangle Bounds => new(
        (int)Position.X,
        (int)Position.Y,
        ToPixelSize(Size.X),
        ToPixelSize(Size.Y));

    public static Vector2 NormalizeSize(Vector2 size) =>
        new(Math.Abs(size.X), Math.Abs(size.Y));

    private static int ToPixelSize(float value) =>
        Math.Max(0, (int)Math.Ceiling(Math.Abs(value)));
}
