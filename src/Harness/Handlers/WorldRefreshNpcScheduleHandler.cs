using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.refresh_npc_schedule</c>. Rebuilds a loaded NPC's schedule
/// from current content and applies it at the current in-game time.
/// </summary>
public static class WorldRefreshNpcScheduleHandler
{
    public const string Method = "world.refresh_npc_schedule";

    private static readonly IWorldRefreshNpcScheduleWorld ProductionWorld = new SdvWorldRefreshNpcScheduleWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IWorldRefreshNpcScheduleWorld world)
    {
        var req = RpcParams.Required<RefreshNpcScheduleRequest>(paramsElement);
        var name = req.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name must be non-empty");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.refresh_npc_schedule requires a loaded world");

        var npc = world.FindNpc(name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"no NPC named: {name}");

        var scheduleKey = string.IsNullOrWhiteSpace(req.ScheduleKey) ? null : req.ScheduleKey.Trim();
        world.RefreshSchedule(npc, name, world.DayOfMonth, world.TimeOfDay, scheduleKey);
        var rawSchedule = world.GetRawSchedule(npc, name, scheduleKey);
        if (!string.IsNullOrWhiteSpace(rawSchedule)
            && SchedulePlacementParser.TryPick(rawSchedule, world.TimeOfDay, out var placement))
        {
            world.PlaceNpc(npc, name, placement);
            world.ApplyRouteDialogue(npc, name, placement);
        }

        var state = world.Project(npc, name);

        return ProtocolJson.ToElement(new RefreshNpcScheduleResult
        {
            Ok = true,
            Tick = world.Tick,
            Location = state.Location,
            Tile = new TilePoint { X = state.X, Y = state.Y },
        });
    }
}

internal interface IWorldRefreshNpcScheduleWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    int DayOfMonth { get; }
    int TimeOfDay { get; }
    object? FindNpc(string name);
    void RefreshSchedule(object npc, string name, int dayOfMonth, int timeOfDay, string? scheduleKey);
    string? GetRawSchedule(object npc, string name, string? scheduleKey);
    void PlaceNpc(object npc, string name, SchedulePlacement placement);
    void ApplyRouteDialogue(object npc, string name, SchedulePlacement placement);
    RefreshedNpcScheduleState Project(object npc, string name);
}

internal sealed record RefreshedNpcScheduleState(string? Location, int X, int Y);
internal sealed record SchedulePlacement(
    int TimeOfDay,
    string Location,
    int X,
    int Y,
    int Direction,
    string? EndBehavior,
    string? EndMessage);

internal static class SchedulePlacementParser
{
    public static bool TryPick(string rawSchedule, int timeOfDay, out SchedulePlacement placement)
    {
        placement = new SchedulePlacement(0, string.Empty, 0, 0, 0, null, null);
        var found = false;

        foreach (var rawSegment in rawSchedule.Split('/'))
        {
            var tokens = TokenizeSegment(rawSegment);
            if (tokens.Length < 5)
                continue;

            if (!int.TryParse(tokens[0], out var segmentTime)
                || segmentTime > timeOfDay
                || !int.TryParse(tokens[2], out var x)
                || !int.TryParse(tokens[3], out var y)
                || !int.TryParse(tokens[4], out var direction))
            {
                continue;
            }

            if (!found || segmentTime >= placement.TimeOfDay)
            {
                found = true;
                var endBehavior = tokens.Length >= 7 || (tokens.Length >= 6 && !LooksLikeDialogueKey(tokens[5]))
                    ? tokens[5]
                    : null;
                var endMessage = tokens.Length >= 7
                    ? tokens[6]
                    : tokens.Length >= 6 && LooksLikeDialogueKey(tokens[5])
                        ? tokens[5]
                        : null;
                placement = new SchedulePlacement(segmentTime, tokens[1], x, y, direction, endBehavior, endMessage);
            }
        }

        return found;
    }

    private static string[] TokenizeSegment(string segment)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;
        var quote = '\0';

        foreach (var ch in segment)
        {
            if (inQuote)
            {
                if (ch == quote)
                {
                    inQuote = false;
                }
                else
                {
                    current.Append(ch);
                }
                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuote = true;
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                Flush();
                continue;
            }

            current.Append(ch);
        }

        Flush();
        return tokens.ToArray();

        void Flush()
        {
            if (current.Length == 0)
                return;

            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private static bool LooksLikeDialogueKey(string value)
        => value.Contains(':', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal);
}

internal sealed class SdvWorldRefreshNpcScheduleWorld : IWorldRefreshNpcScheduleWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int DayOfMonth => Game1.dayOfMonth;
    public int TimeOfDay => Game1.timeOfDay;

    public object? FindNpc(string name)
        => Game1.getCharacterFromName(name);

    public void RefreshSchedule(object npc, string name, int dayOfMonth, int timeOfDay, string? scheduleKey)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"resolved NPC '{name}' was not an NPC");

        character.InvalidateMasterSchedule();
        character.ClearSchedule();
        if (scheduleKey is null)
        {
            character.resetForNewDay(dayOfMonth);
        }
        else if (!character.TryLoadSchedule(scheduleKey))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"NPC '{name}' could not load schedule key '{scheduleKey}'");
        }

        character.checkSchedule(timeOfDay);
    }

    public string? GetRawSchedule(object npc, string name, string? scheduleKey)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"resolved NPC '{name}' was not an NPC");

        var key = scheduleKey ?? character.ScheduleKey;
        return string.IsNullOrWhiteSpace(key) ? null : character.getMasterScheduleEntry(key);
    }

    public void PlaceNpc(object npc, string name, SchedulePlacement placement)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"resolved NPC '{name}' was not an NPC");

        if (Game1.getLocationFromName(placement.Location) is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"schedule for NPC '{name}' targets missing location '{placement.Location}'");

        Game1.warpCharacter(character, placement.Location, new Vector2(placement.X, placement.Y));
        character.faceDirection(placement.Direction);
        character.endOfRouteBehaviorName.Value = placement.EndBehavior ?? string.Empty;
        character.endOfRouteMessage.Value = placement.EndMessage ?? string.Empty;
        character.nextEndOfRouteMessage = placement.EndMessage ?? string.Empty;
        character.resetCurrentDialogue();
    }

    public void ApplyRouteDialogue(object npc, string name, SchedulePlacement placement)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"resolved NPC '{name}' was not an NPC");

        if (string.IsNullOrWhiteSpace(placement.EndMessage))
            return;

        character.setNewDialogue(Dialogue.FromTranslation(character, placement.EndMessage), add: false, clearOnMovement: false);
        if (character.currentLocation is { } location)
            character.checkForMarriageDialogue(Game1.timeOfDay, location);
    }

    public RefreshedNpcScheduleState Project(object npc, string name)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"resolved NPC '{name}' was not an NPC");

        var tile = character.TilePoint;
        return new RefreshedNpcScheduleState(character.currentLocation?.Name, tile.X, tile.Y);
    }
}
