using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>draw.assert_text_contains</c>.</summary>
public static class DrawAssertTextContainsHandler
{
    public const string Method = "draw.assert_text_contains";
    private const int VisualTextInstanceTolerancePx = 4;

    private sealed class AssertRequest
    {
        public TextDrawFilter? Filter { get; set; } = new();
        public int MinCount { get; set; } = 1;
        public int? MaxCount { get; set; }
        public string? Message { get; set; }
    }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<AssertRequest>(paramsElement);
        req.Filter ??= new TextDrawFilter();
        TextDrawFilterMatcher.Validate(req.Filter);

        if (req.MinCount < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.min_count must be >= 1");
        if (req.MaxCount is { } maxCount && maxCount < req.MinCount)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.max_count must be >= params.min_count");

        Recorder.SnapshotTextEvents(out var events, out _);
        int matched = CountMatchingVisibleTextInstances(events, req.Filter);

        return ProtocolJson.ToElement(new AssertResult
        {
            MinCount = req.MinCount,
            MaxCount = req.MaxCount,
            MatchedCount = matched,
            Passed = matched >= req.MinCount && (req.MaxCount is null || matched <= req.MaxCount.Value),
            Message = req.Message,
        });
    }

    private static int CountMatchingVisibleTextInstances(TextDrawEvent[] events, TextDrawFilter filter)
    {
        var instances = new List<TextDrawEvent>();
        foreach (var e in events.AsSpan())
        {
            if (!TextDrawFilterMatcher.Matches(in e, filter))
                continue;

            if (HasSameVisibleTextInstance(instances, in e))
                continue;

            instances.Add(e);
        }

        return instances.Count;
    }

    private static bool HasSameVisibleTextInstance(List<TextDrawEvent> instances, in TextDrawEvent candidate)
    {
        for (int i = 0; i < instances.Count; i++)
        {
            var existing = instances[i];
            if (SameVisibleTextInstance(in existing, in candidate))
                return true;
        }

        return false;
    }

    private static bool SameVisibleTextInstance(in TextDrawEvent a, in TextDrawEvent b)
    {
        if (!string.Equals(a.Text, b.Text, StringComparison.Ordinal))
            return false;

        var aBounds = a.Bounds;
        var bBounds = b.Bounds;
        return Math.Abs(aBounds.X - bBounds.X) <= VisualTextInstanceTolerancePx
            && Math.Abs(aBounds.Y - bBounds.Y) <= VisualTextInstanceTolerancePx
            && Math.Abs(aBounds.Width - bBounds.Width) <= VisualTextInstanceTolerancePx
            && Math.Abs(aBounds.Height - bBounds.Height) <= VisualTextInstanceTolerancePx;
    }
}
