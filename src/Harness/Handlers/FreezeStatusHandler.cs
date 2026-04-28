using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>freeze.status</c>. Pure query — no preconditions.</summary>
public static class FreezeStatusHandler
{
    public const string Method = "freeze.status";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        return ProtocolJson.ToElement(new FreezeStatusResult
        {
            Frozen = DeterminismController.Frozen,
            IsWarping = Game1.isWarping,
            Tick = Game1.ticks,
        });
    }
}
