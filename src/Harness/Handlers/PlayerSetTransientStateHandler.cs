using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>player.set_transient_state</c>. Runs on the game thread.</summary>
public static class PlayerSetTransientStateHandler
{
    public const string Method = "player.set_transient_state";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvTransientPlayerStateWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, ITransientPlayerStateWorld world)
    {
        var req = RpcParams.Required<SetTransientStateRequest>(paramsElement);
        if (req.Swimming is null && req.BathingClothes is null)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.InvalidParams,
                "params.swimming or params.bathing_clothes is required");
        }

        world.RequireWorldReady();

        var previousSwimming = world.Swimming;
        var previousBathingClothes = world.BathingClothes;

        if (req.Swimming is not null)
        {
            world.Swimming = req.Swimming.Value;
        }

        if (req.BathingClothes is not null)
        {
            world.BathingClothes = req.BathingClothes.Value;
        }

        return ProtocolJson.ToElement(new SetTransientStateResult
        {
            Tick = world.Tick,
            PreviousSwimming = previousSwimming,
            PreviousBathingClothes = previousBathingClothes,
            Swimming = world.Swimming,
            BathingClothes = world.BathingClothes,
        });
    }
}

internal interface ITransientPlayerStateWorld
{
    bool Swimming { get; set; }
    bool BathingClothes { get; set; }
    int Tick { get; }
    void RequireWorldReady();
}

internal sealed class SdvTransientPlayerStateWorld : ITransientPlayerStateWorld
{
    public bool Swimming
    {
        get => Game1.player.swimming.Value;
        set => Game1.player.swimming.Value = value;
    }

    public bool BathingClothes
    {
        get => Game1.player.bathingClothes.Value;
        set => Game1.player.bathingClothes.Value = value;
    }

    public int Tick => Game1.ticks;

    public void RequireWorldReady() => RpcPreconditions.RequireWorldReady();
}
