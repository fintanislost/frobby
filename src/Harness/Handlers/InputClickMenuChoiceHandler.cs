using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>input.click_menu_choice</c>. Clicks a response option in the active
/// menu by matching reflected Stardew response text/key to its paired clickable bounds.
/// </summary>
public static class InputClickMenuChoiceHandler
{
    public const string Method = "input.click_menu_choice";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, () => Game1.activeClickableMenu, () => Game1.ticks);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick)
    {
        var req = RpcParams.Required<InputClickMenuChoiceRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.Key)
            && string.IsNullOrWhiteSpace(req.Text)
            && string.IsNullOrWhiteSpace(req.TextEquals)
            && string.IsNullOrWhiteSpace(req.TextMatches))
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.key, params.text, params.text_equals, or params.text_matches required");
        }

        var button = string.IsNullOrWhiteSpace(req.Button) ? "left" : req.Button.Trim().ToLowerInvariant();
        if (button != "left" && button != "right")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.button must be left or right");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} requires an active menu");

        var match = FindChoice(menu, req);
        if (match is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} could not find menu choice: {ChoiceLabel(req)}");

        var bounds = match.Value;
        var x = bounds.X + bounds.Width / 2;
        var y = bounds.Y + bounds.Height / 2;
        menu.performHoverAction(x, y);
        if (button == "left")
            menu.receiveLeftClick(x, y);
        else
            menu.receiveRightClick(x, y);

        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }

    private static Rectangle? FindChoice(IClickableMenu menu, InputClickMenuChoiceRequest req)
    {
        var responses = ReadChoiceResponses(menu);
        var components = ReadClickableComponents(menu);
        var count = Math.Min(responses.Count, components.Count);
        for (var i = 0; i < count; i++)
        {
            if (ChoiceMatches(responses[i], req))
                return components[i].bounds;
        }

        return null;
    }

    private static List<ChoiceResponse> ReadChoiceResponses(object menu)
    {
        var result = new List<ChoiceResponse>();
        foreach (var memberName in new[] { "responses", "Responses", "answers", "Answers", "questionChoices", "QuestionChoices" })
        {
            if (ReadMember(menu, memberName) is not IEnumerable enumerable || enumerable is string)
                continue;

            foreach (var item in enumerable)
            {
                if (item is null)
                    continue;
                result.Add(new ChoiceResponse(
                    item is string itemText
                        ? itemText
                        : ReadFirstString(item, "responseKey", "ResponseKey", "key", "Key", "id", "Id"),
                    item is string itemTextAgain
                        ? itemTextAgain
                        : ReadFirstString(item, "responseText", "ResponseText", "text", "Text", "label", "Label")));
            }

            if (result.Count > 0)
                return result;
        }

        return result;
    }

    private static List<ClickableComponent> ReadClickableComponents(object menu)
    {
        var result = new List<ClickableComponent>();
        foreach (var memberName in new[] { "responseCC", "ResponseCC", "choices", "Choices" })
        {
            if (ReadMember(menu, memberName) is not IEnumerable enumerable || enumerable is string)
                continue;

            foreach (var item in enumerable)
            {
                if (item is ClickableComponent component)
                    result.Add(component);
            }

            if (result.Count > 0)
                return result;
        }

        return result;
    }

    private static bool ChoiceMatches(ChoiceResponse response, InputClickMenuChoiceRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.Key)
            && string.Equals(response.Key, req.Key, StringComparison.Ordinal))
        {
            return true;
        }

        var comparison = req.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.IsNullOrWhiteSpace(req.TextEquals))
            return string.Equals(response.Text, req.TextEquals, comparison);
        if (!string.IsNullOrWhiteSpace(req.Text))
            return response.Text.Contains(req.Text, comparison);
        if (!string.IsNullOrWhiteSpace(req.TextMatches))
            return Regex.IsMatch(response.Text, req.TextMatches, req.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

        return false;
    }

    private static string ChoiceLabel(InputClickMenuChoiceRequest req)
        => req.Key ?? req.TextEquals ?? req.Text ?? req.TextMatches ?? string.Empty;

    private static string ReadFirstString(object source, params string[] names)
    {
        foreach (var name in names)
        {
            if (ReadMember(source, name) is string value && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static object? ReadMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = source.GetType();
        return type.GetField(name, flags)?.GetValue(source)
            ?? type.GetProperty(name, flags)?.GetValue(source);
    }

    private readonly record struct ChoiceResponse(string Key, string Text);
}
