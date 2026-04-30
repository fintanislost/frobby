using System;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>scenario.begin</c>. Pins RNG, resets scenario state, records start tick.</summary>
public static class ScenarioBeginHandler
{
    public const string Method = "scenario.begin";

    /// <summary>Set by ModEntry at startup so the handler can log SeedPinner output.</summary>
    public static IMonitor? Monitor { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<ScenarioBeginRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name required");

        var s = ScenarioState.Current;
        if (s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.ScenarioNotActive,
                $"scenario '{s.Name}' already active — call scenario.end first");

        // Pin RNG per M0 spike. Monitor may be null in unit tests; SeedPinner already
        // handles that case gracefully via its existing signature.
        if (Monitor != null)
            SeedPinner.Pin(req.Seed, Monitor);

        ControlledCursor.Clear();
        s.Reset();
        s.IsActive = true;
        s.Name = req.Name;
        s.Seed = req.Seed;
        s.SessionId = Guid.NewGuid().ToString("N");
        s.StartTick = Game1.ticks;
        s.StartUtc = DateTime.UtcNow;

        return ProtocolJson.ToElement(new ScenarioBeginResult
        {
            SessionId = s.SessionId,
            Tick = s.StartTick,
        });
    }
}
