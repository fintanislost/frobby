using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for the <c>player.give_item</c> RPC method. Creates an item via the SDV 1.6
/// unified <see cref="ItemRegistry"/> factory (qualified id, e.g. <c>"(O)388"</c>) and
/// adds it to the local farmer's inventory. Runs on the game thread.
/// </summary>
/// <remarks>
/// The created stack is passed through <c>addItemByMenuIfNecessary</c>, which puts the
/// item into inventory directly when there's room, otherwise surfaces the in-game
/// "hold up" pickup menu. Scenarios should use <see cref="MutatorOk.Tick"/> as the
/// temporal anchor and poll <c>state.player</c> to verify the add.
/// </remarks>
public static class PlayerGiveItemHandler
{
    public const string Method = "player.give_item";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<GiveItemRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.id required");
        if (req.Count < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.count must be >= 1");

        RpcPreconditions.RequireWorldReady();

        // ItemRegistry.Create returns a placeholder "Error Item" (not null) for unknown IDs,
        // so the null-check idiom silently accepts typos. Validate existence upfront instead.
        if (!ItemRegistry.Exists(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"unknown item id: {req.Id}");

        var item = ItemRegistry.Create(req.Id, req.Count)!;
        Game1.player.addItemByMenuIfNecessary(item);

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
