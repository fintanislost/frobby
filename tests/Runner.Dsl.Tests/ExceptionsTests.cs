using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class ExceptionsTests
{
    [Fact]
    public void Create_GameStateInvalid_ReturnsTypedSubclass()
    {
        var err = new JsonRpcError(JsonRpcErrorCode.GameStateInvalid, "not frozen");
        var ex = SdvRpcException.Create("freeze.begin", err);

        Assert.IsType<SdvGameStateInvalidException>(ex);
        Assert.Equal("freeze.begin", ex.Method);
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("freeze.begin", ex.Message);
        Assert.Contains("not frozen", ex.Message);
    }

    [Fact]
    public void Create_UnknownCode_ReturnsBaseException()
    {
        // A code not in our common-subclasses list falls through to SdvRpcException base.
        var err = new JsonRpcError((JsonRpcErrorCode)(-99999), "custom");
        var ex = SdvRpcException.Create("weird.method", err);

        Assert.IsType<SdvRpcException>(ex);
        Assert.Equal("weird.method", ex.Method);
    }
}
