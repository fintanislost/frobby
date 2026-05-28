using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Reflection;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>input.click_tile</c>. Sends a left-click to a gameplay tile.</summary>
public static class InputClickTileHandler
{
    public const string Method = "input.click_tile";

    private const int TileSize = 64;
    private const int MaxActionSearchRadius = 25;
    private static readonly IInputTileClickWorld ProductionWorld = new SdvInputTileClickWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IInputTileClickWorld world)
    {
        var req = RpcParams.Required<InputClickTileRequest>(paramsElement);
        var button = NormalizeButton(req);
        var tileX = req.X!.Value;
        var tileY = req.Y!.Value;

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires a loaded world");
        if (world.HasActiveMenu)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires no active menu");
        if (world.IsWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires !Game1.isWarping");
        if (world.IsFading)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires no active fade");
        if (world.EventUp && !req.AllowEventInput)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "input.click_tile requires !Game1.eventUp");

        if (req.RequireCurrentLocation
            && !string.IsNullOrWhiteSpace(req.Location)
            && !string.Equals(req.Location, world.CurrentLocationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"input.click_tile location guard expected {req.Location}, current location is {world.CurrentLocationName}");
        }

        (tileX, tileY) = ResolveTargetTile(req, world);

        if ((world.MapWidth is { } width && tileX >= width)
            || (world.MapHeight is { } height && tileY >= height))
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"input.click_tile target tile ({tileX},{tileY}) is outside map bounds");
        }

        var worldX = tileX * TileSize + req.ScreenOffsetX;
        var worldY = tileY * TileSize + req.ScreenOffsetY;
        var screenX = worldX - world.ViewportX;
        var screenY = worldY - world.ViewportY;
        var targetNpcName = button == "right" ? world.FindNpcAtTile(tileX, tileY) : null;
        var selectedItem = world.SelectedItem;
        var handled = button == "right"
            ? world.ClickRightTile(worldX, worldY, screenX, screenY)
            : world.ClickLeftTile(worldX, worldY, screenX, screenY);
        var npcFallbackUsed = false;
        var hasNonTargetDialogue = HasNonTargetDialogue(button, targetNpcName, world);
        if (ShouldUseNpcFallback(button, targetNpcName, handled, world, selectedItem))
        {
            if (hasNonTargetDialogue)
                world.ClearActiveMenu();
            npcFallbackUsed = world.InteractNpcAtTile(tileX, tileY);
            handled = handled || npcFallbackUsed;
        }

        return ProtocolJson.ToElement(new InputClickTileResult
        {
            Ok = true,
            Tick = world.Tick,
            Location = world.CurrentLocationName,
            Tile = new TilePoint { X = tileX, Y = tileY },
            Screen = new PixelPoint { X = screenX, Y = screenY },
            World = new PixelPoint { X = worldX, Y = worldY },
            SelectedItem = selectedItem is { } selected
                ? PlayerSelectItemHandler.ToSummary(selected)
                : null,
            Handled = handled,
            TargetNpcName = targetNpcName,
            NpcFallbackUsed = npcFallbackUsed,
        });
    }

    private static (int X, int Y) ResolveTargetTile(InputClickTileRequest req, IInputTileClickWorld world)
    {
        var centerX = req.X!.Value;
        var centerY = req.Y!.Value;
        if (string.IsNullOrWhiteSpace(req.ActionValue))
            return (centerX, centerY);

        if (req.Radius < 0 || req.Radius > MaxActionSearchRadius)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.radius must be between 0 and {MaxActionSearchRadius}");

        var layers = WorldInteractTileActionHandler.ResolveLayers(req.Layers, world.LayerNames);
        var properties = TileActionPropertyNames.Resolve(req.Properties, "properties");
        var matches = new List<TileActionCandidate>();
        for (var y = centerY - req.Radius; y <= centerY + req.Radius; y++)
        {
            if (y < 0)
                continue;

            for (var x = centerX - req.Radius; x <= centerX + req.Radius; x++)
            {
                if (x < 0)
                    continue;

                foreach (var property in properties)
                foreach (var layer in layers)
                {
                    var value = world.GetTileProperty(x, y, layer, property);
                    if (value is null || !string.Equals(value, req.ActionValue, StringComparison.Ordinal))
                        continue;

                    matches.Add(new TileActionCandidate
                    {
                        Tile = new TilePoint { X = x, Y = y },
                        Layer = layer,
                        Property = property,
                        Value = value,
                        Distance = Math.Abs(x - centerX) + Math.Abs(y - centerY),
                    });
                }
            }
        }

        var match = matches
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Tile.Y)
            .ThenBy(candidate => candidate.Tile.X)
            .FirstOrDefault();
        if (match is null)
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"input.click_tile could not find action_value '{req.ActionValue}' within radius {req.Radius} of tile {centerX},{centerY}");
        }

        return (match.Tile.X, match.Tile.Y);
    }

    private static bool ShouldUseNpcFallback(
        string button,
        string? targetNpcName,
        bool handled,
        IInputTileClickWorld world,
        ISelectableInventoryItem? selectedItem)
    {
        if (button != "right" || targetNpcName is null)
            return false;

        var hasNonTargetDialogue = HasNonTargetDialogue(button, targetNpcName, world);

        if (IsSelectedInventoryObject(selectedItem))
            return hasNonTargetDialogue;

        return !handled || !world.HasActiveMenu || world.HasBlankDialogueMenu || hasNonTargetDialogue;
    }

    private static bool HasNonTargetDialogue(string button, string? targetNpcName, IInputTileClickWorld world)
        => button == "right"
            && targetNpcName is not null
            && world.HasDialogueMenu
            && !string.Equals(world.ActiveDialogueCharacterName, targetNpcName, StringComparison.Ordinal);

    private static bool IsSelectedInventoryObject(ISelectableInventoryItem? selectedItem)
    {
        if (selectedItem is null)
            return false;

        return string.Equals(selectedItem.RuntimeType, "Object", StringComparison.Ordinal)
            || selectedItem.QualifiedId.StartsWith("(O)", StringComparison.Ordinal);
    }

    private static string NormalizeButton(InputClickTileRequest req)
    {
        if (req.X is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x required");
        if (req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y required");
        if (req.X.Value < 0 || req.Y.Value < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.x and params.y must be non-negative");
        if (!string.IsNullOrWhiteSpace(req.ActionValue)
            && (req.Radius < 0 || req.Radius > MaxActionSearchRadius))
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"params.radius must be between 0 and {MaxActionSearchRadius}");
        }
        if (req.ScreenOffsetX is < 0 or >= TileSize)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.screen_offset_x must be between 0 and 63");
        if (req.ScreenOffsetY is < 0 or >= TileSize)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.screen_offset_y must be between 0 and 63");

        var button = string.IsNullOrWhiteSpace(req.Button)
            ? "left"
            : req.Button.Trim().ToLowerInvariant();
        if (button is not ("left" or "right"))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "params.button must be left or right for input.click_tile");

        return button;
    }
}

internal interface IInputTileClickWorld
{
    bool IsWorldReady { get; }
    bool HasActiveMenu { get; }
    bool IsWarping { get; }
    bool IsFading { get; }
    bool EventUp { get; }
    int Tick { get; }
    string CurrentLocationName { get; }
    int? MapWidth { get; }
    int? MapHeight { get; }
    int ViewportX { get; }
    int ViewportY { get; }
    IReadOnlyList<string> LayerNames { get; }
    ISelectableInventoryItem? SelectedItem { get; }
    bool ClickLeftTile(int worldX, int worldY, int screenX, int screenY);
    bool ClickRightTile(int worldX, int worldY, int screenX, int screenY);
    string? GetTileProperty(int x, int y, string layer, string property);
    string? FindNpcAtTile(int tileX, int tileY);
    bool HasBlankDialogueMenu { get; }
    bool HasDialogueMenu { get; }
    string? ActiveDialogueCharacterName { get; }
    void ClearActiveMenu();
    bool InteractNpcAtTile(int tileX, int tileY);
}

internal sealed class SdvInputTileClickWorld : IInputTileClickWorld
{
    private const int TileSize = 64;

    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public bool HasActiveMenu => Game1.activeClickableMenu is not null;
    public bool IsWarping => Game1.isWarping;
    public bool IsFading => GameFadeState.IsFading;
    public bool EventUp => Game1.eventUp;
    public int Tick => Game1.ticks;
    public string CurrentLocationName => CurrentLocation.NameOrUniqueName ?? CurrentLocation.Name ?? string.Empty;
    public int? MapWidth => CurrentLocation.Map?.DisplayWidth / TileSize;
    public int? MapHeight => CurrentLocation.Map?.DisplayHeight / TileSize;
    public int ViewportX => Game1.viewport.X;
    public int ViewportY => Game1.viewport.Y;
    public IReadOnlyList<string> LayerNames
        => CurrentLocation.Map?.Layers.Select(layer => layer.Id).ToList() ?? new List<string>();
    public bool HasBlankDialogueMenu
    {
        get
        {
            if (Game1.activeClickableMenu is not DialogueBox dialog)
                return false;

            var projected = StateMenuHandler.TryProjectDialogue(dialog);
            return projected is null || string.IsNullOrWhiteSpace(projected.Text);
        }
    }
    public bool HasDialogueMenu => Game1.activeClickableMenu is DialogueBox;

    public string? ActiveDialogueCharacterName
    {
        get
        {
            if (Game1.activeClickableMenu is not DialogueBox dialog)
                return null;

            return dialog.characterDialogue?.speaker?.Name ?? string.Empty;
        }
    }

    public void ClearActiveMenu()
        => Game1.activeClickableMenu = null;

    public ISelectableInventoryItem? SelectedItem
    {
        get
        {
            var player = Game1.player;
            if (player is null)
                return null;
            var slot = player.CurrentToolIndex;
            if (slot < 0 || slot >= player.Items.Count || player.Items[slot] is not Item item)
                return null;

            var qualifiedId = item.QualifiedItemId ?? item.ItemId ?? string.Empty;
            var itemId = item.ItemId ?? SdvPlayerStateWorld.StripQualifiedPrefix(qualifiedId);
            return new SelectableInventoryItem(
                slot,
                qualifiedId,
                itemId,
                item.DisplayName ?? item.Name ?? string.Empty,
                item.Stack,
                item.Category,
                item.Quality,
                item.GetType().Name);
        }
    }

    public bool ClickLeftTile(int worldX, int worldY, int screenX, int screenY)
    {
        PrimeCursor(worldX, worldY, screenX, screenY);
        return Game1.pressUseToolButton();
    }

    public bool ClickRightTile(int worldX, int worldY, int screenX, int screenY)
    {
        PrimeCursor(worldX, worldY, screenX, screenY);
        return Game1.pressActionButton(new KeyboardState(), new MouseState(), new GamePadState());
    }

    public string? FindNpcAtTile(int tileX, int tileY)
        => FindNpc(tileX, tileY)?.Name;

    public string? GetTileProperty(int x, int y, string layer, string property)
        => CurrentLocation.doesTileHaveProperty(x, y, property, layer, ignoreTileSheetProperties: false);

    public bool InteractNpcAtTile(int tileX, int tileY)
    {
        var npc = FindLocationNpc(tileX, tileY);
        if (npc is null)
            return InteractEventActorAtTile(tileX, tileY);

        if (HasBlankDialogueMenu)
            Game1.activeClickableMenu = null;

        var handled = npc.checkAction(Game1.player, CurrentLocation);
        if (Game1.activeClickableMenu is null || HasBlankDialogueMenu)
        {
            if (HasBlankDialogueMenu)
                Game1.activeClickableMenu = null;

            if (npc.CurrentDialogue.Count > 0)
                Game1.DrawDialogue(npc.CurrentDialogue.Peek());
            else
                Game1.drawDialogue(npc);
        }

        return handled || Game1.activeClickableMenu is not null;
    }

    private bool InteractEventActorAtTile(int tileX, int tileY)
    {
        var ev = CurrentEvent;
        var actor = FindEventActor(tileX, tileY);
        if (ev is null || actor is null)
            return false;

        if (HasBlankDialogueMenu)
            Game1.activeClickableMenu = null;

        var handled = ev.checkAction(
            new xTile.Dimensions.Location(tileX, tileY),
            Game1.viewport,
            Game1.player);
        if (Game1.activeClickableMenu is null || HasBlankDialogueMenu)
        {
            if (HasBlankDialogueMenu)
                Game1.activeClickableMenu = null;

            if (actor.CurrentDialogue.Count > 0)
                Game1.DrawDialogue(actor.CurrentDialogue.Peek());
            else
                Game1.drawDialogue(actor);
        }

        return handled || Game1.activeClickableMenu is not null;
    }

    private static NPC? FindNpc(int tileX, int tileY)
        => FindLocationNpc(tileX, tileY) ?? FindEventActor(tileX, tileY);

    private static NPC? FindLocationNpc(int tileX, int tileY)
    {
        var tileRect = new Rectangle(tileX * TileSize, tileY * TileSize, TileSize, TileSize);
        foreach (var npc in CurrentLocation.characters)
        {
            if (npc is null)
                continue;

            if ((npc.TilePoint.X == tileX && npc.TilePoint.Y == tileY)
                || npc.GetBoundingBox().Intersects(tileRect))
            {
                return npc;
            }
        }

        return null;
    }

    private static NPC? FindEventActor(int tileX, int tileY)
    {
        var tileRect = new Rectangle(tileX * TileSize, tileY * TileSize, TileSize, TileSize);
        foreach (var actor in ReadActiveEventNpcs())
        {
            if ((actor.TilePoint.X == tileX && actor.TilePoint.Y == tileY)
                || actor.GetBoundingBox().Intersects(tileRect))
            {
                return actor;
            }
        }

        return null;
    }

    private static StardewValley.Event? CurrentEvent
        => Game1.CurrentEvent ?? Game1.currentLocation?.currentEvent;

    private static IEnumerable ReadActors(object? ev)
    {
        if (ev is null)
            yield break;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = ev.GetType();
        foreach (var name in new[] { "actors", "Actors", "characters", "Characters", "festivalActors" })
        {
            var value = type.GetField(name, flags)?.GetValue(ev)
                ?? type.GetProperty(name, flags)?.GetValue(ev);
            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                    yield return item;
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<NPC> ReadActiveEventNpcs()
    {
        foreach (var actor in ReadActors(CurrentEvent))
        {
            if (actor is NPC npc)
                yield return npc;
        }
    }

    private static void PrimeCursor(int worldX, int worldY, int screenX, int screenY)
    {
        ControlledCursor.Set(screenX, screenY);
        Game1.currentCursorTile = new Vector2(worldX / (float)TileSize, worldY / (float)TileSize);
        Game1.lastCursorTile = Game1.currentCursorTile;
        Game1.lastCursorMotionWasMouse = true;
        Game1.mouseCursorTransparency = 1f;
        Game1.wasMouseVisibleThisFrame = true;
    }

    private static GameLocation CurrentLocation
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{InputClickTileHandler.Method} requires a current location");
}
