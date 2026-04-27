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

    [Fact]
    public void AllowUnfrozen_BypassesFreezePrecondition()
    {
        ScenarioState.Current.IsActive = true;
        ScenarioState.Current.Name = "bitmap_allow_unfrozen";

        var frozenEx = Assert.Throws<JsonRpcException>(() =>
            BitmapCaptureHandler.Handle(paramsElement: null));
        Assert.Contains("requires FREEZE phase", frozenEx.Message);

        var allowParams = JsonDocument.Parse("{\"allow_unfrozen\":true}").RootElement;
        var graphicsEx = Assert.Throws<JsonRpcException>(() =>
            BitmapCaptureHandler.Handle(allowParams));

        Assert.Equal(JsonRpcErrorCode.InternalError, graphicsEx.Code);
        Assert.Contains("GraphicsDevice unavailable", graphicsEx.Message);
    }
}
