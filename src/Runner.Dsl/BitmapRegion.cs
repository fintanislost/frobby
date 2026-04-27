namespace SdvTestFramework.Runner.Dsl;

/// <summary>Sub-rect for <see cref="Bitmap.Capture"/>. All fields non-negative; w + h &gt; 0.</summary>
public readonly record struct BitmapRegion(int X, int Y, int W, int H);
