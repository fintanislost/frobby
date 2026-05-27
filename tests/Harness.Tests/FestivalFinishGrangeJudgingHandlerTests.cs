using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FestivalFinishGrangeJudgingHandlerTests
{
    [Fact]
    public void Handle_NonFairFestival_ThrowsGameStateInvalid()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            FestivalFinishGrangeJudgingHandler.Handle(null, new FakeGrangeJudgingWorld { EventId = "festival_spring13" }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_JudgesAndInterpretsActiveFair()
    {
        var world = new FakeGrangeJudgingWorld();

        var json = FestivalFinishGrangeJudgingHandler.Handle(null, world);
        var result = JsonSerializer.Deserialize<FinishGrangeJudgingResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(1234, result.Tick);
        Assert.Equal("festival_fall16", result.Id);
        Assert.True(world.Judged);
        Assert.True(world.Interpreted);
    }

    private sealed class FakeGrangeJudgingWorld : IGrangeJudgingWorld
    {
        public object? ActiveEvent { get; init; } = new();
        public int Tick => 1234;
        public string EventId { get; init; } = "festival_fall16";
        public int? GrangeScore => 42;
        public bool GrangeJudged => Interpreted;
        public bool Judged { get; private set; }
        public bool Interpreted { get; private set; }

        public string ReadEventId(object ev) => EventId;
        public void JudgeGrange(object ev) => Judged = true;
        public void InterpretGrangeResults(object ev) => Interpreted = true;
    }
}
