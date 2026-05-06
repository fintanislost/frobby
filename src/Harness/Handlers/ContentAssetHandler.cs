using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>content.asset</c>. Runs on the game thread.</summary>
public static class ContentAssetHandler
{
    public const string Method = "content.asset";

    public static IContentAssetLoader? Loader { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var loader = Loader
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "content.asset requires a content loader");
        var req = RpcParams.Required<ContentAssetRequest>(paramsElement);
        var result = ContentAssetProjector.Project(loader, req);
        return ProtocolJson.ToElement(result);
    }
}
