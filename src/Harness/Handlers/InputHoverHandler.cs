using System;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>input.hover</c>. Moves the deterministic cursor and sends hover to the active menu.</summary>
public static class InputHoverHandler
{
    public const string Method = "input.hover";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, () => Game1.activeClickableMenu, () => Game1.ticks);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick)
    {
        var req = RpcParams.Required<InputHoverRequest>(paramsElement);
        HoverAt(req.X, req.Y, getActiveMenu, Method);

        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }

    internal static void HoverAt(int? x, int? y, Func<IClickableMenu?> getActiveMenu, string method)
    {
        if (x is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x required");
        if (y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y required");
        if (x.Value < 0 || y.Value < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x and params.y must be non-negative");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{method} requires an active menu");

        ControlledCursor.Set(x.Value, y.Value);
        menu.performHoverAction(x.Value, y.Value);
    }
}
