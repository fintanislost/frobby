using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("Recorder")]
public class DrawArmHandlerTests
{
    [Fact]
    public void Handle_TicksBelowOne_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"ticks\":0}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawArmHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NegativeTicks_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"ticks\":-5}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawArmHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MalformedTicks_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"ticks\":\"thirty\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawArmHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NullParams_UsesDefaults_ArmsInMemory()
    {
        // Relies on Recorder.Initialize accepting a null monitor (task T9 relaxation) — unit
        // tests don't have a full SMAPI host to provide one.
        Recorder.Initialize(null, capacity: 16);
        try
        {
            var result = DrawArmHandler.Handle(null);
            Assert.Contains("\"ok\":true", result.GetRawText());
        }
        finally
        {
            // Clear any pending/armed state so subsequent tests see a clean Recorder.
            Recorder.Disarm();
        }
    }
}
