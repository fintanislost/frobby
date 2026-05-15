using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI.Events;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class TimeNextDayHandlerTests
{
    [Fact]
    public void Handle_NoActiveScenario_RejectsBeforeTransition()
    {
        var world = ReadyWorld();
        world.IsScenarioActive = false;

        var ex = Assert.Throws<JsonRpcException>(() =>
            TimeNextDayHandler.Handle(null, world, new DeterministicTimeNextDayTransition()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("active scenario", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, world.TransitionCalls);
    }

    [Fact]
    public void Handle_WorldNotReady_RejectsBeforeTransition()
    {
        var world = ReadyWorld();
        world.IsWorldReady = false;

        var ex = Assert.Throws<JsonRpcException>(() =>
            TimeNextDayHandler.Handle(null, world, new DeterministicTimeNextDayTransition()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("loaded world", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, world.TransitionCalls);
    }

    [Theory]
    [InlineData(nameof(FakeTimeNextDayWorld.IsMenuOpen), "menu")]
    [InlineData(nameof(FakeTimeNextDayWorld.IsMinigameActive), "minigame")]
    [InlineData(nameof(FakeTimeNextDayWorld.IsEventActive), "event")]
    [InlineData(nameof(FakeTimeNextDayWorld.IsWarping), "warp")]
    public void Handle_BlockedGameModes_RejectBeforeTransition(string propertyName, string expectedMessage)
    {
        var world = ReadyWorld();
        typeof(FakeTimeNextDayWorld).GetProperty(propertyName)!.SetValue(world, true);

        var ex = Assert.Throws<JsonRpcException>(() =>
            TimeNextDayHandler.Handle(null, world, new DeterministicTimeNextDayTransition()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains(expectedMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, world.TransitionCalls);
    }

    [Fact]
    public void Handle_ValidRequest_FiresDayCallbacksInOrderAndReturnsPostTransitionDate()
    {
        var world = ReadyWorld();
        world.Tick = 90123;
        world.Year = 1;
        world.Season = "spring";
        world.DayOfMonth = 28;
        world.TimeOfDay = 2200;

        var json = TimeNextDayHandler.Handle(null, world, new DeterministicTimeNextDayTransition());
        var result = JsonSerializer.Deserialize<TimeNextDayResult>(json.GetRawText(), ProtocolJson.Options)!;

        Assert.Equal(new[] { "day-ending", "day-started" }, world.Callbacks);
        Assert.Equal(1, world.TransitionCalls);
        Assert.True(result.Ok);
        Assert.Equal(90123, result.Tick);
        Assert.Equal(1, result.Year);
        Assert.Equal("summer", result.Season);
        Assert.Equal(1, result.DayOfMonth);
        Assert.Equal(600, result.TimeOfDay);
    }

    [Fact]
    public void ProductionTransition_UsesDeterministicFallback()
    {
        var field = typeof(TimeNextDayHandler).GetField(
            "ProductionTransition",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.IsType<DeterministicTimeNextDayTransition>(field.GetValue(null));
    }

    [Fact]
    public void DeterministicTransition_MarksRaisesMutatesAndRaisesStartedInOrder()
    {
        var world = ReadyWorld();
        world.Year = 1;
        world.Season = "winter";
        world.DayOfMonth = 28;
        world.TimeOfDay = 2500;

        new DeterministicTimeNextDayTransition().Advance(world);

        Assert.Equal(new[]
        {
            "transition-started",
            "trigger-day-ending",
            "day-ending",
            "date:2/spring/1 time:600",
            "trigger-day-started",
            "day-started",
        }, world.Events);
    }

    [Fact]
    public void DeterministicTransition_InvalidCurrentDate_ThrowsBeforeAnyObservableCallbacks()
    {
        var world = ReadyWorld();
        world.Season = "autumn";
        world.DayOfMonth = 28;
        world.TimeOfDay = 2200;

        var ex = Assert.Throws<ArgumentException>(() =>
            new DeterministicTimeNextDayTransition().Advance(world));

        Assert.Contains("season", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, world.TransitionCalls);
        Assert.Empty(world.Callbacks);
        Assert.Empty(world.Events);
        Assert.Equal("autumn", world.Season);
        Assert.Equal(28, world.DayOfMonth);
        Assert.Equal(2200, world.TimeOfDay);
    }

    [Theory]
    [InlineData(1, "spring", 1, 1, "spring", 2)]
    [InlineData(1, "spring", 28, 1, "summer", 1)]
    [InlineData(1, "summer", 28, 1, "fall", 1)]
    [InlineData(1, "fall", 28, 1, "winter", 1)]
    [InlineData(1, "winter", 28, 2, "spring", 1)]
    public void CalendarProjection_UsesSdvSeasonAndYearRollover(
        int year,
        string season,
        int dayOfMonth,
        int expectedYear,
        string expectedSeason,
        int expectedDayOfMonth)
    {
        var next = TimeNextDayCalendar.Next(year, season, dayOfMonth);

        Assert.Equal(expectedYear, next.Year);
        Assert.Equal(expectedSeason, next.Season);
        Assert.Equal(expectedDayOfMonth, next.DayOfMonth);
    }

    [Fact]
    public void SmapiEventSink_RaisesDayEndingAndDayStartedExactlyOnce()
    {
        var dayEnding = new OverloadedManagedEvent<DayEndingEventArgs>("day-ending");
        var dayStarted = new OverloadedManagedEvent<DayStartedEventArgs>("day-started");
        var log = new List<string>();
        dayEnding.Raised += log.Add;
        dayStarted.Raised += log.Add;
        var sink = new SmapiTimeNextDayEventSink(name => name switch
        {
            "DayEnding" => dayEnding,
            "DayStarted" => dayStarted,
            _ => throw new InvalidOperationException(name),
        });

        sink.RaiseDayEnding();
        sink.RaiseDayStarted();

        Assert.Equal(1, dayEnding.Count);
        Assert.Equal(1, dayStarted.Count);
        Assert.Equal(new[] { "day-ending", "day-started" }, log);
    }

    [Fact]
    public void SmapiEventSink_AmbiguousRaise_ThrowsClearError()
    {
        var sink = new SmapiTimeNextDayEventSink(_ => new AmbiguousManagedEvent());

        var ex = Assert.Throws<InvalidOperationException>(() => sink.RaiseDayStarted());

        Assert.Contains(nameof(AmbiguousManagedEvent), ex.Message);
        Assert.Contains(nameof(DayStartedEventArgs), ex.Message);
        Assert.Contains("multiple public instance Raise", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FakeTimeNextDayWorld ReadyWorld()
        => new()
        {
            IsScenarioActive = true,
            IsWorldReady = true,
            Year = 1,
            Season = "spring",
            DayOfMonth = 1,
            TimeOfDay = 600,
        };

    private sealed class FakeTimeNextDayWorld : ITimeNextDayWorld
    {
        private int _timeOfDay;

        public bool IsScenarioActive { get; set; }
        public bool IsWorldReady { get; set; }
        public bool IsMenuOpen { get; set; }
        public bool IsMinigameActive { get; set; }
        public bool IsEventActive { get; set; }
        public bool IsWarping { get; set; }
        public int Tick { get; set; }
        public int Year { get; set; }
        public string Season { get; set; } = "spring";
        public int DayOfMonth { get; set; }
        public int TimeOfDay
        {
            get => _timeOfDay;
            set
            {
                _timeOfDay = value;
                if (TransitionCalls > 0 && value == 600)
                    Events.Add($"date:{Year}/{Season}/{DayOfMonth} time:{TimeOfDay}");
            }
        }

        public int TransitionCalls { get; set; }
        public List<string> Callbacks { get; } = new();
        public List<string> Events { get; } = new();

        public void MarkTransitionStarted()
        {
            TransitionCalls++;
            Events.Add("transition-started");
        }

        public void NotifyDayEnding()
        {
            Callbacks.Add("day-ending");
            Events.Add("day-ending");
        }

        public void RaiseDayEndingTriggerActions()
            => Events.Add("trigger-day-ending");

        public void RaiseDayStartedTriggerActions()
            => Events.Add("trigger-day-started");

        public void NotifyDayStarted()
        {
            Callbacks.Add("day-started");
            Events.Add("day-started");
        }
    }

    public sealed class OverloadedManagedEvent<TArgs> where TArgs : EventArgs
    {
        private readonly string _message;

        public OverloadedManagedEvent(string message)
        {
            _message = message;
        }

        public event Action<string>? Raised;
        public int Count { get; private set; }

        public void Raise() => throw new InvalidOperationException("wrong overload");
        public void Raise(string value) => throw new InvalidOperationException("wrong overload");
        public void Raise(TArgs args)
        {
            Count++;
            Raised?.Invoke(_message);
        }
    }

    public sealed class AmbiguousManagedEvent
    {
        public void Raise(EventArgs args) { }
        public void Raise(DayStartedEventArgs args) { }
    }
}
