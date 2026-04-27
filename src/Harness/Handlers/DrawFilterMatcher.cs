using System;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal static class DrawFilterMatcher
{
    public static bool Matches(in DrawEvent e, DrawFilter f)
    {
        if (f.Color is { Length: 4 } c &&
            (e.Color.R != c[0] || e.Color.G != c[1] || e.Color.B != c[2] || e.Color.A != c[3]))
            return false;

        if (f.InRect is { Length: 4 } r)
        {
            var rect = new Rectangle(r[0], r[1], r[2], r[3]);
            if (!rect.Contains(e.DestRect)) return false;
        }

        if (f.LayerDepthRange is { Length: 2 } ldr)
        {
            if (e.LayerDepth < ldr[0] || e.LayerDepth > ldr[1]) return false;
        }

        if (f.SourceRect is { Length: 4 } sr)
        {
            if (e.SourceRect is not { } actual ||
                actual.X != sr[0] || actual.Y != sr[1] || actual.Width != sr[2] || actual.Height != sr[3])
                return false;
        }

        // texture_asset filter (D1.5 Tier 1): resolve the event's texture via the shared
        // registry and compare on the resolved path. Unresolved events (Tier 3 anonymous)
        // never match a path filter; use a filter without texture_asset + secondary fields
        // (e.g. tex_w, source_rect) to query those.
        if (!string.IsNullOrEmpty(f.TextureAsset))
        {
            var resolved = Assets.TextureAssetRegistry.Shared?.TryResolve(e.Texture);
            if (resolved is null || resolved != f.TextureAsset)
                return false;
        }

        if (f.ContentHash is { Length: > 0 } hashPrefix)
        {
            if (e.ContentHash is null) return false;
            if (!e.ContentHash.StartsWith(hashPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (f.TextureSize is { Length: 2 } size)
        {
            if (e.TextureSize is not { Length: 2 } evtSize) return false;
            if (evtSize[0] != size[0] || evtSize[1] != size[1]) return false;
        }

        return true;
    }
}
