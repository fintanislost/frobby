using System;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal static class TextDrawFilterMatcher
{
    public static void Validate(TextDrawFilter filter)
    {
        if (filter.InRect is { } r)
        {
            if (r.Length != 4)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    $"filter.in_rect must be [x, y, w, h] (got length {r.Length})");
            if (r[2] < 0 || r[3] < 0)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    "filter.in_rect width/height must be >= 0");
        }

        if (filter.Color is { } c && c.Length != 4)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"filter.color must be [r, g, b, a] (got length {c.Length})");

        if (filter.LayerDepthRange is { } ldr)
        {
            if (ldr.Length != 2)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    $"filter.layer_depth_range must be [min, max] (got length {ldr.Length})");
            if (ldr[0] > ldr[1])
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    $"filter.layer_depth_range min ({ldr[0]}) must be <= max ({ldr[1]})");
        }
    }

    public static bool Matches(in TextDrawEvent e, TextDrawFilter f)
    {
        var comparison = f.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!string.IsNullOrEmpty(f.TextContains) &&
            (e.Text ?? string.Empty).IndexOf(f.TextContains, comparison) < 0)
            return false;

        if (f.TextEquals is { } equals &&
            !string.Equals(e.Text ?? string.Empty, equals, comparison))
            return false;

        if (f.Color is { Length: 4 } c &&
            (e.Color.R != c[0] || e.Color.G != c[1] || e.Color.B != c[2] || e.Color.A != c[3]))
            return false;

        if (f.InRect is { Length: 4 } r)
        {
            var rect = new Rectangle(r[0], r[1], r[2], r[3]);
            if (!rect.Contains((int)e.Position.X, (int)e.Position.Y)) return false;
        }

        if (f.LayerDepthRange is { Length: 2 } ldr)
        {
            if (e.LayerDepth < ldr[0] || e.LayerDepth > ldr[1]) return false;
        }

        return true;
    }
}
