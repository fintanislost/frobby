using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DrawFilterValidatorTests
{
    [Fact]
    public void InRect_InvalidLength_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { InRect = new[] { 0, 0, 10 } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("in_rect", ex.Message);
    }

    [Fact]
    public void InRect_NegativeWidth_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { InRect = new[] { 0, 0, -1, 10 } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("in_rect", ex.Message);
    }

    [Fact]
    public void SourceRect_InvalidLength_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { SourceRect = new[] { 0, 0, 10 } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("source_rect", ex.Message);
    }

    [Fact]
    public void Color_InvalidLength_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { Color = new[] { 255, 0, 0 } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("color", ex.Message);
    }

    [Fact]
    public void LayerDepthRange_InvalidLength_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { LayerDepthRange = new[] { 0.5f } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("layer_depth_range", ex.Message);
    }

    [Fact]
    public void LayerDepthRange_MinGreaterThanMax_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { LayerDepthRange = new[] { 1.0f, 0.5f } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("layer_depth_range", ex.Message);
    }

    [Fact]
    public void ContentHash_NonHexChars_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { ContentHash = "xyz!" };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("content_hash", ex.Message);
    }

    [Fact]
    public void TextureSize_InvalidLength_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { TextureSize = new[] { 512 } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("texture_size", ex.Message);
    }

    [Fact]
    public void TextureSize_NegativeDimension_ThrowsInvalidParams()
    {
        var filter = new DrawFilter { TextureSize = new[] { -512, 1002 } };
        var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("texture_size", ex.Message);
    }
}
