using System;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>festival.finish_grange_judging</c>. Applies vanilla Stardew Fair grange results.</summary>
public static class FestivalFinishGrangeJudgingHandler
{
    public const string Method = "festival.finish_grange_judging";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvGrangeJudgingWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IGrangeJudgingWorld world)
    {
        var ev = world.ActiveEvent
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} requires an active event");
        var id = world.ReadEventId(ev);
        if (!string.Equals(id, "festival_fall16", StringComparison.Ordinal))
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} requires the active Stardew Fair festival");

        world.JudgeGrange(ev);
        world.InterpretGrangeResults(ev);
        return ProtocolJson.ToElement(new FinishGrangeJudgingResult
        {
            Tick = world.Tick,
            Id = id,
            GrangeScore = world.GrangeScore,
            GrangeJudged = world.GrangeJudged,
        });
    }
}

internal interface IGrangeJudgingWorld
{
    object? ActiveEvent { get; }
    int Tick { get; }
    string ReadEventId(object ev);
    int? GrangeScore { get; }
    bool GrangeJudged { get; }
    void JudgeGrange(object ev);
    void InterpretGrangeResults(object ev);
}

internal sealed class SdvGrangeJudgingWorld : IGrangeJudgingWorld
{
    public object? ActiveEvent => Game1.CurrentEvent ?? Game1.currentLocation?.currentEvent;
    public int Tick => Game1.ticks;
    public int? GrangeScore => ReadIntField(ActiveEvent, "grangeScore");
    public bool GrangeJudged => ReadBoolField(ActiveEvent, "grangeJudged") == true;

    public string ReadEventId(object ev)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return ev.GetType().GetField("id", flags)?.GetValue(ev) as string ?? string.Empty;
    }

    public void JudgeGrange(object ev)
        => InvokeEventMethod(ev, "judgeGrange");

    public void InterpretGrangeResults(object ev)
        => InvokeEventMethod(ev, "interpretGrangeResults");

    private static void InvokeEventMethod(object ev, string methodName)
    {
        if (ev is not StardewValley.Event)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"{FestivalFinishGrangeJudgingHandler.Method} resolved an invalid event object");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        ev.GetType().GetMethod(methodName, flags, Type.EmptyTypes)?.Invoke(ev, null);
    }

    private static int? ReadIntField(object? source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return source?.GetType().GetField(name, flags)?.GetValue(source) is int value ? value : null;
    }

    private static bool? ReadBoolField(object? source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return source?.GetType().GetField(name, flags)?.GetValue(source) is bool value ? value : null;
    }
}
