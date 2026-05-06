using System;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.menu</c> RPC method. Runs on the game thread.</summary>
public static class StateMenuHandler
{
    public const string Method = "state.menu";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var menu = Game1.activeClickableMenu;
        if (menu is null)
            return ProtocolJson.ToElement(new MenuState { Present = false });

        var state = new MenuState
        {
            Type = menu.GetType().Name,
            Present = true,
        };

        // Menu-type-specific extras. Kept small for M1; extend per need.
        if (menu is ShopMenu shop)
        {
            state.Extra["currency"] = shop.currency.ToString();
            state.Extra["item_count"] = shop.forSale.Count.ToString();
        }
        else if (menu is DialogueBox dialog)
        {
            state.Extra["character"] = dialog.characterDialogue?.speaker?.Name ?? string.Empty;
        }
        AddCurrentPanelExtras(state, menu);
        AddReadableTextExtras(state, menu);

        return ProtocolJson.ToElement(state);
    }

    internal static void AddCurrentPanelExtras(MenuState state, object menu)
    {
        var field = menu.GetType().GetField("_currentPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var panel = field?.GetValue(menu);
        if (panel is null)
            return;

        state.Extra["current_panel_type"] = panel.GetType().Name;
        AddStringProperty(state, panel, "current_panel_hotkey", "Hotkey");
        AddStringProperty(state, panel, "current_panel_title", "Title");
        AddScalarProperty(state, panel, "current_panel_timeframe", "Timeframe");
    }

    private static void AddStringProperty(MenuState state, object source, string key, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetValue(source) is string value)
            state.Extra[key] = value;
    }

    private static void AddScalarProperty(MenuState state, object source, string key, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var value = property?.GetValue(source);
        if (value is null)
            return;

        var type = value.GetType();
        if (type.IsEnum || type.IsPrimitive || value is string || value is decimal)
            state.Extra[key] = value.ToString() ?? string.Empty;
    }

    internal static EventDialogueState? TryProjectDialogue(object? menu)
    {
        if (menu is null)
            return null;

        var text = ReadFirstString(menu, "dialogue", "currentDialogue", "message", "text", "question");
        var speaker = ReadNestedSpeaker(menu);
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(speaker))
            return null;

        return new EventDialogueState
        {
            MenuType = menu.GetType().Name,
            Speaker = speaker,
            Text = text,
        };
    }

    internal static void AddReadableTextExtras(MenuState state, object menu)
    {
        var projected = TryProjectDialogue(menu);
        if (projected is null)
            return;

        if (!string.IsNullOrWhiteSpace(projected.Speaker))
            state.Extra["character"] = projected.Speaker;

        if (!string.IsNullOrWhiteSpace(projected.Text))
        {
            var key = state.Type.Contains("Question", StringComparison.OrdinalIgnoreCase)
                ? "question_text"
                : state.Type.Contains("Message", StringComparison.OrdinalIgnoreCase)
                    ? "message_text"
                    : "dialogue_text";
            state.Extra[key] = projected.Text;
        }
    }

    private static string ReadFirstString(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    private static string ReadNestedSpeaker(object source)
    {
        var dialogue = ReadMember(source, "characterDialogue");
        var speaker = dialogue is null ? null : ReadMember(dialogue, "speaker");
        return speaker is null ? string.Empty : ReadFirstString(speaker, "Name", "name", "displayName");
    }

    private static object? ReadMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = source.GetType();
        return type.GetField(name, flags)?.GetValue(source)
            ?? type.GetProperty(name, flags)?.GetValue(source);
    }
}
