using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>event.skip</c>. Skips the currently active Stardew event/cutscene.</summary>
public static class EventSkipHandler
{
    public const string Method = "event.skip";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvEventSkipWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IEventSkipWorld world)
    {
        var ev = world.ActiveEvent
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "event.skip requires an active event");
        var id = world.ReadEventId(ev);
        world.SkipEvent(ev);

        return ProtocolJson.ToElement(new EventSkipResult
        {
            Tick = world.Tick,
            Id = id,
        });
    }
}

internal interface IEventSkipWorld
{
    object? ActiveEvent { get; }
    int Tick { get; }
    string ReadEventId(object ev);
    void SkipEvent(object ev);
}

internal sealed class SdvEventSkipWorld : IEventSkipWorld
{
    public object? ActiveEvent => Game1.CurrentEvent ?? Game1.currentLocation?.currentEvent;

    public int Tick => Game1.ticks;

    public string ReadEventId(object ev)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return ev.GetType().GetField("id", flags)?.GetValue(ev) as string ?? string.Empty;
    }

    public void SkipEvent(object ev)
    {
        if (ev is not StardewValley.Event sdvEvent)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "event.skip resolved an invalid event object");

        sdvEvent.skipEvent();
    }
}
