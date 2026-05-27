using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using SObject = StardewValley.Object;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>festival.set_grange_display</c>. Seeds the vanilla Stardew Fair grange display.</summary>
public static class FestivalSetGrangeDisplayHandler
{
    public const string Method = "festival.set_grange_display";

    private static readonly IGrangeDisplayWorld ProductionWorld = new SdvGrangeDisplayWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IGrangeDisplayWorld world)
    {
        var req = RpcParams.Required<SetGrangeDisplayRequest>(paramsElement);
        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{Method} requires a loaded world");

        Validate(req);
        if (req.Clear)
            world.ClearDisplay();

        var resultItems = new List<SetGrangeDisplayItemResult>();
        foreach (var itemReq in req.Items)
        {
            if (!world.ItemExists(itemReq.Id))
                throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"unknown item id: {itemReq.Id}");

            var obj = world.CreateObject(itemReq.Id)
                ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                    $"item is not an object: {itemReq.Id}");

            if (itemReq.Stack is not null)
                obj.Stack = itemReq.Stack.Value;
            if (itemReq.Quality is not null)
                obj.Quality = itemReq.Quality.Value;

            world.SetDisplayItem(itemReq.Slot, obj);
            resultItems.Add(new SetGrangeDisplayItemResult
            {
                Slot = itemReq.Slot,
                Id = obj.Id,
                QualifiedId = obj.QualifiedId,
                Name = obj.Name,
                Stack = obj.Stack,
                Quality = obj.Quality,
                RuntimeType = obj.RuntimeType,
            });
        }

        return ProtocolJson.ToElement(new SetGrangeDisplayResult
        {
            Tick = world.Tick,
            FilledSlots = world.FilledSlots,
            Items = resultItems,
        });
    }

    private static void Validate(SetGrangeDisplayRequest req)
    {
        foreach (var item in req.Items)
        {
            if (item.Slot is < 0 or > 8)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.items[].slot must be between 0 and 8");
            if (string.IsNullOrWhiteSpace(item.Id))
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.items[].id required");
            if (item.Stack is not null && item.Stack < 1)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.items[].stack must be >= 1");
            if (item.Quality is not null && item.Quality < 0)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.items[].quality must be >= 0");
        }
    }
}

internal interface IGrangeDisplayWorld
{
    bool IsWorldReady { get; }
    int Tick { get; }
    bool ItemExists(string id);
    IPlaceableObject? CreateObject(string id);
    void ClearDisplay();
    void SetDisplayItem(int slot, IPlaceableObject item);
    int FilledSlots { get; }
}

internal sealed class SdvGrangeDisplayWorld : IGrangeDisplayWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public int Tick => Game1.ticks;
    public int FilledSlots => Game1.player.team.grangeDisplay.Count(item => item is not null);

    public bool ItemExists(string id) => ItemRegistry.Exists(id);

    public IPlaceableObject? CreateObject(string id)
    {
        var item = ItemRegistry.Create(id);
        return item is SObject obj ? new SdvPlaceableObject(obj) : null;
    }

    public void ClearDisplay()
    {
        EnsureDisplaySlots();
        for (var i = 0; i < 9; i++)
            Game1.player.team.grangeDisplay[i] = null;
    }

    public void SetDisplayItem(int slot, IPlaceableObject item)
    {
        if (item is not SdvPlaceableObject sdvObject)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                $"{FestivalSetGrangeDisplayHandler.Method} can only use live Stardew objects");

        EnsureDisplaySlots();
        Game1.player.team.grangeDisplay[slot] = sdvObject.Object;
    }

    private static void EnsureDisplaySlots()
    {
        while (Game1.player.team.grangeDisplay.Count < 9)
            Game1.player.team.grangeDisplay.Add(null);
    }
}
