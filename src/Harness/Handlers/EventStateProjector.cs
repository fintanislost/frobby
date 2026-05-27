using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal sealed class EventProjectionSource
{
    public object? CurrentEvent { get; init; }
    public object? LocationEvent { get; init; }
    public bool EventUp { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public Rectangle Viewport { get; init; }
    public object? ActiveMenu { get; init; }
    public IEnumerable<object?> AdditionalActors { get; init; } = Array.Empty<object?>();
}

internal static class EventStateProjector
{
    public static EventState ToState(EventProjectionSource source)
    {
        var ev = source.CurrentEvent ?? source.LocationEvent;
        var active = ev is not null || source.EventUp;
        var state = new EventState
        {
            Active = active,
            EventUp = source.EventUp,
            Location = active ? source.LocationName : string.Empty,
            Id = ev is null ? string.Empty : ReadString(ev, "id", "eventId", "EventId", "ID"),
            IsFestival = ev is not null && ReadBool(ev, "isFestival", "IsFestival"),
            IsSkippable = ev is not null && ReadBool(ev, "skippable", "Skippable", "isSkippable", "IsSkippable"),
            PlayerControlLocked = active,
            Viewport = active
                ? new EventViewportState
                {
                    X = source.Viewport.X,
                    Y = source.Viewport.Y,
                    Width = source.Viewport.Width,
                    Height = source.Viewport.Height,
                }
                : null,
            Dialogue = StateMenuHandler.TryProjectDialogue(source.ActiveMenu),
        };
        if (state.Dialogue is not null)
            state.Choices = state.Dialogue.Choices;

        if (!active)
            return state;

        foreach (var actor in ReadActors(ev).Concat(source.AdditionalActors).Where(a => a is not null))
        {
            var projected = ProjectActor(actor!);
            if (!string.IsNullOrWhiteSpace(projected.Name)
                && state.Actors.All(a => !string.Equals(a.Name, projected.Name, StringComparison.Ordinal)))
            {
                state.Actors.Add(projected);
            }
        }

        return state;
    }

    private static IEnumerable<object?> ReadActors(object? ev)
    {
        if (ev is null)
            yield break;

        foreach (var name in new[] { "actors", "Actors", "characters", "Characters", "festivalActors" })
        {
            var value = ReadMember(ev, name);
            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                    yield return item;
            }
        }
    }

    private static EventActorState ProjectActor(object actor)
    {
        var tile = ReadPoint(actor, "TilePoint", "Tile", "tilePoint", "tile");
        var pixel = ReadVector(actor, "Position", "position");
        var sprite = ReadMember(actor, "Sprite") ?? ReadMember(actor, "sprite");
        var dialogue = ProjectActorDialogue(actor);
        return new EventActorState
        {
            Name = ReadString(actor, "Name", "name", "displayName", "DisplayName"),
            Tile = new TilePoint { X = tile.X, Y = tile.Y },
            Pixel = new PixelPoint { X = (int)pixel.X, Y = (int)pixel.Y },
            FacingDirection = ReadInt(actor, "FacingDirection", "facingDirection", "FacingDirectionValue"),
            CurrentFrame = sprite is null
                ? ReadInt(actor, "CurrentFrame", "currentFrame")
                : ReadInt(sprite, "CurrentFrame", "currentFrame"),
            DialogueKey = dialogue.Key,
            DialogueText = dialogue.Text,
            DialogueCount = dialogue.Count,
        };
    }

    private static (string Key, string Text, int Count) ProjectActorDialogue(object actor)
    {
        var currentDialogue = ReadMember(actor, "CurrentDialogue") ?? ReadMember(actor, "currentDialogue");
        if (currentDialogue is not IEnumerable enumerable || currentDialogue is string)
            return (string.Empty, string.Empty, 0);

        object? first = null;
        var count = 0;
        foreach (var item in enumerable)
        {
            if (item is null)
                continue;

            first ??= item;
            count++;
        }

        if (first is null)
            return (string.Empty, string.Empty, 0);

        return (
            ReadString(first, "dialogueKey", "DialogueKey", "key", "Key"),
            ReadDialogueText(first),
            count);
    }

    private static string ReadDialogueText(object dialogue)
    {
        foreach (var name in new[] { "Text", "text", "currentDialogue", "CurrentDialogue", "dialogue", "Dialogue" })
        {
            var value = ReadMember(dialogue, name);
            if (value is string s)
                return s;
            if (value is not null && (value.GetType().IsPrimitive || value.GetType().IsEnum))
                return value.ToString() ?? string.Empty;
        }

        return InvokeStringMethod(dialogue, "getCurrentDialogue", "GetCurrentDialogue");
    }

    private static string InvokeStringMethod(object source, params string[] names)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = source.GetType();
        foreach (var name in names)
        {
            var method = type.GetMethod(name, flags, binder: null, Type.EmptyTypes, modifiers: null);
            if (method?.Invoke(source, Array.Empty<object>()) is string value)
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

    private static string ReadString(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is string s)
                return s;
            if (value is not null && (value.GetType().IsPrimitive || value.GetType().IsEnum))
                return value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool ReadBool(object source, params string[] names)
    {
        foreach (var name in names)
            if (ReadMember(source, name) is bool value)
                return value;
        return false;
    }

    private static int ReadInt(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is int i)
                return i;
            if (value is short s)
                return s;
        }

        return 0;
    }

    private static Point ReadPoint(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(source, name);
            if (value is Point p)
                return p;
            if (value is Vector2 v)
                return new Point((int)v.X, (int)v.Y);
        }

        return Point.Zero;
    }

    private static Vector2 ReadVector(object source, params string[] names)
    {
        foreach (var name in names)
            if (ReadMember(source, name) is Vector2 v)
                return v;
        return Vector2.Zero;
    }
}
