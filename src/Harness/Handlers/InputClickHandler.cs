using System;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>input.click</c>. Sends a mouse click to the active menu.</summary>
public static class InputClickHandler
{
    public const string Method = "input.click";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, () => Game1.activeClickableMenu, () => Game1.ticks);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick)
    {
        var req = RpcParams.Required<InputClickRequest>(paramsElement);
        if (req.X is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x required");
        if (req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y required");
        if (req.X.Value < 0 || req.Y.Value < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x and params.y must be non-negative");

        var button = string.IsNullOrWhiteSpace(req.Button) ? "left" : req.Button.Trim().ToLowerInvariant();
        if (button != "left" && button != "right")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.button must be left or right");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} requires an active menu");

        if (button == "left")
            menu.receiveLeftClick(req.X.Value, req.Y.Value);
        else
            menu.receiveRightClick(req.X.Value, req.Y.Value);

        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }
}
