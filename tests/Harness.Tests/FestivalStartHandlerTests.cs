using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class FestivalStartHandlerTests
{
    [Fact]
    public void Handle_ReturnsFestivalStartResult()
    {
        var world = new FakeFestivalStartWorld
        {
            Result = new FestivalStartResult
            {
                Tick = 123,
                Id = "fall27",
                Location = "Town",
                IsFestival = true,
            },
        };
        var p = JsonDocument.Parse("{\"location\":\"Town\"}").RootElement;

        var result = FestivalStartHandler.Handle(p, world);

        Assert.Equal("Town", world.ExpectedLocation);
        Assert.Equal(123, result.GetProperty("tick").GetInt32());
        Assert.Equal("fall27", result.GetProperty("id").GetString());
        Assert.Equal("Town", result.GetProperty("location").GetString());
        Assert.True(result.GetProperty("is_festival").GetBoolean());
    }

    [Fact]
    public void Handle_RejectsEmptyLocation()
    {
        var world = new FakeFestivalStartWorld();
        var p = JsonDocument.Parse("{\"location\":\"\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => FestivalStartHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params.location must not be empty", ex.Message);
    }

    [Fact]
    public void Handle_RejectsMismatchedExpectedLocation()
    {
        var world = new FakeFestivalStartWorld
        {
            ErrorCode = JsonRpcErrorCode.GameStateInvalid,
            ErrorMessage = "festival.start expected location Town but festival is in Forest",
        };
        var p = JsonDocument.Parse("{\"location\":\"Town\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => FestivalStartHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("expected location Town", ex.Message);
    }

    private sealed class FakeFestivalStartWorld : IFestivalStartWorld
    {
        public string? ExpectedLocation { get; private set; }
        public FestivalStartResult Result { get; set; } = new();
        public JsonRpcErrorCode? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public FestivalStartResult StartCurrentFestival(string? expectedLocation)
        {
            ExpectedLocation = expectedLocation;
            if (ErrorCode is { } code)
                throw new JsonRpcException(code, ErrorMessage ?? "festival.start failed");

            return Result;
        }
    }
}
