using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>freeze.end</c>. Unwinds the FREEZE state.</summary>
public static class FreezeEndHandler
{
    public const string Method = "freeze.end";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        if (!DeterminismController.Frozen)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "freeze.end requires Frozen == true (not frozen)");

        DeterminismController.ExitFreeze();

        return ProtocolJson.ToElement(new MutatorOk { Ok = true, Tick = Game1.ticks });
    }
}
