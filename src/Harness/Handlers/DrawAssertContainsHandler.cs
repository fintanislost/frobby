using System;
using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>draw.assert_contains</c>. Counts matches of a filter against the captured buffer and returns pass/fail against <c>min_count</c>.</summary>
public static class DrawAssertContainsHandler
{
    public const string Method = "draw.assert_contains";

    private sealed class AssertRequest
    {
        // Must stay nullable so `{"filter": null}` deserializes as null rather than being
        // coerced silently — handler then rewrites to new DrawFilter().
        public DrawFilter? Filter { get; set; } = new();
        public int MinCount { get; set; } = 1;
        public string? Message { get; set; }
    }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AssertRequest>(paramsElement);

        // Guard: caller sent `{"filter": null}`. Treat as empty filter (matches everything).
        req.Filter ??= new DrawFilter();
        DrawFilterValidator.Validate(req.Filter);

        if (req.MinCount < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.min_count must be >= 1");

        Recorder.SnapshotEvents(out var events, out _);
        int matched = 0;
        foreach (var e in events.AsSpan())
        {
            var enriched = e;   // struct copy — leave ring-buffer original intact
            if (e.Texture is { } tex && TextureAssetRegistry.Shared is { } registry)
            {
                try
                {
                    var (_, hash, w, h) = registry.TryResolveWithFallback(tex, DrawSnapshotHandler.Manifest);
                    enriched.ContentHash = hash;
                    if (w != 0 || h != 0)
                        enriched.TextureSize = new[] { w, h };
                }
                catch { /* GPU-backed, disposed, etc. — leave null */ }
            }
            if (DrawFilterMatcher.Matches(in enriched, req.Filter))
                matched++;
        }

        return ProtocolJson.ToElement(new AssertResult
        {
            MinCount = req.MinCount,
            MatchedCount = matched,
            Passed = matched >= req.MinCount,
            Message = req.Message,
        });
    }
}
