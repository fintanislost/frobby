using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal interface IFishingSamplerWorld : IFishingWorld
{
    IFishingSampleState Snapshot(FishingSampleCatchRequest request);

    FishingCatchResult SampleCatch(FishingSampleCatchRequest request, TilePoint tile, int attempt);
}

internal interface IFishingSampleState
{
    void Restore();
}

public static class FishingSampleCatchHandler
{
    public const string Method = "fishing.sample_catch";
    private const int MaxAttempts = 100;

    private static readonly IFishingSamplerWorld ProductionWorld = new SdvFishingWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IFishingSamplerWorld world)
    {
        var request = RpcParams.Optional<FishingSampleCatchRequest>(paramsElement);
        if (request.Attempts <= 0)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.attempts must be > 0");
        }

        if (request.Attempts > MaxAttempts)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"params.attempts must be <= {MaxAttempts}");
        }

        var context = FishingProjection.BuildContext(world, request);
        var state = world.Snapshot(request);
        var results = new List<FishingCatchResult>();
        try
        {
            for (var attempt = 1; attempt <= request.Attempts; attempt++)
            {
                results.Add(world.SampleCatch(request, context.Tile, attempt));
            }
        }
        finally
        {
            if (request.RestoreState)
            {
                state.Restore();
            }
        }

        return ProtocolJson.ToElement(new FishingSampleCatchResult
        {
            Context = context,
            Attempts = request.Attempts,
            StateRestored = request.RestoreState,
            Results = results,
        });
    }
}
