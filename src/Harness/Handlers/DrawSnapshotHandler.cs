using System;
using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>draw.snapshot</c>. Returns the currently-buffered draw events without
/// flushing to disk. Safe to call while armed or after disarm; the ring buffer retains
/// until the next <see cref="Recorder.Arm"/> call resets the head.
/// </summary>
public static class DrawSnapshotHandler
{
    public const string Method = "draw.snapshot";

    /// <summary>
    /// Texture-hash manifest used for Tier 2 resolution. Populated by
    /// <c>ModEntry.Entry</c> at startup from the version-specific manifest file.
    /// Defaults to an empty manifest so Tier 2 no-ops when no manifest is present;
    /// Tier 3 (anonymous with hash+size) still fires.
    /// </summary>
    public static TextureHashManifest Manifest { get; set; } = TextureHashManifest.Load("/dev/null");

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        Recorder.SnapshotEvents(out var events, out var meta);

        var snap = new DrawEventSnapshot
        {
            Meta = new SnapshotMeta
            {
                Ticks = meta.Ticks,
                Events = events.Length,
                Dropped = meta.Dropped,
            },
        };

        int resolved = 0;
        foreach (ref readonly var e in events.AsSpan())
        {
            var dto = ToDto(in e);
            if (dto.TextureAsset is not null) resolved++;
            snap.Events.Add(dto);
        }
        snap.Meta.ResolvedCount = resolved;

        return ProtocolJson.ToElement(snap);
    }

    /// <summary>Public because <c>DrawFindHandler</c> (T11) reuses the same projection.</summary>
    public static DrawEventDto ToDto(in DrawEvent e)
    {
        string? path = null;
        string? hash = null;
        int[]? size = null;

        if (e.Texture is { } tex && TextureAssetRegistry.Shared is { } registry)
        {
            try
            {
                var (p, h, w, hh) = registry.TryResolveWithFallback(tex, Manifest);
                path = p;
                hash = h;
                if (w != 0 || hh != 0)
                    size = new[] { w, hh };
            }
            catch { /* GPU-backed render target, disposed, etc. — fall through to null */ }
        }

        return new DrawEventDto
        {
            Tick = e.Tick,
            Call = e.CallIndex,
            TexRef = e.TextureRefId,
            TexW = e.TextureWidth,
            TexH = e.TextureHeight,
            TextureAsset = path,
            ContentHash = hash,
            TextureSize = size,
            Src = e.SourceRect is { } sr ? new[] { sr.X, sr.Y, sr.Width, sr.Height } : null,
            Dst = new[] { e.DestRect.X, e.DestRect.Y, e.DestRect.Width, e.DestRect.Height },
            Col = new[] { (int)e.Color.R, (int)e.Color.G, (int)e.Color.B, (int)e.Color.A },
            Rot = e.Rotation,
            Orig = new[] { e.Origin.X, e.Origin.Y },
            Fx = (int)e.Effects,
            Z = e.LayerDepth,
        };
    }
}
