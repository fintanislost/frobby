using System;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class FreezeEndHandlerTests
{
    public FreezeEndHandlerTests()
    {
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Handle_NotFrozen_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() => FreezeEndHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not frozen", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_WhenFrozen_FlipsFrozenFalse()
    {
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => { }, ApplyAmbient: () => { },
            PinRngs: _ => 0, HaltNpcs: () => 0,
            RestoreAmbient: () => { }, RestoreRngs: () => { }, RestoreNpcs: () => { });
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        try
        {
            FreezeEndHandler.Handle(null);
            Assert.False(DeterminismController.Frozen);
        }
        finally
        {
            // Handler already thawed; guard against double-ExitFreeze.
            if (DeterminismController.Frozen) DeterminismController.ExitFreeze();
        }
    }
}
