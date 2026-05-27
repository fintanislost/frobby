using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>festival.start</c>. Starts the current date's active festival through Stardew festival APIs.</summary>
public static class FestivalStartHandler
{
    public const string Method = "festival.start";
    internal static PendingFestivalAdditionalActors? PendingAdditionalActors { get; set; }

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

internal sealed class FestivalAdditionalActor
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int FacingDirection { get; set; }
}

internal sealed class PendingFestivalAdditionalActors
{
    public string FestivalId { get; set; } = string.Empty;
    public int Year { get; set; }
}

internal sealed class SdvFestivalStartWorld : IFestivalStartWorld
{
    private static readonly MethodInfo? AddActorMethod = typeof(StardewValley.Event).GetMethod(
        "addActor",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(GameLocation) },
        modifiers: null);
    private static readonly FieldInfo? FestivalDataAssetNameField = typeof(StardewValley.Event).GetField(
        "festivalDataAssetName",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FestivalDataField = typeof(StardewValley.Event).GetField(
        "festivalData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ActorPositionsAfterMoveField = typeof(StardewValley.Event).GetField(
        "actorPositionsAfterMove",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PreviousAmbientLightField = typeof(StardewValley.Event).GetField(
        "previousAmbientLight",
        BindingFlags.Instance | BindingFlags.NonPublic);

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
                out var assetName,
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

        if (!StardewValley.Event.tryToLoadFestival(festivalId, out var ev) || ev is null)
        {
            ev = CreateFestivalEvent(festivalId, assetName, data);
        }

        ev.isFestival = true;

        // Put the festival on the destination before the warp so Player.Warped observers can
        // inspect e.NewLocation.currentEvent through the same lifecycle a mod sees in-game.
        Game1.whereIsTodaysFest = locationName;
        location.currentEvent = ev;
        Game1.warpFarmer(locationName, Game1.player.TilePoint.X, Game1.player.TilePoint.Y, false);
        location.startEvent(ev);
        FestivalStartHandler.PendingAdditionalActors = new PendingFestivalAdditionalActors
        {
            FestivalId = festivalId,
            Year = Game1.year,
        };

        return new FestivalStartResult
        {
            Tick = Game1.ticks,
            Id = festivalId,
            Location = locationName,
            IsFestival = ev.isFestival,
        };
    }

    private static StardewValley.Event CreateFestivalEvent(
        string festivalId,
        string assetName,
        IReadOnlyDictionary<string, string> data)
    {
        var festivalData = new Dictionary<string, string>(data)
        {
            ["file"] = festivalId,
        };
        var ev = new StardewValley.Event
        {
            id = $"festival_{festivalId}",
            isFestival = true,
            eventCommands = StardewValley.Event.ParseCommands(SelectFestivalSetupScript(festivalData, Game1.year)),
        };

        SetPrivateField(FestivalDataAssetNameField, ev, assetName, "festivalDataAssetName");
        SetPrivateField(FestivalDataField, ev, festivalData, "festivalData");
        SetPrivateField(
            ActorPositionsAfterMoveField,
            ev,
            new Dictionary<string, Vector3>(),
            "actorPositionsAfterMove");
        SetPrivateField(PreviousAmbientLightField, ev, Game1.ambientLight, "previousAmbientLight");
        Game1.player.festivalScore = 0;
        return ev;
    }

    private static void SetPrivateField(FieldInfo? field, StardewValley.Event ev, object value, string fieldName)
    {
        if (field is null)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, $"festival.start could not find Event.{fieldName}");

        field.SetValue(ev, value);
    }

    internal static void ApplyPendingAdditionalActors()
    {
        if (FestivalStartHandler.PendingAdditionalActors is not { } pending)
            return;

        var location = Game1.currentLocation;
        var ev = Game1.CurrentEvent ?? location?.currentEvent;
        if (location is null || ev is null || !ev.isFestival)
            return;

        var data = Game1.content.Load<Dictionary<string, string>>($"Data/Festivals/{pending.FestivalId}");
        AddFestivalAdditionalActors(ev, location, data, pending.Year);
        FestivalStartHandler.PendingAdditionalActors = null;
    }

    internal static string? SelectFestivalAdditionalActorData(IReadOnlyDictionary<string, string> data, int year)
        => SelectFestivalDataForYear(data, "Set-Up_additionalCharacters", year);

    internal static IReadOnlyList<FestivalAdditionalActor> ParseFestivalAdditionalActors(string? data)
    {
        var actors = new List<FestivalAdditionalActor>();
        if (string.IsNullOrWhiteSpace(data))
            return actors;

        foreach (var entry in data.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4
                || !int.TryParse(parts[1], out var x)
                || !int.TryParse(parts[2], out var y)
                || !TryParseFacingDirection(parts[3], out var facingDirection))
            {
                continue;
            }

            actors.Add(new FestivalAdditionalActor
            {
                Name = parts[0].TrimEnd('?'),
                X = x,
                Y = y,
                FacingDirection = facingDirection,
            });
        }

        return actors;
    }

    private static void AddFestivalAdditionalActors(
        StardewValley.Event ev,
        GameLocation location,
        IReadOnlyDictionary<string, string> data,
        int year)
    {
        var actorData = SelectFestivalAdditionalActorData(data, year);
        var actors = ParseFestivalAdditionalActors(actorData);
        foreach (var actor in actors)
        {
            if (ev.getActorByName(actor.Name) is null)
            {
                if (AddActorMethod is null)
                    throw new JsonRpcException(JsonRpcErrorCode.InternalError, "festival.start could not find Event.addActor");

                AddActorMethod.Invoke(ev, new object[] { actor.Name, actor.X, actor.Y, actor.FacingDirection, location });
            }
        }
    }

    private static bool TryParseFacingDirection(string value, out int facingDirection)
    {
        if (int.TryParse(value, out facingDirection))
            return true;

        facingDirection = value.ToLowerInvariant() switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => -1,
        };

        return facingDirection >= 0;
    }

    internal static string SelectFestivalSetupScript(IReadOnlyDictionary<string, string> data, int year)
    {
        if (SelectFestivalDataForYear(data, "set-up", year) is { } setup)
            return setup;

        throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "festival.start could not find set-up script in festival data");
    }

    private static string? SelectFestivalDataForYear(IReadOnlyDictionary<string, string> data, string key, int year)
    {
        var variantCount = 1;
        while (data.ContainsKey($"{key}_y{variantCount + 1}"))
            variantCount++;

        var variant = year % variantCount;
        if (variant == 0)
            variant = variantCount;

        var actualKey = variant > 1 ? $"{key}_y{variant}" : key;
        return data.TryGetValue(actualKey, out var value) ? value : null;
    }
}
