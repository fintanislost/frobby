using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateNpcHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => StateNpcHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Handle_MissingName_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => StateNpcHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NameWrongType_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"name\": 42}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => StateNpcHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingNameProperty_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"other\": \"x\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => StateNpcHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }
}
