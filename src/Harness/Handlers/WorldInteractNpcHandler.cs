using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>world.interact_npc</c>. Triggers an NPC interaction by directly invoking
/// <see cref="NPC.checkAction"/> — the same call SDV makes when the player presses action
/// while facing an NPC at conversation distance. The NPC must be present in the player's
/// current location; otherwise the handler returns <c>GameStateInvalid</c> rather than
/// silently warping (test authors should warp explicitly first).
/// </summary>
public static class WorldInteractNpcHandler
{
    public const string Method = "world.interact_npc";

    private static readonly IWorldInteractNpcWorld ProductionWorld = new SdvWorldInteractNpcWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IWorldInteractNpcWorld world)
    {
        var req = RpcParams.Required<WorldInteractNpcRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "name is required");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save — mutation requires a loaded world");

        var npc = world.FindNpcInCurrentLocation(req.Name)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"NPC '{req.Name}' not found in current location '{world.CurrentLocationName}'");

        var canTalk = world.CanTalk(npc);
        if (canTalk)
            world.PrepareDialogue(npc);

        // Return value is intentionally ignored — some interactions return false even
        // when they successfully triggered something (e.g. dialogue that routes through
        // a different code path than checkAction's boolean contract implies).
        world.CheckAction(npc);
        if (canTalk && (!world.HasActiveMenu || world.HasEmptyDialogueMenu))
            world.DrawDialogue(npc);

        return ProtocolJson.ToElement(new MutatorOk { Tick = world.Tick });
    }
}

internal interface IWorldInteractNpcWorld
{
    int Tick { get; }
    bool IsWorldReady { get; }
    string CurrentLocationName { get; }
    bool HasActiveMenu { get; }
    bool HasEmptyDialogueMenu { get; }
    object? FindNpcInCurrentLocation(string name);
    void PrepareDialogue(object npc);
    void CheckAction(object npc);
    bool CanTalk(object npc);
    void DrawDialogue(object npc);
}

internal sealed class SdvWorldInteractNpcWorld : IWorldInteractNpcWorld
{
    public int Tick => Game1.ticks;

    public bool IsWorldReady
        => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;

    public string CurrentLocationName => Game1.currentLocation?.Name ?? string.Empty;

    public bool HasActiveMenu => Game1.activeClickableMenu is not null;

    public bool HasEmptyDialogueMenu
    {
        get
        {
            if (Game1.activeClickableMenu is not StardewValley.Menus.DialogueBox dialog)
                return false;

            var projected = StateMenuHandler.TryProjectDialogue(dialog);
            return projected is null || string.IsNullOrWhiteSpace(projected.Text);
        }
    }

    public object? FindNpcInCurrentLocation(string name)
        => Game1.currentLocation?.characters?.FirstOrDefault(c => c?.Name == name);

    public void CheckAction(object npc)
    {
        var location = Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldInteractNpcHandler.Method} requires a current location");

        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "resolved character was not an NPC");

        character.checkAction(Game1.player, location);
    }

    public void PrepareDialogue(object npc)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "resolved character was not an NPC");

        if (character.CurrentDialogue.Count > 0)
            return;

        var heartLevel = Game1.player?.getFriendshipHeartLevelForNPC(character.Name) ?? 0;
        character.checkForNewCurrentDialogue(heartLevel, noPreface: false);
        if (character.CurrentDialogue.Count > 0)
            return;

        var introduction = character.TryGetDialogue("Introduction");
        if (introduction is not null)
            character.setNewDialogue(introduction, add: false, clearOnMovement: false);
    }

    public bool CanTalk(object npc)
        => npc is NPC character && character.canTalk();

    public void DrawDialogue(object npc)
    {
        if (npc is not NPC character)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "resolved character was not an NPC");

        if (character.CurrentDialogue.Count > 0)
        {
            Game1.activeClickableMenu = null;
            Game1.DrawDialogue(character.CurrentDialogue.Peek());
            return;
        }

        Game1.drawDialogue(character);
    }
}
