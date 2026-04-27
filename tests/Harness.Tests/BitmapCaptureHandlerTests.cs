using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class BitmapCaptureHandlerTests
{
    public BitmapCaptureHandlerTests()
    {
        ScenarioState.Current.Reset();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void MissingScenario_ReturnsGameStateInvalid()
    {
        // Ensure no scenario active.
        ScenarioState.Current.Reset();

        var ex = Assert.Throws<JsonRpcException>(() =>
            BitmapCaptureHandler.Handle(paramsElement: null));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("bitmap.capture requires an active scenario", ex.Message);
    }
}
