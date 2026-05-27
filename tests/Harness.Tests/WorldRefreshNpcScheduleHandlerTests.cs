using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldRefreshNpcScheduleHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var world = new FakeWorldRefreshNpcScheduleWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldRefreshNpcScheduleHandler.Handle(null, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"\"}")]
    [InlineData("{\"name\":\"   \"}")]
    public void Handle_InvalidName_ThrowsInvalidParams(string json)
    {
        var p = JsonDocument.Parse(json).RootElement;
        var world = new FakeWorldRefreshNpcScheduleWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldRefreshNpcScheduleHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_WorldNotReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"name\":\"Claire\"}").RootElement;
        var world = new FakeWorldRefreshNpcScheduleWorld { IsWorldReady = false };

        var ex = Assert.Throws<JsonRpcException>(() => WorldRefreshNpcScheduleHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("loaded world", ex.Message);
    }

    [Fact]
    public void Handle_UnknownNpc_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"name\":\"Missing\"}").RootElement;
        var world = new FakeWorldRefreshNpcScheduleWorld();

        var ex = Assert.Throws<JsonRpcException>(() => WorldRefreshNpcScheduleHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("no NPC named", ex.Message);
    }

    [Fact]
    public void Handle_ValidRequest_RefreshesScheduleAndReturnsLocation()
    {
        var p = JsonDocument.Parse("{\"name\":\"Claire\",\"schedule_key\":\"Thu\"}").RootElement;
        var npc = new object();
        var world = new FakeWorldRefreshNpcScheduleWorld
        {
            Npc = npc,
            RawSchedule = "0 Custom_Claire_WarpRoom 1 3 3/650 Town 92 44 2/850 MovieTheater 7 5 2 Claire_Blink \"Strings\\schedules\\Claire:MovieTheater.002\"/2130 BusStop 22 9 0",
        };

        var result = WorldRefreshNpcScheduleHandler.Handle(p, world);

        Assert.Equal(new[]
        {
            "find:Claire",
            "refresh:Claire:4:900:Thu",
            "raw:Claire:Thu",
            "place:Claire:MovieTheater:7:5:Claire_Blink:Strings\\schedules\\Claire:MovieTheater.002",
            "dialogue:Claire:Strings\\schedules\\Claire:MovieTheater.002",
            "project:Claire",
        }, world.Calls);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(123, result.GetProperty("tick").GetInt32());
        Assert.Equal("MovieTheater", result.GetProperty("location").GetString());
        Assert.Equal(7, result.GetProperty("tile").GetProperty("x").GetInt32());
        Assert.Equal(5, result.GetProperty("tile").GetProperty("y").GetInt32());
    }

    [Theory]
    [InlineData(600, "Custom_Claire_WarpRoom", 1, 3)]
    [InlineData(700, "Town", 92, 44)]
    [InlineData(900, "MovieTheater", 7, 5)]
    [InlineData(2200, "BusStop", 22, 9)]
    public void TryPickSchedulePlacement_SelectsLatestPastRoute(
        int timeOfDay,
        string expectedLocation,
        int expectedX,
        int expectedY)
    {
        var raw = "0 Custom_Claire_WarpRoom 1 3 3/650 Town 92 44 2/850 MovieTheater 7 5 2 Claire_Blink \"Strings\\schedules\\Claire:MovieTheater.002\"/2130 BusStop 22 9 0";

        var found = SchedulePlacementParser.TryPick(raw, timeOfDay, out var placement);

        Assert.True(found);
        Assert.Equal(expectedLocation, placement.Location);
        Assert.Equal(expectedX, placement.X);
        Assert.Equal(expectedY, placement.Y);
    }

    [Fact]
    public void TryPickSchedulePlacement_CapturesOptionalBehaviorAndMessage()
    {
        var raw = "850 MovieTheater 7 5 2 Claire_Blink \"Strings\\schedules\\Claire:MovieTheater.002\"";

        var found = SchedulePlacementParser.TryPick(raw, 900, out var placement);

        Assert.True(found);
        Assert.Equal("Claire_Blink", placement.EndBehavior);
        Assert.Equal("Strings\\schedules\\Claire:MovieTheater.002", placement.EndMessage);
    }

    [Fact]
    public void TryPickSchedulePlacement_IgnoresMalformedSegments()
    {
        var found = SchedulePlacementParser.TryPick("bad/850 MovieTheater 7 5 2", 900, out var placement);

        Assert.True(found);
        Assert.Equal("MovieTheater", placement.Location);
        Assert.Equal(7, placement.X);
        Assert.Equal(5, placement.Y);
    }

    private sealed class FakeWorldRefreshNpcScheduleWorld : IWorldRefreshNpcScheduleWorld
    {
        public List<string> Calls { get; } = new();
        public object? Npc { get; set; }
        public string? RawSchedule { get; set; }
        public bool IsWorldReady { get; set; } = true;
        public int Tick => 123;
        public int DayOfMonth => 4;
        public int TimeOfDay => 900;

        public object? FindNpc(string name)
        {
            Calls.Add($"find:{name}");
            return Npc;
        }

        public void RefreshSchedule(object npc, string name, int dayOfMonth, int timeOfDay, string? scheduleKey)
            => Calls.Add($"refresh:{name}:{dayOfMonth}:{timeOfDay}:{scheduleKey}");

        public string? GetRawSchedule(object npc, string name, string? scheduleKey)
        {
            Calls.Add($"raw:{name}:{scheduleKey}");
            return RawSchedule;
        }

        public void PlaceNpc(object npc, string name, SchedulePlacement placement)
            => Calls.Add($"place:{name}:{placement.Location}:{placement.X}:{placement.Y}:{placement.EndBehavior}:{placement.EndMessage}");

        public void ApplyRouteDialogue(object npc, string name, SchedulePlacement placement)
            => Calls.Add($"dialogue:{name}:{placement.EndMessage}");

        public RefreshedNpcScheduleState Project(object npc, string name)
        {
            Calls.Add($"project:{name}");
            return new RefreshedNpcScheduleState("MovieTheater", 7, 5);
        }
    }
}
