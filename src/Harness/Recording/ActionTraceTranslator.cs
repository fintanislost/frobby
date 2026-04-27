using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Pure-function translator from <see cref="RecordedAction"/> buffer to a
/// <see cref="ScenarioStep"/> sequence.
/// </summary>
/// <remarks>
/// Heuristics:
/// <list type="bullet">
///   <item>Multi-warp coalesce: warps within 1 second of the previous warp replace it
///   (player walked, framework saw multiple Warped events).</item>
///   <item>Time-advance debounce: pending minutes only emitted at ≥10-minute threshold
///   (drops per-tick noise — SDV's clock advances ~1.4 in-game minutes per real second).</item>
///   <item>End-of-buffer flush: emit any pending time-advance at stop.</item>
/// </list>
/// </remarks>
internal static class ActionTraceTranslator
{
    private static readonly TimeSpan WarpCoalesceWindow = TimeSpan.FromSeconds(1);
    private const int TimeAdvanceThresholdMinutes = 10;

    public static IReadOnlyList<ScenarioStep> Translate(IReadOnlyList<RecordedAction> buffer)
    {
        var steps = new List<ScenarioStep>();
        int pendingMinutes = 0;
        DateTime? lastWarpAt = null;

        void FlushPendingTime()
        {
            if (pendingMinutes >= TimeAdvanceThresholdMinutes)
            {
                steps.Add(MakeTimeAdvanceStep(pendingMinutes));
            }
            pendingMinutes = 0;
        }

        foreach (var a in buffer)
        {
            switch (a.Kind)
            {
                case ActionKind.Warp:
                    FlushPendingTime();
                    if (lastWarpAt is { } prev && (a.At - prev) < WarpCoalesceWindow && steps.Count > 0)
                    {
                        // Coalesce: replace the previous warp's args.
                        steps[^1] = MakeWarpStep(a.Location!, a.X!.Value, a.Y!.Value);
                    }
                    else
                    {
                        steps.Add(MakeWarpStep(a.Location!, a.X!.Value, a.Y!.Value));
                    }
                    lastWarpAt = a.At;
                    break;

                case ActionKind.NpcInteract:
                    FlushPendingTime();
                    steps.Add(MakeNpcStep(a.NpcName!));
                    lastWarpAt = null; // reset coalesce window
                    break;

                case ActionKind.TimeAdvance:
                    pendingMinutes += a.MinutesElapsed ?? 0;
                    break;
            }
        }
        FlushPendingTime();
        return steps;
    }

    private static ScenarioStep MakeWarpStep(string location, int x, int y) =>
        new()
        {
            Action = "player.warp",
            Args = JsonSerializer.SerializeToElement(new { location, x, y }, ProtocolJson.Options),
        };

    private static ScenarioStep MakeNpcStep(string name) =>
        new()
        {
            Action = "world.interact_npc",
            Args = JsonSerializer.SerializeToElement(new { name }, ProtocolJson.Options),
        };

    private static ScenarioStep MakeTimeAdvanceStep(int minutes) =>
        new()
        {
            Action = "time.advance",
            Args = JsonSerializer.SerializeToElement(new { minutes }, ProtocolJson.Options),
        };
}
