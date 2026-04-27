using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class FreezeBeginHandlerTests
{
    public FreezeBeginHandlerTests()
    {
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Handle_NoActiveScenario_ThrowsGameStateInvalid()
    {
        // No scenario.begin happened; ScenarioState.IsActive == false.
        var ex = Assert.Throws<JsonRpcException>(() => FreezeBeginHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("active scenario", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_AlreadyFrozen_ThrowsGameStateInvalid()
    {
        ScenarioState.Current.IsActive = true;
        ScenarioState.Current.Seed = 1;
        DeterminismController.HooksForTests = new DeterminismController.Hooks(
            SnapshotAmbient: () => { }, ApplyAmbient: () => { },
            PinRngs: _ => 0, HaltNpcs: () => 0,
            RestoreAmbient: () => { }, RestoreRngs: () => { }, RestoreNpcs: () => { });
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        try
        {
            var ex = Assert.Throws<JsonRpcException>(() => FreezeBeginHandler.Handle(null));
            Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        }
        finally { DeterminismController.ExitFreeze(); }
    }
}
