using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SdvTestFramework.Harness.Assets;

/// <summary>
/// SHA-256 over texture pixel data. Tier 2 of the texture-resolution cascade uses this
/// to look up textures missed by Tier 1's <c>IContentEvents.AssetReady</c> hook.
/// </summary>
/// <remarks>
/// Runs on the game thread — <see cref="Texture2D.GetData{T}(T[])"/> requires it.
/// Expect ~1ms for a 512x1002 portrait; cheap enough for on-demand use.
/// </remarks>
public static class TextureHasher
{
    /// <summary>Compute the 16-hex-char prefix of SHA-256 over the texture's pixel data.</summary>
    public static string ComputeHashHexPrefix(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        var bytes = MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
        return ComputeHashHexPrefix(bytes);
    }

    /// <summary>Same as above but accepts a raw byte buffer — used by tests that can't construct a GPU-backed Texture2D.</summary>
    public static string ComputeHashHexPrefix(byte[] data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        // 16 hex chars = 8 bytes = 64 bits of hash. Collision prob ≈ 2^-64 × N²/2.
        // For a ~5K-entry manifest: 2^-64 × 12.5M ≈ 7e-13 — safe.
        return Convert.ToHexString(hash.Slice(0, 8)).ToLowerInvariant();
    }
}
