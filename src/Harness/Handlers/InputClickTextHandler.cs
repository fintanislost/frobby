using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>input.click_text</c>. Clicks the center of captured text bounds.</summary>
public static class InputClickTextHandler
{
    public const string Method = "input.click_text";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(
            paramsElement,
            () => Game1.activeClickableMenu,
            () => Game1.ticks,
            SnapshotTextEvents);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick,
        Func<TextDrawEvent[]> getTextEvents)
    {
        var req = RpcParams.Required<InputClickTextRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.Text)
            && string.IsNullOrWhiteSpace(req.TextEquals)
            && string.IsNullOrWhiteSpace(req.TextMatches))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.text, params.text_equals, or params.text_matches required");
        if (req.Occurrence < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.occurrence must be >= 1");

        var button = string.IsNullOrWhiteSpace(req.Button) ? "left" : req.Button.Trim().ToLowerInvariant();
        if (button != "left" && button != "right")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.button must be left or right");

        var filter = new TextDrawFilter
        {
            TextContains = req.Text,
            TextEquals = req.TextEquals,
            TextMatches = req.TextMatches,
            CaseSensitive = req.CaseSensitive,
            InRect = req.InRect,
            BoundsWithinRect = req.BoundsWithinRect,
            BoundsIntersectsRect = req.BoundsIntersectsRect,
        };
        TextDrawFilterMatcher.Validate(filter);

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} requires an active menu");

        var match = FindOccurrence(getTextEvents(), filter, req.Occurrence);
        if (match is null)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"{Method} could not find captured text: {TextLabel(req)}");

        var bounds = match.Value.Bounds;
        var x = bounds.X + bounds.Width / 2;
        var y = bounds.Y + bounds.Height / 2;

        if (button == "left")
            menu.receiveLeftClick(x, y);
        else
            menu.receiveRightClick(x, y);

        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }

    private static TextDrawEvent[] SnapshotTextEvents()
    {
        Recorder.SnapshotTextEvents(out var events, out _);
        return events;
    }

    private static TextDrawEvent? FindOccurrence(IEnumerable<TextDrawEvent> events, TextDrawFilter filter, int occurrence)
    {
        var seen = 0;
        foreach (var e in events)
        {
            if (!TextDrawFilterMatcher.Matches(in e, filter))
                continue;

            seen++;
            if (seen == occurrence)
                return e;
        }

        return null;
    }

    private static string TextLabel(InputClickTextRequest req)
        => req.TextEquals ?? req.Text ?? req.TextMatches ?? string.Empty;
}
