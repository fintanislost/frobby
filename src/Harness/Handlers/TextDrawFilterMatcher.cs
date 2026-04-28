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
            ValidateRect("filter.in_rect", r);

        if (filter.BoundsWithinRect is { } within)
            ValidateRect("filter.bounds_within_rect", within);

        if (filter.BoundsIntersectsRect is { } intersects)
            ValidateRect("filter.bounds_intersects_rect", intersects);

        if (filter.Color is { } c && c.Length != 4)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"filter.color must be [r, g, b, a] (got length {c.Length})");

        if (filter.ColorAny is { } colors)
        {
            for (var i = 0; i < colors.Length; i++)
            {
                if (colors[i].Length != 4)
                    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                        $"filter.color_any[{i}] must be [r, g, b, a] (got length {colors[i].Length})");
            }
        }

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

        if (f.ColorAny is { Length: > 0 } colorAny && !MatchesAnyColor(e.Color, colorAny))
            return false;

        if (f.InRect is { Length: 4 } r)
        {
            var rect = new Rectangle(r[0], r[1], r[2], r[3]);
            if (!rect.Contains((int)e.Position.X, (int)e.Position.Y)) return false;
        }

        if (f.BoundsWithinRect is { Length: 4 } within)
        {
            var rect = new Rectangle(within[0], within[1], within[2], within[3]);
            if (!rect.Contains(e.Bounds)) return false;
        }

        if (f.BoundsIntersectsRect is { Length: 4 } intersects)
        {
            var rect = new Rectangle(intersects[0], intersects[1], intersects[2], intersects[3]);
            if (!rect.Intersects(e.Bounds)) return false;
        }

        if (f.LayerDepthRange is { Length: 2 } ldr)
        {
            if (e.LayerDepth < ldr[0] || e.LayerDepth > ldr[1]) return false;
        }

        return true;
    }

    private static bool MatchesAnyColor(Color eventColor, int[][] colors)
    {
        foreach (var c in colors)
        {
            if (c.Length == 4 &&
                eventColor.R == c[0] &&
                eventColor.G == c[1] &&
                eventColor.B == c[2] &&
                eventColor.A == c[3])
                return true;
        }

        return false;
    }

    private static void ValidateRect(string name, int[] rect)
    {
        if (rect.Length != 4)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"{name} must be [x, y, w, h] (got length {rect.Length})");
        if (rect[2] < 0 || rect[3] < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"{name} width/height must be >= 0");
    }

}
