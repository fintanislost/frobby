using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Triggers;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>time.next_day</c>. Runs Frobby's deterministic scenario day transition
/// instead of directly writing <see cref="Game1.dayOfMonth"/> like <c>time.set</c>.
/// </summary>
public static class TimeNextDayHandler
{
    public const string Method = "time.next_day";

    private static readonly ITimeNextDayWorld ProductionWorld = new SdvTimeNextDayWorld();
    private static readonly ITimeNextDayTransition ProductionTransition = new DeterministicTimeNextDayTransition();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld, ProductionTransition);

    internal static JsonElement Handle(
        JsonElement? paramsElement,
        ITimeNextDayWorld world,
        ITimeNextDayTransition transition)
    {
        if (!world.IsScenarioActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "time.next_day requires an active scenario (call scenario.begin first)");

        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "time.next_day requires a loaded world");

        if (world.IsMenuOpen)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "time.next_day requires no active menu");

        if (world.IsMinigameActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "time.next_day requires no active minigame");

        if (world.IsEventActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "time.next_day requires no active event");

        if (world.IsWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "time.next_day requires no active warp");

        transition.Advance(world);

        return ProtocolJson.ToElement(new TimeNextDayResult
        {
            Tick = world.Tick,
            Year = world.Year,
            Season = world.Season,
            DayOfMonth = world.DayOfMonth,
            TimeOfDay = world.TimeOfDay,
        });
    }

}

internal interface ITimeNextDayTransition
{
    void Advance(ITimeNextDayWorld world);
}

internal interface ITimeNextDayWorld
{
    bool IsScenarioActive { get; }
    bool IsWorldReady { get; }
    bool IsMenuOpen { get; }
    bool IsMinigameActive { get; }
    bool IsEventActive { get; }
    bool IsWarping { get; }
    int Tick { get; }
    int Year { get; set; }
    string Season { get; set; }
    int DayOfMonth { get; set; }
    int TimeOfDay { get; set; }

    void MarkTransitionStarted();
    void RaiseDayEndingTriggerActions();
    void NotifyDayEnding();
    void RaiseDayStartedTriggerActions();
    void NotifyDayStarted();
}

internal sealed record TimeNextDayDate(int Year, string Season, int DayOfMonth);

internal static class TimeNextDayCalendar
{
    public static TimeNextDayDate Next(int year, string season, int dayOfMonth)
    {
        if (dayOfMonth < 1 || dayOfMonth > 28)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "SDV months are 1-28");

        if (dayOfMonth < 28)
            return new TimeNextDayDate(year, season, dayOfMonth + 1);

        return season.ToLowerInvariant() switch
        {
            "spring" => new TimeNextDayDate(year, "summer", 1),
            "summer" => new TimeNextDayDate(year, "fall", 1),
            "fall" => new TimeNextDayDate(year, "winter", 1),
            "winter" => new TimeNextDayDate(year + 1, "spring", 1),
            _ => throw new ArgumentException($"Unknown SDV season '{season}'", nameof(season)),
        };
    }
}

internal sealed class DeterministicTimeNextDayTransition : ITimeNextDayTransition
{
    public void Advance(ITimeNextDayWorld world)
    {
        var next = TimeNextDayCalendar.Next(world.Year, world.Season, world.DayOfMonth);

        world.MarkTransitionStarted();
        world.RaiseDayEndingTriggerActions();
        world.NotifyDayEnding();

        world.Year = next.Year;
        world.Season = next.Season;
        world.DayOfMonth = next.DayOfMonth;
        world.TimeOfDay = 600;

        world.RaiseDayStartedTriggerActions();
        world.NotifyDayStarted();
    }
}

internal interface ITimeNextDayEventSink
{
    void RaiseDayEnding();
    void RaiseDayStarted();
}

internal sealed class SmapiTimeNextDayEventSink : ITimeNextDayEventSink
{
    private readonly Func<string, object> _getManagedEvent;

    public SmapiTimeNextDayEventSink(IModHelper helper)
        : this(eventName =>
        {
            object gameLoopEvents = helper.Events.GameLoop;
            object eventManager = GetFieldFromHierarchy(gameLoopEvents, "EventManager");
            return GetFieldFromHierarchy(eventManager, eventName);
        }) { }

    internal SmapiTimeNextDayEventSink(Func<string, object> getManagedEvent)
    {
        _getManagedEvent = getManagedEvent;
    }

    public void RaiseDayEnding()
        => Raise("DayEnding", typeof(DayEndingEventArgs));

    public void RaiseDayStarted()
        => Raise("DayStarted", typeof(DayStartedEventArgs));

    private void Raise(string eventName, Type argsType)
    {
        object managedEvent = _getManagedEvent(eventName);
        object args = Activator.CreateInstance(argsType)
            ?? throw new InvalidOperationException($"Could not create {argsType.FullName}");
        MethodInfo raise = FindRaiseMethod(managedEvent.GetType(), argsType);
        raise.Invoke(managedEvent, new[] { args });
    }

    private static MethodInfo FindRaiseMethod(Type managedEventType, Type argsType)
    {
        var candidates = managedEventType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "Raise")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(argsType);
            })
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0];

        string message = candidates.Length == 0
            ? "no public instance Raise overload"
            : "multiple public instance Raise overloads";
        throw new InvalidOperationException(
            $"Could not select SMAPI event Raise method: {message} on {managedEventType.FullName} can accept {argsType.FullName}.");
    }

    private static object GetFieldFromHierarchy(object instance, string fieldName)
    {
        Type? type = instance.GetType();
        while (type != null)
        {
            FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(instance)
                    ?? throw new InvalidOperationException($"{type.FullName}.{fieldName} was null");

            type = type.BaseType;
        }

        throw new MissingFieldException(instance.GetType().FullName, fieldName);
    }
}

internal sealed class SdvTimeNextDayWorld : ITimeNextDayWorld
{
    public static ITimeNextDayEventSink EventSink { get; set; } = new NoopTimeNextDayEventSink();

    public bool IsScenarioActive => ScenarioState.Current.IsActive;
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public bool IsMenuOpen => Game1.activeClickableMenu != null;
    public bool IsMinigameActive => Game1.currentMinigame != null;
    public bool IsEventActive => Game1.eventUp;
    public bool IsWarping => Game1.isWarping;
    public int Tick => Game1.ticks;

    public int Year
    {
        get => Game1.year;
        set => Game1.year = value;
    }

    public string Season
    {
        get => Game1.season.ToString().ToLowerInvariant();
        set => Game1.season = value.ToLowerInvariant() switch
        {
            "spring" => StardewValley.Season.Spring,
            "summer" => StardewValley.Season.Summer,
            "fall" => StardewValley.Season.Fall,
            "winter" => StardewValley.Season.Winter,
            _ => throw new ArgumentException($"Unknown SDV season '{value}'", nameof(value)),
        };
    }

    public int DayOfMonth
    {
        get => Game1.dayOfMonth;
        set => Game1.dayOfMonth = value;
    }

    public int TimeOfDay
    {
        get => Game1.timeOfDay;
        set => Game1.timeOfDay = value;
    }

    public void MarkTransitionStarted() { }

    public void RaiseDayEndingTriggerActions()
        => TriggerActionManager.Raise("DayEnding", null, null, null, null, null);

    public void NotifyDayEnding()
        => EventSink.RaiseDayEnding();

    public void RaiseDayStartedTriggerActions()
        => TriggerActionManager.Raise("DayStarted", null, null, null, null, null);

    public void NotifyDayStarted()
        => EventSink.RaiseDayStarted();
}

internal sealed class NoopTimeNextDayEventSink : ITimeNextDayEventSink
{
    public void RaiseDayEnding() { }

    public void RaiseDayStarted() { }
}
