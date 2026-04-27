using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Shared validation for <see cref="DrawFilter"/> shape invariants used by both
/// <c>draw.find</c> and <c>draw.assert_contains</c>. Silent-accept-then-never-match was a
/// footgun called out in T11's code review — explicit <see cref="JsonRpcErrorCode.InvalidParams"/>
/// surfaces malformed filters loudly.
/// </summary>
internal static class DrawFilterValidator
{
    public static void Validate(DrawFilter filter)
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

        if (filter.SourceRect is { } sr)
        {
            if (sr.Length != 4)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    $"filter.source_rect must be [x, y, w, h] (got length {sr.Length})");
            if (sr[2] < 0 || sr[3] < 0)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    "filter.source_rect width/height must be >= 0");
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

        if (filter.ContentHash is { Length: > 0 } ch)
        {
            foreach (var hex in ch)
            {
                bool isHex = (hex >= '0' && hex <= '9') || (hex >= 'a' && hex <= 'f') || (hex >= 'A' && hex <= 'F');
                if (!isHex)
                    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                        $"filter.content_hash must be hex chars only (got '{ch}')");
            }
        }

        if (filter.TextureSize is { } ts)
        {
            if (ts.Length != 2 || ts[0] <= 0 || ts[1] <= 0)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    "filter.texture_size must be a 2-element array of positive integers");
        }
    }
}
