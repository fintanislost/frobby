using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class StateFishingContextHandler
{
    public const string Method = "state.fishing_context";

    private static readonly IFishingWorld ProductionWorld = new SdvFishingWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        return Handle(paramsElement, ProductionWorld);
    }

    internal static JsonElement Handle(JsonElement? paramsElement, IFishingWorld world)
    {
        var request = RpcParams.Optional<FishingContextRequest>(paramsElement);
        var state = FishingProjection.BuildContext(world, request);
        return ProtocolJson.ToElement(state);
    }
}
