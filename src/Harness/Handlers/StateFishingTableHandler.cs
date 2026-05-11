using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class StateFishingTableHandler
{
    public const string Method = "state.fishing_table";

    private static readonly IFishingWorld ProductionWorld = new SdvFishingWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        return Handle(paramsElement, ProductionWorld);
    }

    internal static JsonElement Handle(JsonElement? paramsElement, IFishingWorld world)
    {
        var request = RpcParams.Optional<FishingTableRequest>(paramsElement);
        var state = FishingProjection.BuildTable(world, request);
        return ProtocolJson.ToElement(state);
    }
}
