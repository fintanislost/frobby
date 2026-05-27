using System;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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
        var handled = button == "right"
            ? world.ClickRightTile(worldX, worldY, screenX, screenY)
            : world.ClickLeftTile(worldX, worldY, screenX, screenY);
        var npcFallbackUsed = false;
        if (button == "right"
            && targetNpcName is not null
            && (!handled || !world.HasActiveMenu || world.HasBlankDialogueMenu))
        {
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
            SelectedItem = world.SelectedItem is { } selected
                ? PlayerSelectItemHandler.ToSummary(selected)
                : null,
            Handled = handled,
            TargetNpcName = targetNpcName,
            NpcFallbackUsed = npcFallbackUsed,
        });
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
    ISelectableInventoryItem? SelectedItem { get; }
    bool ClickLeftTile(int worldX, int worldY, int screenX, int screenY);
    bool ClickRightTile(int worldX, int worldY, int screenX, int screenY);
    string? FindNpcAtTile(int tileX, int tileY);
    bool HasBlankDialogueMenu { get; }
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

    public bool InteractNpcAtTile(int tileX, int tileY)
    {
        var npc = FindNpc(tileX, tileY);
        if (npc is null)
            return false;

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

    private static NPC? FindNpc(int tileX, int tileY)
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
