using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class RpcParamsTests
{
    private sealed class Example { public string? Name { get; set; } public int Count { get; set; } }

    [Fact]
    public void Required_NullParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => RpcParams.Required<Example>(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params required", ex.Message);
    }

    [Fact]
    public void Required_ValidJson_ReturnsDeserialized()
    {
        var p = JsonDocument.Parse("{\"name\":\"a\",\"count\":3}").RootElement;
        var result = RpcParams.Required<Example>(p);
        Assert.Equal("a", result.Name);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Required_WrongFieldType_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"name\":\"a\",\"count\":\"not-a-number\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => RpcParams.Required<Example>(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("parse error", ex.Message);
    }

    [Fact]
    public void Required_JsonNull_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("null").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => RpcParams.Required<Example>(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Optional_NullParams_ReturnsDefault()
    {
        var result = RpcParams.Optional<Example>(null);
        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Optional_ValidJson_ReturnsDeserialized()
    {
        var p = JsonDocument.Parse("{\"name\":\"a\",\"count\":3}").RootElement;
        var result = RpcParams.Optional<Example>(p);
        Assert.Equal("a", result.Name);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Optional_MalformedJson_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"count\":\"not-a-number\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => RpcParams.Optional<Example>(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("parse error", ex.Message);
    }
}
