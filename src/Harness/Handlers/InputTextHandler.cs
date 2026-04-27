using System;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework.Input;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>input.text</c>. Sends text to the active menu.</summary>
public static class InputTextHandler
{
    public const string Method = "input.text";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, () => Game1.activeClickableMenu, () => Game1.ticks);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick)
    {
        var req = RpcParams.Required<InputTextRequest>(paramsElement);
        if (req.Text == null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.text required");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires an active menu");

        var textInput = FindTextInputMethod(menu);
        if (textInput != null)
        {
            foreach (var c in req.Text)
                InvokeTextInput(menu, textInput, c);
        }
        else
        {
            var keys = MapFallbackKeys(req.Text);
            foreach (var key in keys)
                menu.receiveKeyPress(key);
        }

        if (req.Submit)
            menu.receiveKeyPress(Keys.Enter);

        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }

    private static MethodInfo? FindTextInputMethod(IClickableMenu menu)
    {
        foreach (var method in menu.GetType().GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "receiveTextInput", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(method.Name, "ReceiveTextInput", StringComparison.OrdinalIgnoreCase))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 1
                && (parameters[0].ParameterType == typeof(char) || parameters[0].ParameterType == typeof(string)))
                return method;
        }

        return null;
    }

    private static void InvokeTextInput(IClickableMenu menu, MethodInfo textInput, char c)
    {
        var parameterType = textInput.GetParameters()[0].ParameterType;
        var arg = parameterType == typeof(char) ? (object)c : c.ToString();
        textInput.Invoke(menu, new[] { arg });
    }

    private static Keys[] MapFallbackKeys(string text)
    {
        var keys = new Keys[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (!TryMapFallbackKey(text[i], out keys[i]))
                throw new JsonRpcException(
                    JsonRpcErrorCode.InvalidParams,
                    $"unsupported character for {Method} fallback: U+{(int)text[i]:X4}");
        }

        return keys;
    }

    private static bool TryMapFallbackKey(char c, out Keys key)
    {
        if (c is >= 'A' and <= 'Z')
        {
            key = Keys.A + (c - 'A');
            return true;
        }

        if (c is >= 'a' and <= 'z')
        {
            key = Keys.A + (c - 'a');
            return true;
        }

        if (c is >= '0' and <= '9')
        {
            key = Keys.D0 + (c - '0');
            return true;
        }

        if (c == ' ')
        {
            key = Keys.Space;
            return true;
        }

        key = default;
        return false;
    }
}
