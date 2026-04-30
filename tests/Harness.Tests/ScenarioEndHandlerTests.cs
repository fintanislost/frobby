using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class ScenarioEndHandlerTests
{
    public ScenarioEndHandlerTests()
    {
        ScenarioBeginHandler.Monitor = null;
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
        ControlledCursor.Clear();
    }

    [Fact]
    public void Handle_NoActiveScenario_ThrowsScenarioNotActive()
    {
        var ex = Assert.Throws<JsonRpcException>(() => ScenarioEndHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.ScenarioNotActive, ex.Code);
    }

    [Fact]
    public void Handle_Active_ReturnsStatsAndResets()
    {
        // Begin a scenario, inject some counters, end.
        var begin = JsonDocument.Parse("{\"name\":\"x\",\"seed\":0}").RootElement;
        ScenarioBeginHandler.Handle(begin);
        ScenarioState.Current.AssertionsRun = 5;
        ScenarioState.Current.AssertionsPassed = 4;

        var result = ScenarioEndHandler.Handle(null);
        var text = result.GetRawText();
        Assert.Contains("\"assertions_run\":5", text);
        Assert.Contains("\"assertions_passed\":4", text);
        Assert.Contains("\"duration_ms\":", text);

        Assert.False(ScenarioState.Current.IsActive);
    }

    [Fact]
    public void Handle_WhenFrozen_AutoThaws()
    {
        ScenarioState.Current.IsActive = true;
        ScenarioState.Current.Name = "test";
        ScenarioState.Current.StartUtc = System.DateTime.UtcNow;
        DeterminismController.EnterFreeze(seed: 1, monitor: null);
        Assert.True(DeterminismController.Frozen);

        ScenarioEndHandler.Handle(null);

        Assert.False(DeterminismController.Frozen);
    }

    [Fact]
    public void Handle_WithAssertionCounts_PopulatesScenarioState()
    {
        var s = ScenarioState.Current;
        s.Reset();
        s.IsActive = true;
        s.Name = "t5";
        s.StartUtc = System.DateTime.UtcNow;

        var json = System.Text.Json.JsonDocument
            .Parse("""{"assertions_run":7,"assertions_passed":6}""").RootElement;
        var resp = ScenarioEndHandler.Handle(json);

        Assert.Equal(7, resp.GetProperty("assertions_run").GetInt32());
        Assert.Equal(6, resp.GetProperty("assertions_passed").GetInt32());
    }

    [Fact]
    public void Handle_ClearsControlledCursor()
    {
        var s = ScenarioState.Current;
        s.Reset();
        s.IsActive = true;
        s.Name = "t6";
        s.StartUtc = System.DateTime.UtcNow;
        ControlledCursor.Set(144, 134);

        ScenarioEndHandler.Handle(null);

        Assert.False(ControlledCursor.HasOverride);
    }
}
