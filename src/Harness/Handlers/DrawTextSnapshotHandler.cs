using System;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>draw.text_snapshot</c>.</summary>
public static class DrawTextSnapshotHandler
{
    public const string Method = "draw.text_snapshot";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        Recorder.SnapshotTextEvents(out var events, out var meta);

        var snap = new TextDrawEventSnapshot
        {
            Meta = new TextDrawSnapshotMetadata
            {
                Ticks = meta.Ticks,
                Events = events.Length,
                Dropped = meta.Dropped,
            },
        };

        foreach (ref readonly var e in events.AsSpan())
            snap.Events.Add(ToDto(in e));

        return ProtocolJson.ToElement(snap);
    }

    public static TextDrawEventDto ToDto(in TextDrawEvent e)
    {
        var bounds = e.Bounds;
        return new TextDrawEventDto
        {
            Tick = e.Tick,
            Call = e.CallIndex,
            Text = e.Text ?? string.Empty,
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            Color = new[] { (int)e.Color.R, (int)e.Color.G, (int)e.Color.B, (int)e.Color.A },
            LayerDepth = e.LayerDepth,
        };
    }
}
