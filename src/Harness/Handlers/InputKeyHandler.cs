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

        if (!TryParseKeyName(req.Key, out var key))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown key: {req.Key}");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires an active menu");

        menu.receiveKeyPress(key);
        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }

    private static bool TryParseKeyName(string rawKey, out Keys key)
    {
        key = default;
        var keyName = rawKey.Trim();
        foreach (var name in Enum.GetNames(typeof(Keys)))
        {
            if (!string.Equals(name, keyName, StringComparison.OrdinalIgnoreCase))
                continue;

            key = Enum.Parse<Keys>(name);
            return key != Keys.None;
        }

        return false;
    }
}
