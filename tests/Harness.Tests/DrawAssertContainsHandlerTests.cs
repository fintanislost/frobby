using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>
/// Tests for the guard paths added in response to T11 code review:
/// null-filter protection, MinCount validation, DrawFilter shape validation.
/// </summary>
[Collection("Recorder")]
public class DrawAssertContainsHandlerTests
{
    public DrawAssertContainsHandlerTests()
    {
        // Ensure the recorder has a buffer so SnapshotEvents doesn't NRE in the handler.
        Recorder.Initialize(null, capacity: 16);
        Recorder.Disarm();
    }

    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertContainsHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NullFilter_TreatedAsEmpty()
    {
        // {"filter": null} should NOT NRE; empty filter matches everything. With an empty
        // buffer, `matched_count` is 0 and `passed` is false (min_count default 1).
        var p = JsonDocument.Parse("{\"filter\":null}").RootElement;
        var result = DrawAssertContainsHandler.Handle(p);
        var text = result.GetRawText();
        Assert.Contains("\"matched_count\":0", text);
        Assert.Contains("\"passed\":false", text);
    }

    [Fact]
    public void Handle_MinCountZero_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"filter\":{},\"min_count\":0}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertContainsHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("min_count", ex.Message);
    }

    [Fact]
    public void Handle_MinCountNegative_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"filter\":{},\"min_count\":-5}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertContainsHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_InvertedLayerDepthRange_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse(
            "{\"filter\":{\"layer_depth_range\":[1.0,0.0]}}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertContainsHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("layer_depth_range", ex.Message);
    }

    [Fact]
    public void Handle_NegativeInRectSize_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse(
            "{\"filter\":{\"in_rect\":[0,0,-10,10]}}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertContainsHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_WrongColorLength_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse(
            "{\"filter\":{\"color\":[255,255,255]}}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertContainsHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("color", ex.Message);
    }
}
