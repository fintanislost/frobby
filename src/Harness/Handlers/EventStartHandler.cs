using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>event.start</c>. Starts a location event by id through Stardew's event resolver.</summary>
public static class EventStartHandler
{
    public const string Method = "event.start";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvEventStartWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IEventStartWorld world)
    {
        var req = RpcParams.Required<EventStartRequest>(paramsElement);
        if (string.IsNullOrWhiteSpace(req.Id))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.id required");

        var ev = world.FindEvent(req.Id, req.Location);
        if (ev is null)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"event not found: {req.Id} in {world.ResolveLocationName(req.Location)}");

        world.StartEvent(ev);

        return ProtocolJson.ToElement(new EventStartResult
        {
            Tick = world.Tick,
            Id = req.Id,
            Location = world.ResolveLocationName(req.Location),
        });
    }
}

internal interface IEventStartWorld
{
    string CurrentLocationName { get; }
    int Tick { get; }
    object? FindEvent(string id, string? location);
    string ResolveLocationName(string? location);
    void StartEvent(object ev);
}

internal sealed class SdvEventStartWorld : IEventStartWorld
{
    private GameLocation? _lastResolvedLocation;

    public string CurrentLocationName
        => Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? string.Empty;

    public int Tick => Game1.ticks;

    public object? FindEvent(string id, string? location)
    {
        var resolved = ResolveLocation(location);
        if (Game1.player is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "event.start requires a loaded player");

        return resolved.findEventById(id, Game1.player);
    }

    public string ResolveLocationName(string? location)
    {
        var resolved = ResolveLocation(location);
        return resolved.NameOrUniqueName ?? resolved.Name ?? string.Empty;
    }

    public void StartEvent(object ev)
    {
        if (ev is not StardewValley.Event sdvEvent)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "event.start resolved an invalid event object");

        var location = _lastResolvedLocation
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "event.start requires a loaded location");
        location.startEvent(sdvEvent);
    }

    private GameLocation ResolveLocation(string? location)
    {
        var resolved = string.IsNullOrWhiteSpace(location)
            ? Game1.currentLocation
            : Game1.getLocationFromName(location);
        if (resolved is null)
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                string.IsNullOrWhiteSpace(location)
                    ? "event.start requires a loaded location"
                    : $"no location named: {location}");

        _lastResolvedLocation = resolved;
        return resolved;
    }
}
