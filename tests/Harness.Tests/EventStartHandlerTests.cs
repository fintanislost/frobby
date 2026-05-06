using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class EventStartHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => EventStartHandler.Handle(null, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_MissingId_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"location\":\"BusStop\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => EventStartHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params.id", ex.Message);
    }

    [Fact]
    public void Handle_EventNotFound_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"missing\",\"location\":\"BusStop\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => EventStartHandler.Handle(p, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("event not found", ex.Message);
    }

    [Fact]
    public void Handle_FoundEvent_StartsEventAndReturnsLocation()
    {
        var p = JsonDocument.Parse("{\"id\":\"520702\",\"location\":\"BusStop\"}").RootElement;
        var world = new FakeWorld { FoundEvent = new object(), Tick = 123 };

        var result = EventStartHandler.Handle(p, world);

        Assert.Equal("520702", world.RequestedId);
        Assert.Equal("BusStop", world.RequestedLocation);
        Assert.Same(world.FoundEvent, world.StartedEvent);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(123, result.GetProperty("tick").GetInt32());
        Assert.Equal("520702", result.GetProperty("id").GetString());
        Assert.Equal("BusStop", result.GetProperty("location").GetString());
    }

    private sealed class FakeWorld : IEventStartWorld
    {
        public object? FoundEvent { get; set; }
        public object? StartedEvent { get; private set; }
        public string? RequestedId { get; private set; }
        public string? RequestedLocation { get; private set; }
        public int Tick { get; set; }

        public string CurrentLocationName => "Farm";

        public object? FindEvent(string id, string? location)
        {
            RequestedId = id;
            RequestedLocation = location;
            return FoundEvent;
        }

        public string ResolveLocationName(string? location)
            => location ?? CurrentLocationName;

        public void StartEvent(object ev)
            => StartedEvent = ev;
    }
}
