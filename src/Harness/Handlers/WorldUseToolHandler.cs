using System;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Tools;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>world.use_tool</c>. Runs a player inventory tool against a target tile.</summary>
public static class WorldUseToolHandler
{
    public const string Method = "world.use_tool";

    private static readonly IUseToolWorld ProductionWorld = new SdvUseToolWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IUseToolWorld world)
    {
        var req = RpcParams.Required<UseToolRequest>(paramsElement);
        var tool = NormalizeTool(req.Tool);
        ValidateRequest(req, tool);

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "no active save - world.use_tool requires a loaded world");

        if (!string.IsNullOrWhiteSpace(req.Location)
            && !string.Equals(req.Location, world.CurrentLocationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"world.use_tool location guard expected {req.Location}, current location is {world.CurrentLocationName}");
        }

        var selected = world.SelectTool(tool);
        if (!string.IsNullOrWhiteSpace(req.Facing))
            world.FaceDirection(NormalizeDirection(req.Facing));
        world.UseToolAtTile(req.X!.Value, req.Y!.Value, req.Power);

        return ProtocolJson.ToElement(new UseToolResult
        {
            Tick = world.Tick,
            Tool = tool,
            Location = world.CurrentLocationName,
            Tile = new TilePoint { X = req.X.Value, Y = req.Y.Value },
            SelectedItemId = selected.ItemId,
            SelectedItemQualifiedId = selected.QualifiedItemId,
            SelectedItemName = selected.Name,
            SelectedItemRuntimeType = selected.RuntimeType,
            SelectedToolIndex = selected.ToolIndex,
            Invoked = true,
        });
    }

    private static void ValidateRequest(UseToolRequest req, string tool)
    {
        if (tool != "Hoe")
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.use_tool currently only supports Hoe");
        if ((req.X is null) != (req.Y is null))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.use_tool requires both x and y");
        if (req.X is null || req.Y is null)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                "world.use_tool requires target tile x and y");
        if (req.X < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.x must be >= 0");
        if (req.Y < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.y must be >= 0");
        if (req.Power < 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.power must be >= 0");
        if (!string.IsNullOrWhiteSpace(req.Facing) && !IsKnownDirection(req.Facing))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {req.Facing}");
    }

    private static string NormalizeTool(string? tool)
    {
        if (string.IsNullOrWhiteSpace(tool))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "world.use_tool requires params.tool");

        return tool.Trim().Equals("hoe", StringComparison.OrdinalIgnoreCase) ? "Hoe" : tool.Trim();
    }

    private static bool IsKnownDirection(string direction)
        => NormalizeDirection(direction) is "up" or "right" or "down" or "left";

    private static string NormalizeDirection(string direction)
        => direction.Trim().ToLowerInvariant();
}

internal interface IUseToolWorld
{
    bool IsWorldReady { get; }
    string CurrentLocationName { get; }
    int Tick { get; }
    UseToolSelectedItem SelectTool(string tool);
    void FaceDirection(string direction);
    void UseToolAtTile(int x, int y, int power);
}

internal sealed record UseToolSelectedItem(
    string? ItemId,
    string? QualifiedItemId,
    string? Name,
    string? RuntimeType,
    int? ToolIndex);

internal sealed class SdvUseToolWorld : IUseToolWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public string CurrentLocationName => CurrentLocation.NameOrUniqueName ?? CurrentLocation.Name ?? string.Empty;
    public int Tick => Game1.ticks;

    public UseToolSelectedItem SelectTool(string tool)
    {
        var player = Game1.player;
        if (player.CurrentTool is Hoe current)
            return SummarizeTool(current, player.CurrentToolIndex);

        for (var slot = 0; slot < player.Items.Count; slot++)
        {
            if (player.Items[slot] is not Hoe hoe)
                continue;

            player.CurrentToolIndex = slot;
            return SummarizeTool(hoe, slot);
        }

        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
            "world.use_tool could not find Hoe in the farmer inventory");
    }

    public void FaceDirection(string direction)
    {
        Game1.player.faceDirection(DirectionToStardew(direction));
    }

    public void UseToolAtTile(int x, int y, int power)
    {
        if (Game1.player.CurrentTool is not Hoe hoe)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "world.use_tool requires a selected Hoe");

        // Stardew Tool.DoFunction receives pixel coordinates and converts them to tile
        // coordinates internally. Calling the tool path lets location/Harmony patches
        // observe buried-item behavior without direct reward mutation.
        hoe.DoFunction(CurrentLocation, x * 64, y * 64, power, Game1.player);
    }

    private static UseToolSelectedItem SummarizeTool(Tool tool, int? slot)
        => new(
            tool.ItemId,
            tool.QualifiedItemId,
            tool.DisplayName ?? tool.Name,
            tool.GetType().Name,
            slot);

    private static int DirectionToStardew(string direction)
        => direction switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown direction: {direction}"),
        };

    private static GameLocation CurrentLocation
        => Game1.currentLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{WorldUseToolHandler.Method} requires a current location");
}
