using System;
using System.Text.Json;
using Microsoft.Xna.Framework.Input;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>input.key</c>. Sends a key press to the active menu.</summary>
public static class InputKeyHandler
{
    public const string Method = "input.key";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, () => Game1.activeClickableMenu, () => Game1.ticks);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick)
    {
        var req = RpcParams.Required<InputKeyRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.Key))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.key required");

        if (!Enum.TryParse<Keys>(req.Key, ignoreCase: true, out var key)
            || !Enum.IsDefined(key))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown key: {req.Key}");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires an active menu");

        menu.receiveKeyPress(key);
        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }
}
