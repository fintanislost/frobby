using System;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Wire-format ↔ enum conversion for <see cref="BitmapMethod"/>. Lives in Runner
/// (not Protocol) because Protocol is the cross-target shared layer and this is
/// a Runner-only concern; Harness doesn't deserialize bitmap-method strings.
/// </summary>
internal static class BitmapMethodExtensions
{
    /// <summary>Parse a wire-format method string (kebab-case) to a <see cref="BitmapMethod"/>. Null → ssim. Throws <see cref="ArgumentException"/> on unknown.</summary>
    public static BitmapMethod ParseMethod(string? wireForm) =>
        (wireForm ?? "ssim") switch
        {
            "ssim" => BitmapMethod.Ssim,
            "pixel-exact" => BitmapMethod.PixelExact,
            "dhash" => BitmapMethod.DHash,
            _ => throw new ArgumentException($"unknown bitmap method: '{wireForm}'"),
        };
}
