using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class FreezeStatusHandlerTests
{
    public FreezeStatusHandlerTests()
    {
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Handle_NotFrozen_ReturnsFrozenFalse()
    {
        var result = FreezeStatusHandler.Handle(null);
        Assert.False(result.GetProperty("frozen").GetBoolean());
    }

    [Fact]
    public void Handle_Frozen_ReturnsFrozenTrue()
    {
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => { }, ApplyAmbient: () => { },
            PinRngs: _ => 0, HaltNpcs: () => 0,
            RestoreAmbient: () => { }, RestoreRngs: () => { }, RestoreNpcs: () => { });
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        try
        {
            var result = FreezeStatusHandler.Handle(null);
            Assert.True(result.GetProperty("frozen").GetBoolean());
        }
        finally
        {
            if (DeterminismController.Frozen)
                DeterminismController.ExitFreeze();
        }
    }
}
