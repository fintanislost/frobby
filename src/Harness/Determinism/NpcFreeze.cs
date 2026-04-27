using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace SdvTestFramework.Harness.Determinism;

/// <summary>Snapshot entry capturing one NPC's pre-halt state.</summary>
public readonly struct NpcFreezeSnapshot
{
    public NpcFreezeSnapshot(object npc, Vector2 position, object? schedule, object? controller)
    {
        Npc = npc;
        Position = position;
        Schedule = schedule;
        Controller = controller;
    }
    public object Npc { get; }
    public Vector2 Position { get; }
    public object? Schedule { get; }
    public object? Controller { get; }
}

/// <summary>
/// Halt every input NPC: snapshot their <c>Position</c>/<c>Schedule</c>/<c>controller</c>,
/// call <c>Halt()</c>, null out <c>controller</c>. Restore reverses those steps. Missing
/// fields tolerated silently — exotic subclasses that lack one of these get skipped.
/// </summary>
public static class NpcFreeze
{
    private const BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static IReadOnlyList<NpcFreezeSnapshot> HaltAll(IEnumerable<object> npcs)
    {
        var snaps = new List<NpcFreezeSnapshot>();
        foreach (var npc in npcs)
        {
            var t = npc.GetType();
            var positionField = t.GetField("Position", AllInstance);
            var scheduleField = t.GetField("Schedule", AllInstance);
            var controllerField = t.GetField("controller", AllInstance);
            if (positionField is null || scheduleField is null || controllerField is null)
                continue;

            var pos = (Vector2)(positionField.GetValue(npc) ?? default(Vector2));
            var sched = scheduleField.GetValue(npc);
            var ctrl = controllerField.GetValue(npc);

            // Call Halt() if present. Mirrors NPC.Halt() in SDV.
            t.GetMethod("Halt", AllInstance)?.Invoke(npc, null);
            controllerField.SetValue(npc, null);

            snaps.Add(new NpcFreezeSnapshot(npc, pos, sched, ctrl));
        }
        return snaps;
    }

    public static void RestoreAll(IEnumerable<NpcFreezeSnapshot> snapshots)
    {
        foreach (var s in snapshots)
        {
            var t = s.Npc.GetType();
            t.GetField("Position", AllInstance)?.SetValue(s.Npc, s.Position);
            t.GetField("Schedule", AllInstance)?.SetValue(s.Npc, s.Schedule);
            t.GetField("controller", AllInstance)?.SetValue(s.Npc, s.Controller);
        }
    }
}
