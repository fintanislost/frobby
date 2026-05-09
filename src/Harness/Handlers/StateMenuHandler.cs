using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            Bounds = new MenuBounds
            {
                X = menu.xPositionOnScreen,
                Y = menu.yPositionOnScreen,
                Width = menu.width,
                Height = menu.height,
            },
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
        AddDiagnosticMemberExtras(state, menu);

        return ProtocolJson.ToElement(state);
    }

    private static void AddDiagnosticMemberExtras(MenuState state, object menu)
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("SDV_TEST_DIAGNOSTIC_MENU_MEMBERS"),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = menu.GetType();
        state.Extra["diagnostic_choice_sources"] = BuildDiagnosticChoiceSourceSummary(menu);
        state.Extra["diagnostic_menu_state"] = string.Join(
            ",",
            new[]
            {
                $"selectedResponse={FormatDiagnosticValue(ReadMember(menu, "selectedResponse"))}",
                $"_showedOptions={FormatDiagnosticValue(ReadMember(menu, "_showedOptions"))}",
                $"isQuestion={FormatDiagnosticValue(ReadMember(menu, "isQuestion"))}",
            });
        state.Extra["diagnostic_member_names"] = string.Join(
            "|",
            type.GetFields(flags).Select(f => f.Name)
                .Concat(type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0).Select(p => p.Name))
                .OrderBy(n => n, StringComparer.Ordinal)
                .Take(120));
        AddDiagnosticValueExtra(state, menu, "menu", "dialogue");
        AddDiagnosticValueExtra(state, menu, "menu", "question");
        AddDiagnosticValueExtra(state, menu, "menu", "message");
        AddDiagnosticValueExtra(state, menu, "menu", "text");
        AddDiagnosticEnumerableExtra(state, menu, "dialogues");
        AddDiagnosticEnumerableExtra(state, menu, "responses");
        AddDiagnosticEnumerableExtra(state, menu, "responseCC");
    }

    private static string BuildDiagnosticChoiceSourceSummary(object menu)
    {
        var parts = new List<string>();
        AddDiagnosticSourceParts(parts, menu, "responses", "responseKey", "responseText");
        AddDiagnosticSourceParts(parts, menu, "responseCC", "name", "label", "hoverText", "myID", "bounds");
        AddDiagnosticSourceParts(parts, menu, "questionChoices");
        AddDiagnosticSourceParts(parts, menu, "dialogues");
        return string.Join("; ", parts);
    }

    private static void AddDiagnosticSourceParts(List<string> parts, object source, string memberName, params string[] valueNames)
    {
        if (ReadMember(source, memberName) is not IEnumerable enumerable || enumerable is string)
            return;

        var values = new List<string>();
        var index = 0;
        foreach (var item in enumerable)
        {
            if (index >= 4)
                break;
            if (item is null)
            {
                values.Add($"{index}:<null>");
            }
            else if (valueNames.Length == 0)
            {
                values.Add($"{index}:{item.GetType().Name}='{item}'");
            }
            else
            {
                var itemValues = valueNames
                    .Select(name => $"{name}={FormatDiagnosticValue(ReadMember(item, name))}")
                    .ToArray();
                values.Add($"{index}:{item.GetType().Name}({string.Join(",", itemValues)})");
            }
            index++;
        }

        parts.Add($"{memberName}[{index}]={string.Join("|", values)}");
    }

    private static string FormatDiagnosticValue(object? value)
        => value is null ? "<null>" : $"{value.GetType().Name}:'{value}'";

    private static void AddDiagnosticEnumerableExtra(MenuState state, object menu, string memberName)
    {
        if (ReadMember(menu, memberName) is not IEnumerable enumerable || enumerable is string)
            return;

        var items = enumerable.Cast<object?>().Where(item => item is not null).Take(3).ToList();
        state.Extra[$"diagnostic_{memberName}_count"] = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (items.FirstOrDefault() is not { } first)
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = first.GetType();
        state.Extra[$"diagnostic_{memberName}_item_type"] = type.FullName ?? type.Name;
        state.Extra[$"diagnostic_{memberName}_item_string"] = first.ToString() ?? string.Empty;
        state.Extra[$"diagnostic_{memberName}_item_members"] = string.Join(
            "|",
            type.GetFields(flags).Select(f => f.Name)
                .Concat(type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0).Select(p => p.Name))
                .OrderBy(n => n, StringComparer.Ordinal)
                .Take(80));
        foreach (var diagnosticValueName in new[]
        {
            "name", "label", "hoverText", "myID", "responseKey", "responseText",
        })
        {
            AddDiagnosticValueExtra(state, first, memberName, diagnosticValueName);
        }
    }

    private static void AddDiagnosticValueExtra(MenuState state, object source, string memberName, string valueName)
    {
        var value = ReadMember(source, valueName);
        if (value is null)
            return;

        state.Extra[$"diagnostic_{memberName}_{valueName}_type"] = value.GetType().FullName ?? value.GetType().Name;
        state.Extra[$"diagnostic_{memberName}_{valueName}_string"] = value.ToString() ?? string.Empty;
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

        var dialogue = ReadMember(menu, "characterDialogue");
        var text = ReadFirstString(menu, "dialogue", "currentDialogue", "message", "text", "question");
        if (string.IsNullOrWhiteSpace(text) && dialogue is not null)
            text = ReadDialogueText(dialogue);

        var speaker = ReadNestedSpeaker(menu);
        var choices = ReadChoiceSummaries(menu);
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(speaker) && choices.Count == 0)
            return null;

        return new EventDialogueState
        {
            MenuType = menu.GetType().Name,
            Speaker = speaker,
            Text = text,
            Choices = choices,
        };
    }

    internal static void AddReadableTextExtras(MenuState state, object menu)
    {
        var projected = TryProjectDialogue(menu);
        if (projected is null)
            return;

        state.Choices = projected.Choices
            .Select(c => new MenuChoiceState { Key = c.Key, Text = c.Text })
            .ToList();
        if (state.Choices.Count > 0)
            state.Extra["choice_count"] = state.Choices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            AddDialogueProgressExtras(state, menu, projected.Text);
        }
    }

    private static void AddDialogueProgressExtras(MenuState state, object menu, string text)
    {
        var index = ReadFirstInt(menu, "characterIndexInDialogue", "currentDialogueCharacterIndex");
        if (index is null)
            return;

        var length = text.Length;
        var safetyTimer = ReadFirstInt(menu, "safetyTimer");
        var ready = length == 0 || index.Value >= length - 1;
        if (safetyTimer is > 0)
            ready = false;

        state.Extra["dialogue_character_index"] = index.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        state.Extra["dialogue_text_length"] = length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        state.Extra["dialogue_ready"] = ready ? "true" : "false";
        if (safetyTimer is not null)
            state.Extra["dialogue_safety_timer"] = safetyTimer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

    private static string ReadNestedSpeaker(object source)
    {
        var dialogue = ReadMember(source, "characterDialogue");
        var speaker = dialogue is null ? null : ReadMember(dialogue, "speaker");
        return speaker is null ? string.Empty : ReadFirstString(speaker, "Name", "name", "displayName");
    }

    private static string ReadDialogueText(object dialogue)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var method = dialogue.GetType().GetMethod("getCurrentDialogue", flags, Type.EmptyTypes);
        return method?.Invoke(dialogue, null) as string ?? string.Empty;
    }

    private static List<MenuChoiceState> ReadChoiceSummaries(object menu)
    {
        var result = new List<MenuChoiceState>();
        foreach (var memberName in new[] { "responses", "Responses", "answers", "Answers", "questionChoices", "QuestionChoices" })
        {
            if (ReadMember(menu, memberName) is not IEnumerable enumerable || enumerable is string)
                continue;

            foreach (var item in enumerable)
            {
                if (item is null)
                    continue;

                var text = item is string itemText
                    ? itemText
                    : ReadFirstString(item, "responseText", "ResponseText", "text", "Text", "label", "Label");
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var key = item is string
                    ? text
                    : ReadFirstString(item, "responseKey", "ResponseKey", "key", "Key", "id", "Id");
                result.Add(new MenuChoiceState { Key = key, Text = text });
            }

            if (result.Count > 0)
                return result;
        }

        return result;
    }

    private static object? ReadMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = source.GetType();
        return type.GetField(name, flags)?.GetValue(source)
            ?? type.GetProperty(name, flags)?.GetValue(source);
    }
}
