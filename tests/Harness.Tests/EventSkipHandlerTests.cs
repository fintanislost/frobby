using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class EventSkipHandlerTests
{
    [Fact]
    public void Handle_NoActiveEvent_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() => EventSkipHandler.Handle(null, new FakeWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("active event", ex.Message);
    }

    [Fact]
    public void Handle_ActiveEvent_SkipsAndReturnsId()
    {
        var ev = new FakeEvent { Id = "520702" };
        var result = EventSkipHandler.Handle(null, new FakeWorld { ActiveEvent = ev, Tick = 321 });

        Assert.True(ev.Skipped);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(321, result.GetProperty("tick").GetInt32());
        Assert.Equal("520702", result.GetProperty("id").GetString());
    }

    private sealed class FakeWorld : IEventSkipWorld
    {
        public object? ActiveEvent { get; set; }
        public int Tick { get; set; }

        public string ReadEventId(object ev)
            => ((FakeEvent)ev).Id;

        public void SkipEvent(object ev)
            => ((FakeEvent)ev).Skipped = true;
    }

    private sealed class FakeEvent
    {
        public string Id { get; set; } = string.Empty;
        public bool Skipped { get; set; }
    }
}
