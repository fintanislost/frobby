using System;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>input.click_menu_advance</c>. Clicks a known advance/OK control on the
/// active menu when one is discoverable, otherwise falls back to the menu center.
/// </summary>
public static class InputClickMenuAdvanceHandler
{
    public const string Method = "input.click_menu_advance";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, () => Game1.activeClickableMenu, () => Game1.ticks);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        Func<IClickableMenu?> getActiveMenu,
        Func<int> getTick)
    {
        var req = RpcParams.Optional<InputClickMenuAdvanceRequest>(paramsElement);
        var button = string.IsNullOrWhiteSpace(req.Button) ? "left" : req.Button.Trim().ToLowerInvariant();
        if (button != "left" && button != "right")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.button must be left or right");

        var menu = getActiveMenu()
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires an active menu");

        var foundAdvanceButton = FindAdvanceBounds(menu);
        var target = foundAdvanceButton ?? MenuAdvanceFallback(menu);
        var x = target.X + target.Width / 2;
        var y = target.Y + target.Height / 2;
        if (button == "left")
            menu.receiveLeftClick(x, y);
        else
            menu.receiveRightClick(x, y);
        if (foundAdvanceButton is null)
        {
            foreach (var key in FallbackAdvanceKeys)
                menu.receiveKeyPress(key);
        }

        return ProtocolJson.ToElement(new MutatorOk { Tick = getTick() });
    }

    private static Rectangle? FindAdvanceBounds(object menu)
    {
        foreach (var memberName in AdvanceMemberNames)
        {
            if (ReadMember(menu, memberName) is ClickableComponent component)
                return component.bounds;
        }

        return null;
    }

    private static Rectangle MenuAdvanceFallback(IClickableMenu menu)
    {
        var width = ReadFirstInt(menu, "dialogueWidth", "DialogueWidth") ?? menu.width;
        var x = menu.xPositionOnScreen + Math.Max(0, width - 80);
        var y = menu.yPositionOnScreen + Math.Max(0, menu.height - 80);
        return new Rectangle(x, y, 40, 40);
    }

    private static int? ReadFirstInt(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is int parsed)
                return parsed;
        }

        return null;
    }

    private static object? ReadMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = source.GetType();
        return type.GetField(name, flags)?.GetValue(source)
            ?? type.GetProperty(name, flags)?.GetValue(source);
    }

    private static readonly string[] AdvanceMemberNames =
    {
        "nextDialogueButton",
        "NextDialogueButton",
        "nextButton",
        "NextButton",
        "okButton",
        "OkButton",
        "doneButton",
        "DoneButton",
    };

    private static readonly Keys[] FallbackAdvanceKeys =
    {
        Keys.X,
        Keys.Enter,
        Keys.Space,
    };
}
