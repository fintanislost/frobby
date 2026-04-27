using System;
using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>draw.assert_not_contains</c>. Counts matches of a filter against
/// the captured buffer and returns pass when matches == 0 (inverse of draw.assert_contains).</summary>
public static class DrawAssertNotContainsHandler
{
    public const string Method = "draw.assert_not_contains";

    private sealed class AssertRequest
    {
        public DrawFilter? Filter { get; set; } = new();
        public string? Message { get; set; }
    }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AssertRequest>(paramsElement);
        req.Filter ??= new DrawFilter();
        DrawFilterValidator.Validate(req.Filter);

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
            // Reuse AssertResult DTO — min_count isn't meaningful here, but the shape
            // (passed/matched_count/message) is otherwise identical, and scenario consumers
            // can treat "not_contains passed" identically to "contains passed."
            MinCount = 0,
            MatchedCount = matched,
            Passed = matched == 0,
            Message = req.Message,
        });
    }
}
