using System;
using System.Collections.Generic;
using System.Linq;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ActionTraceTranslatorTests
{
    private static DateTime T0 = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    private static RecordedAction Warp(int seconds, string loc, int x, int y) =>
        new(T0.AddSeconds(seconds), ActionKind.Warp, Location: loc, X: x, Y: y);
    private static RecordedAction Npc(int seconds, string name) =>
        new(T0.AddSeconds(seconds), ActionKind.NpcInteract, NpcName: name);
    private static RecordedAction Time(int seconds, int minutes) =>
        new(T0.AddSeconds(seconds), ActionKind.TimeAdvance, MinutesElapsed: minutes);

    [Fact]
    public void EmptyBuffer_ReturnsEmptyList()
    {
        var steps = ActionTraceTranslator.Translate(Array.Empty<RecordedAction>());
        Assert.Empty(steps);
    }

    [Fact]
    public void OnlyWarp_EmitsWarpStep()
    {
        var steps = ActionTraceTranslator.Translate(new[] { Warp(0, "Farm", 64, 15) });
        Assert.Single(steps);
        Assert.Equal("player.warp", steps[0].Action);
    }

    [Fact]
    public void WarpThenNpcInteract_EmitsBothSteps()
    {
        var steps = ActionTraceTranslator.Translate(new[]
        {
            Warp(0, "SeedShop", 4, 19),
            Npc(2, "Pierre"),
        });
        Assert.Equal(2, steps.Count);
        Assert.Equal("player.warp", steps[0].Action);
        Assert.Equal("world.interact_npc", steps[1].Action);
    }

    [Fact]
    public void MultipleWarpsWithinOneSecond_CoalescesToLatest()
    {
        // Three warps, each 200ms apart — should produce ONE warp step (the last).
        var steps = ActionTraceTranslator.Translate(new[]
        {
            new RecordedAction(T0,                       ActionKind.Warp, Location: "Farm", X: 60, Y: 15),
            new RecordedAction(T0.AddMilliseconds(200),  ActionKind.Warp, Location: "Farm", X: 62, Y: 15),
            new RecordedAction(T0.AddMilliseconds(400),  ActionKind.Warp, Location: "Farm", X: 64, Y: 15),
        });
        Assert.Single(steps);
        // Verify it's the latest (X=64).
        Assert.Contains("\"x\":64", System.Text.Json.JsonSerializer.Serialize(steps[0].Args));
    }

    [Fact]
    public void LongIdleBeforeWarp_EmitsTimeAdvance()
    {
        // 30 minutes accumulated time, then a warp. Expect: time.advance(30) THEN warp.
        var steps = ActionTraceTranslator.Translate(new[]
        {
            Time(0, 30),
            Warp(60, "Farm", 64, 15),
        });
        Assert.Equal(2, steps.Count);
        Assert.Equal("time.advance", steps[0].Action);
        Assert.Equal("player.warp", steps[1].Action);
    }

    [Fact]
    public void TimeAdvanceBelowThreshold_NotEmitted()
    {
        // 5 minutes accumulated — below the 10-min threshold. Should be dropped.
        var steps = ActionTraceTranslator.Translate(new[]
        {
            Time(0, 5),
            Warp(60, "Farm", 64, 15),
        });
        Assert.Single(steps);
        Assert.Equal("player.warp", steps[0].Action);
    }

    [Fact]
    public void EndOfBufferFlushesPendingTime()
    {
        // 30 minutes accumulated, no other events. Expect: time.advance(30) at flush.
        var steps = ActionTraceTranslator.Translate(new[] { Time(0, 30) });
        Assert.Single(steps);
        Assert.Equal("time.advance", steps[0].Action);
    }
}
