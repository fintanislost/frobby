using System;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>draw.assert_text_not_contains</c>.</summary>
public static class DrawAssertTextNotContainsHandler
{
    public const string Method = "draw.assert_text_not_contains";

    private sealed class AssertRequest
    {
        public TextDrawFilter? Filter { get; set; } = new();
        public string? Message { get; set; }
    }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AssertRequest>(paramsElement);
        req.Filter ??= new TextDrawFilter();
        TextDrawFilterMatcher.Validate(req.Filter);

        Recorder.SnapshotTextEvents(out var events, out _);
        int matched = 0;
        foreach (var e in events.AsSpan())
        {
            if (TextDrawFilterMatcher.Matches(in e, req.Filter))
                matched++;
        }

        return ProtocolJson.ToElement(new AssertResult
        {
            MinCount = 0,
            MatchedCount = matched,
            Passed = matched == 0,
            Message = req.Message,
        });
    }
}
