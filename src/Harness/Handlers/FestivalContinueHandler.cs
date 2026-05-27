using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>festival.continue</c>. Advances the active festival through Stardew's own continuation path.</summary>
public static class FestivalContinueHandler
{
    public const string Method = "festival.continue";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvFestivalContinueWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IFestivalContinueWorld world)
    {
        var ev = world.ActiveEvent
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} requires an active event");
        if (!world.IsFestival)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"{Method} requires an active festival event");

        var id = world.ReadEventId(ev);
        world.ContinueFestival(ev);
        return ProtocolJson.ToElement(new FestivalContinueResult
        {
            Tick = world.Tick,
            Id = id,
            IsFestival = true,
        });
    }
}

internal interface IFestivalContinueWorld
{
    object? ActiveEvent { get; }
    int Tick { get; }
    bool IsFestival { get; }
    string ReadEventId(object ev);
    void ContinueFestival(object ev);
}

internal sealed class SdvFestivalContinueWorld : IFestivalContinueWorld
{
    public object? ActiveEvent => Game1.CurrentEvent ?? Game1.currentLocation?.currentEvent;
    public int Tick => Game1.ticks;
    public bool IsFestival => ActiveEvent is StardewValley.Event { isFestival: true };

    public string ReadEventId(object ev)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return ev.GetType().GetField("id", flags)?.GetValue(ev) as string ?? string.Empty;
    }

    public void ContinueFestival(object ev)
    {
        if (ev is not StardewValley.Event sdvEvent)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"{FestivalContinueHandler.Method} resolved an invalid event object");

        sdvEvent.forceFestivalContinue();
    }
}
