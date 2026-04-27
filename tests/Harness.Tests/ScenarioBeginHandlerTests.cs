using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class ScenarioBeginHandlerTests
{
    public ScenarioBeginHandlerTests()
    {
        // Keep SeedPinner dormant; tests don't need RNG pinning.
        ScenarioBeginHandler.Monitor = null;
        ScenarioState.Current.Reset();
    }

    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => ScenarioBeginHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyName_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"name\":\"\",\"seed\":42}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ScenarioBeginHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_AlreadyActive_ThrowsScenarioNotActive()
    {
        var p = JsonDocument.Parse("{\"name\":\"first\",\"seed\":1}").RootElement;
        ScenarioBeginHandler.Handle(p);

        var second = JsonDocument.Parse("{\"name\":\"second\",\"seed\":2}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => ScenarioBeginHandler.Handle(second));
        Assert.Equal(JsonRpcErrorCode.ScenarioNotActive, ex.Code);
        Assert.Contains("already active", ex.Message);

        ScenarioState.Current.Reset();   // cleanup so other tests are clean
    }

    [Fact]
    public void Handle_Valid_SetsStateAndReturnsSessionId()
    {
        var p = JsonDocument.Parse("{\"name\":\"smoke\",\"seed\":99}").RootElement;
        var result = ScenarioBeginHandler.Handle(p);
        var s = ScenarioState.Current;

        Assert.True(s.IsActive);
        Assert.Equal("smoke", s.Name);
        Assert.NotEmpty(s.SessionId);
        Assert.Contains("\"session_id\":", result.GetRawText());
        Assert.Contains("\"tick\":", result.GetRawText());

        ScenarioState.Current.Reset();
    }

    [Fact]
    public void Handle_PersistsSeedToScenarioState()
    {
        ScenarioState.Current.Reset();
        var json = JsonDocument.Parse("""{"name":"s","seed":1234}""").RootElement;
        ScenarioBeginHandler.Handle(json);
        Assert.Equal(1234, ScenarioState.Current.Seed);
        ScenarioState.Current.Reset();
    }
}
