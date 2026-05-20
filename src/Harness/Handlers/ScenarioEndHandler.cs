using System;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>scenario.end</c>. Returns duration + assertion stats, clears scenario state.</summary>
public static class ScenarioEndHandler
{
    public const string Method = "scenario.end";

    /// <summary>Set by ModEntry at startup so auto-thaw logs are attributable.</summary>
    public static IMonitor? Monitor { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var s = ScenarioState.Current;
        if (!s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.ScenarioNotActive, "no scenario active");

        // Safety valve: if an assertion failure inside the scenario left the world frozen,
        // unwind it here so the harness doesn't wedge. Mirrors the S4 scenario-end-in-finally
        // fix applied during the M1 smoke sweep.
        if (DeterminismController.Frozen)
        {
            Monitor?.Log("scenario ended while frozen — auto-thawed", LogLevel.Info);
            DeterminismController.ExitFreeze();
        }

        // Force-disarm the recorder so the next scenario can arm cleanly. Without this, a
        // scenario that arms for N ticks and ends before N ticks elapse leaves the recorder
        // armed, and the next scenario's draw.arm fails with "Already armed."
        Recorder.Disarm();
        ControlledCursor.Clear();
        CombatLabIdentityRegistry.Clear();

        // Optional per-scenario counter snapshot, populated by ScenarioRunner when it calls
        // scenario.end. Missing params → the scenario ran without runner wiring (e.g. hand
        // probe via Python), in which case the counters stay at their existing values.
        if (paramsElement is { ValueKind: JsonValueKind.Object } obj)
        {
            if (obj.TryGetProperty("assertions_run", out var ar) && ar.TryGetInt32(out var arI))
                s.AssertionsRun = arI;
            if (obj.TryGetProperty("assertions_passed", out var ap) && ap.TryGetInt32(out var apI))
                s.AssertionsPassed = apI;
        }

        var elapsed = (DateTime.UtcNow - s.StartUtc).TotalMilliseconds;
        var result = new ScenarioEndResult
        {
            DurationMs = (int)elapsed,
            AssertionsRun = s.AssertionsRun,
            AssertionsPassed = s.AssertionsPassed,
        };

        s.Reset();
        return ProtocolJson.ToElement(result);
    }
}
