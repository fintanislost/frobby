using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Determinism;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class NpcFreezeTests
{
    // Shim stand-in for NPC. Fields mirror the real NPC type closely enough for the
    // pinner: Position (Vector2), Schedule (object-typed so the shim can hold anything),
    // controller (object-typed same reason). Halt() is called during freeze.
    private sealed class NpcShim
    {
        public Vector2 Position;
        public object? Schedule;
        public object? controller;
        public int HaltCount;
        public void Halt() => HaltCount++;
    }

    [Fact]
    public void HaltAll_CallsHaltAndNullsController()
    {
        var npc = new NpcShim
        {
            Position = new Vector2(3, 4),
            Schedule = new object(),
            controller = new object(),
        };

        var snaps = NpcFreeze.HaltAll(new object[] { npc });

        Assert.Equal(1, npc.HaltCount);
        Assert.Null(npc.controller);
        Assert.Single(snaps);
    }

    [Fact]
    public void RestoreAll_RestoresPositionScheduleController()
    {
        var sched = new object();
        var ctrl = new object();
        var npc = new NpcShim
        {
            Position = new Vector2(3, 4),
            Schedule = sched,
            controller = ctrl,
        };

        var snaps = NpcFreeze.HaltAll(new object[] { npc });
        // Mutate post-halt to confirm restore overwrites
        npc.Position = new Vector2(99, 99);

        NpcFreeze.RestoreAll(snaps);

        Assert.Equal(new Vector2(3, 4), npc.Position);
        Assert.Same(sched, npc.Schedule);
        Assert.Same(ctrl, npc.controller);
    }

    [Fact]
    public void HaltAll_ShimWithoutFields_SilentlySkipped()
    {
        // Object without any of the expected fields — should not throw.
        var snaps = NpcFreeze.HaltAll(new object[] { new object() });
        Assert.Empty(snaps);
    }
}
