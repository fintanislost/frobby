using System;
using System.Collections.Generic;
using SdvTestFramework.Harness.Determinism;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

// Shares the ScenarioState collection to serialize access to the process-wide
// DeterminismController._frozen flag — handler tests in the same collection
// mutate it and would race in parallel.
[Collection("ScenarioState")]
public class DeterminismControllerTests
{
    public DeterminismControllerTests()
    {
        // Tests mutate a process-wide singleton; reset before each.
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Frozen_DefaultsFalse()
    {
        Assert.False(DeterminismController.Frozen);
    }

    [Fact]
    public void EnterFreeze_WhenNotFrozen_FlipsFrozenTrue()
    {
        DeterminismController.EnterFreeze(seed: 42, monitor: null);
        Assert.True(DeterminismController.Frozen);
    }

    [Fact]
    public void EnterFreeze_WhenAlreadyFrozen_Throws()
    {
        DeterminismController.EnterFreeze(seed: 42, monitor: null);
        Assert.Throws<InvalidOperationException>(
            () => DeterminismController.EnterFreeze(seed: 43, monitor: null));
    }

    [Fact]
    public void ExitFreeze_WhenFrozen_FlipsFrozenFalse()
    {
        DeterminismController.EnterFreeze(seed: 42, monitor: null);
        DeterminismController.ExitFreeze();
        Assert.False(DeterminismController.Frozen);
    }

    [Fact]
    public void ExitFreeze_WhenNotFrozen_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DeterminismController.ExitFreeze());
    }

    [Fact]
    public void EnterFreeze_ThenExit_CallsOrchestrationHooksInOrder()
    {
        // Inject a recorder to observe the ordering of SnapshotAmbient / PinRngs / HaltNpcs.
        var log = new List<string>();
        var priorHooks = DeterminismController.HooksForTests;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => log.Add("snap"),
            ApplyAmbient: () => log.Add("apply"),
            PinRngs: _ => { log.Add("pin"); return 7; },
            HaltNpcs: () => { log.Add("halt"); return 3; },
            RestoreAmbient: () => log.Add("unapply"),
            RestoreRngs: () => log.Add("unpin"),
            RestoreNpcs: () => log.Add("unhalt"));
        try
        {
            DeterminismController.EnterFreeze(seed: 42, monitor: null);
            DeterminismController.ExitFreeze();

            Assert.Equal(
                new[] { "snap", "apply", "pin", "halt", "unpin", "unhalt", "unapply" },
                log);
        }
        finally { DeterminismController.HooksForTests = priorHooks; }
    }

    [Fact]
    public void EnterFreeze_WhenPinThrows_RollsBackAndRethrows()
    {
        var log = new List<string>();
        var priorHooks = DeterminismController.HooksForTests;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => log.Add("snap"),
            ApplyAmbient: () => log.Add("apply"),
            PinRngs: _ => throw new InvalidOperationException("simulated failure"),
            HaltNpcs: () => { log.Add("halt"); return 0; },
            RestoreAmbient: () => log.Add("unapply"),
            RestoreRngs: () => log.Add("unpin"),
            RestoreNpcs: () => log.Add("unhalt"));
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => DeterminismController.EnterFreeze(seed: 42, monitor: null));
            // Frozen state rolled back.
            Assert.False(DeterminismController.Frozen);
            // Only snap + apply ran; pin threw; unapply ran during rollback.
            Assert.Equal(new[] { "snap", "apply", "unapply" }, log);
        }
        finally { DeterminismController.HooksForTests = priorHooks; }
    }

    [Fact]
    public void EnterFreeze_ReportsCounts()
    {
        var priorHooks = DeterminismController.HooksForTests;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => { },
            ApplyAmbient: () => { },
            PinRngs: _ => 11,
            HaltNpcs: () => 22,
            RestoreAmbient: () => { },
            RestoreRngs: () => { },
            RestoreNpcs: () => { });
        try
        {
            var result = DeterminismController.EnterFreeze(seed: 1, monitor: null);
            Assert.Equal(11, result.LocationsPinned);
            Assert.Equal(22, result.NpcsHalted);
        }
        finally
        {
            if (DeterminismController.Frozen) DeterminismController.ExitFreeze();
            DeterminismController.HooksForTests = priorHooks;
        }
    }
}
