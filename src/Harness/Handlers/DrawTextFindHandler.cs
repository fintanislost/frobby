using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>draw.text_find</c>.</summary>
public static class DrawTextFindHandler
{
    public const string Method = "draw.text_find";

    private sealed class TextDrawFindResult
    {
        public List<TextDrawEventDto> Events { get; set; } = new();
        public int Count { get; set; }
    }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var filter = RpcParams.Optional<TextDrawFilter>(paramsElement);
        TextDrawFilterMatcher.Validate(filter);
        Recorder.SnapshotTextEvents(out var events, out _);
        if (filter.DisarmAfterSnapshot)
            Recorder.Disarm();

        var result = new TextDrawFindResult();
        foreach (var e in events.AsSpan())
        {
            if (TextDrawFilterMatcher.Matches(in e, filter))
                result.Events.Add(DrawTextSnapshotHandler.ToDto(in e));
        }
        result.Count = result.Events.Count;

        return ProtocolJson.ToElement(result);
    }
}
