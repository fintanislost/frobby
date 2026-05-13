using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>festival.start</c>. Starts the current date's active festival through Stardew festival APIs.</summary>
public static class FestivalStartHandler
{
    public const string Method = "festival.start";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvFestivalStartWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IFestivalStartWorld world)
    {
        var req = RpcParams.Optional<FestivalStartRequest>(paramsElement);
        if (req.Location is { Length: 0 })
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.location must not be empty");

        return ProtocolJson.ToElement(world.StartCurrentFestival(req.Location));
    }
}

internal sealed class FestivalStartRequest
{
    public string? Location { get; set; }
}

internal interface IFestivalStartWorld
{
    FestivalStartResult StartCurrentFestival(string? expectedLocation);
}

internal sealed class FestivalStartResult
{
    public int Tick { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsFestival { get; set; }
}

internal sealed class SdvFestivalStartWorld : IFestivalStartWorld
{
    public FestivalStartResult StartCurrentFestival(string? expectedLocation)
    {
        RpcPreconditions.RequireWorldReady();
        if (Game1.player is null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "festival.start requires a loaded player");

        var festivalId = $"{Game1.currentSeason}{Game1.dayOfMonth}";
        if (!Utility.isFestivalDay())
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"festival.start found no active festival for {festivalId}");

        if (!StardewValley.Event.tryToLoadFestivalData(
                festivalId,
                out _,
                out var data,
                out var locationName,
                out var startTime,
                out var endTime))
        {
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"festival.start could not load festival data for {festivalId}");
        }

        if (!string.IsNullOrWhiteSpace(expectedLocation)
            && !string.Equals(expectedLocation, locationName, StringComparison.Ordinal))
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"festival.start expected location {expectedLocation} but festival is in {locationName}");
        }

        if (Game1.timeOfDay < startTime || Game1.timeOfDay > endTime)
        {
            throw new JsonRpcException(
                JsonRpcErrorCode.GameStateInvalid,
                $"festival.start requires time between {startTime} and {endTime} for {festivalId}; current time is {Game1.timeOfDay}");
        }

        var location = Game1.getLocationFromName(locationName)
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, $"festival.start could not find location {locationName}");

        var assetName = $"Data/Festivals/{festivalId}";
        var setupScript = SelectFestivalSetupScript(data, Game1.year);
        var ev = new StardewValley.Event(setupScript, assetName, festivalId, Game1.player)
        {
            isFestival = true,
        };

        // Put the festival on the destination before the warp so Player.Warped observers can
        // inspect e.NewLocation.currentEvent through the same lifecycle a mod sees in-game.
        Game1.whereIsTodaysFest = locationName;
        location.currentEvent = ev;
        Game1.warpFarmer(locationName, Game1.player.TilePoint.X, Game1.player.TilePoint.Y, false);
        location.startEvent(ev);

        return new FestivalStartResult
        {
            Tick = Game1.ticks,
            Id = festivalId,
            Location = locationName,
            IsFestival = ev.isFestival,
        };
    }

    internal static string SelectFestivalSetupScript(IReadOnlyDictionary<string, string> data, int year)
    {
        for (var candidate = year; candidate >= 1; candidate--)
        {
            if (data.TryGetValue($"set-up_y{candidate}", out var yearly))
                return yearly;
        }

        if (data.TryGetValue("set-up", out var setup))
            return setup;

        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "festival.start could not find set-up script in festival data");
    }
}
