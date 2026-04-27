using SdvTestFramework.Harness.Scenarios;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

// ScenarioState.Current is a process-wide singleton; run all scenario-touching tests
// serially in one collection to prevent parallel test races (seen in CI where one
// test's Reset() zeroed another test's injected counters).
[Collection("ScenarioState")]
public class ScenarioStateTests
{
    [Fact]
    public void Reset_ClearsEverything()
    {
        var s = ScenarioState.Current;
        s.IsActive = true;
        s.Name = "x";
        s.SessionId = "abc";
        s.AssertionsRun = 5;
        s.AssertionsPassed = 3;
        s.Seed = 999;

        s.Reset();
        Assert.False(s.IsActive);
        Assert.Empty(s.Name);
        Assert.Empty(s.SessionId);
        Assert.Equal(0, s.AssertionsRun);
        Assert.Equal(0, s.AssertionsPassed);
        Assert.Equal(0, s.Seed);
    }
}
