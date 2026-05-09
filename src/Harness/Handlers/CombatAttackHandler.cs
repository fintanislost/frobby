using System;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Tools;

namespace SdvTestFramework.Harness.Handlers;

public static class CombatAttackHandler
{
    public const string Method = "combat.attack";

    private static readonly ICombatAttackWorld ProductionWorld = new SdvCombatAttackWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, ICombatAttackWorld world)
    {
        var req = RpcParams.Required<CombatAttackRequest>(paramsElement);
        ValidateRequest(req);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save - combat.attack requires a loaded world");

        var direction = ResolveDirection(req, world.TileX, world.TileY);
        var selected = world.SelectWeapon(req.QualifiedItemId);
        for (var i = 0; i < req.Repeat; i++)
        {
            world.FaceDirection(direction);
            world.AttackOnce();
        }

        return ProtocolJson.ToElement(new CombatAttackResult
        {
            Ok = true,
            Tick = world.Tick,
            Tile = new TilePoint { X = world.TileX, Y = world.TileY },
            Direction = direction,
            SelectedItemId = selected.ItemId,
            SelectedItemQualifiedId = selected.QualifiedItemId,
            SelectedItemName = selected.Name,
            SelectedItemRuntimeType = selected.RuntimeType,
        });
    }

    private static void ValidateRequest(CombatAttackRequest req)
    {
        if ((req.X is null) != (req.Y is null))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires both x and y when targeting a tile");
        if (req.X is null && string.IsNullOrWhiteSpace(req.Direction))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires a direction or target tile");
        if (req.Repeat < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires repeat >= 1");
        if (req.DelayTicks < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack requires delay_ticks >= 0");
        if (!string.IsNullOrWhiteSpace(req.Direction) && !IsKnownDirection(req.Direction))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"unknown direction: {req.Direction}");
    }

    private static string ResolveDirection(CombatAttackRequest req, int playerX, int playerY)
    {
        if (!string.IsNullOrWhiteSpace(req.Direction))
            return NormalizeDirection(req.Direction);

        var dx = req.X!.Value - playerX;
        var dy = req.Y!.Value - playerY;
        if (dx == 0 && dy == 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "combat.attack target tile must differ from the player tile");

        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0 ? "right" : "left";

        return dy > 0 ? "down" : "up";
    }

    private static bool IsKnownDirection(string direction)
        => NormalizeDirection(direction) is "up" or "right" or "down" or "left";

    private static string NormalizeDirection(string direction)
        => direction.Trim().ToLowerInvariant();
}

internal interface ICombatAttackWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    int TileX { get; }
    int TileY { get; }
    CombatAttackSelectedItem SelectWeapon(string? qualifiedItemId);
    void FaceDirection(string direction);
    void AttackOnce();
}

internal sealed record CombatAttackSelectedItem(
    string? ItemId,
    string? QualifiedItemId,
    string? Name,
    string? RuntimeType);

internal sealed class SdvCombatAttackWorld : ICombatAttackWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int TileX => Game1.player.TilePoint.X;
    public int TileY => Game1.player.TilePoint.Y;

    public CombatAttackSelectedItem SelectWeapon(string? qualifiedItemId)
    {
        var player = Game1.player;
        if (player.CurrentTool is MeleeWeapon current
            && (string.IsNullOrWhiteSpace(qualifiedItemId)
                || string.Equals(current.QualifiedItemId, qualifiedItemId, StringComparison.Ordinal)))
        {
            return SummarizeWeapon(current);
        }

        for (var slot = 0; slot < player.Items.Count; slot++)
        {
            if (player.Items[slot] is not MeleeWeapon weapon)
                continue;
            if (!string.IsNullOrWhiteSpace(qualifiedItemId)
                && !string.Equals(weapon.QualifiedItemId, qualifiedItemId, StringComparison.Ordinal))
                continue;

            player.CurrentToolIndex = slot;
            return SummarizeWeapon(weapon);
        }

        var message = string.IsNullOrWhiteSpace(qualifiedItemId)
            ? "combat.attack requires a melee weapon in the farmer inventory"
            : $"combat.attack could not find melee weapon {qualifiedItemId} in the farmer inventory";
        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, message);
    }

    public void FaceDirection(string direction)
    {
        Game1.player.faceDirection(DirectionToStardew(direction));
    }

    public void AttackOnce()
    {
        if (Game1.currentLocation is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "combat.attack requires a current location");
        if (Game1.player.CurrentTool is not MeleeWeapon weapon)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "combat.attack requires a selected melee weapon");

        var toolLocation = Game1.player.GetToolLocation();
        weapon.DoFunction(Game1.currentLocation, (int)toolLocation.X, (int)toolLocation.Y, 0, Game1.player);
    }

    private static CombatAttackSelectedItem SummarizeWeapon(MeleeWeapon weapon)
        => new(
            weapon.ItemId,
            weapon.QualifiedItemId,
            weapon.DisplayName ?? weapon.Name,
            weapon.GetType().Name);

    private static int DirectionToStardew(string direction)
        => direction switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {direction}"),
        };
}
