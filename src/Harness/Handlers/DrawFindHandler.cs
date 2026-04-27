using System;
using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>draw.find</c>. Returns all captured draw events matching the supplied
/// filter. An empty filter returns every event.
/// </summary>
public static class DrawFindHandler
{
    public const string Method = "draw.find";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var filter = RpcParams.Optional<DrawFilter>(paramsElement);
        DrawFilterValidator.Validate(filter);
        Recorder.SnapshotEvents(out var events, out _);

        var result = new DrawFindResult();
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
            if (DrawFilterMatcher.Matches(in enriched, filter))
                result.Events.Add(DrawSnapshotHandler.ToDto(in enriched));
        }
        result.Count = result.Events.Count;

        return ProtocolJson.ToElement(result);
    }
}
