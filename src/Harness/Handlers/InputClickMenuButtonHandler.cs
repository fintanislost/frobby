using System;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>input.click_menu_button</c>. Clicks a reflected button region on
/// custom menus whose panels keep stable button bounds in fields.
/// </summary>
public static class InputClickMenuButtonHandler
{
    public const string Method = "input.click_menu_button";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, () => Game1.activeClickableMenu, () => Game1.ticks);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick)
    {
        var req = RpcParams.Required<InputClickMenuButtonRequest>(paramsElement);
        var label = LabelTarget(req);
        if (string.IsNullOrWhiteSpace(req.Id) && string.IsNullOrWhiteSpace(label))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.id or params.label required");

        var button = string.IsNullOrWhiteSpace(req.Button) ? "left" : req.Button.Trim().ToLowerInvariant();
        if (button != "left" && button != "right")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.button must be left or right");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires an active menu");

        var panel = GetCurrentPanel(menu)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} could not find active menu current panel");

        var match = FindButtonRegion(panel, req);
        if (match is null)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"{Method} could not find menu button: {req.Id ?? label}");

        var bounds = match.Value.Bounds;
        var x = bounds.X + bounds.Width / 2;
        var y = bounds.Y + bounds.Height / 2;

        if (button == "left")
            menu.receiveLeftClick(x, y);
        else
            menu.receiveRightClick(x, y);

        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }

    private static object? GetCurrentPanel(IClickableMenu menu)
    {
        var field = menu.GetType().GetField(
            "_currentPanel",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(menu);
    }

    private static ButtonRegion? FindButtonRegion(object source, InputClickMenuButtonRequest req)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = source.GetType();

        foreach (var field in type.GetFields(flags))
        {
            if (TryReadButtonRegion(field.GetValue(source), out var region) && Matches(region, req))
                return region;
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;
            if (TryReadButtonRegion(property.GetValue(source), out var region) && Matches(region, req))
                return region;
        }

        return null;
    }

    private static bool TryReadButtonRegion(object? value, out ButtonRegion region)
    {
        region = default;
        if (value is null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = value.GetType();
        var id = type.GetProperty("Id", flags)?.GetValue(value) as string;
        var label = type.GetProperty("Label", flags)?.GetValue(value) as string;
        var boundsValue = type.GetProperty("Bounds", flags)?.GetValue(value);

        if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(label))
            return false;
        if (boundsValue is not Rectangle bounds)
            return false;

        region = new ButtonRegion(id, label, bounds);
        return true;
    }

    private static bool Matches(ButtonRegion region, InputClickMenuButtonRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.Id)
            && string.Equals(region.Id, req.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var label = LabelTarget(req);
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(region.Label))
            return false;

        var comparison = req.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(region.Label, label, comparison);
    }

    private static string? LabelTarget(InputClickMenuButtonRequest req)
        => string.IsNullOrWhiteSpace(req.Label) ? req.TextEquals : req.Label;

    private readonly record struct ButtonRegion(string? Id, string? Label, Rectangle Bounds);
}
