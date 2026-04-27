using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for the <c>world.set_weather</c> RPC method. Sets the current location's
/// weather to one of the documented weather types. Runs on the game thread.
/// </summary>
/// <remarks>
/// SDV 1.6 gates weather per location-context via <c>Game1.netWorldState.Value.GetWeatherForLocation</c>.
/// This handler updates the current context's weather; other contexts are unchanged. A
/// single <c>Game1.updateWeather</c> is triggered to apply the visual effects immediately.
/// </remarks>
public static class WorldSetWeatherHandler
{
    public const string Method = "world.set_weather";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<WeatherRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Type))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.type required");

        string weatherId = req.Type.ToLowerInvariant() switch
        {
            "sun" => "Sun",
            "rain" => "Rain",
            "storm" => "Storm",
            "snow" => "Snow",
            "wind" => "Wind",
            "festival" => "Festival",
            _ => throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unknown weather type: {req.Type}"),
        };

        RpcPreconditions.RequireWorldReady();

        var state = Game1.netWorldState.Value;
        var contextId = Game1.currentLocation?.GetLocationContextId() ?? "Default";
        state.GetWeatherForLocation(contextId).Weather = weatherId;
        Game1.updateWeather(Game1.currentGameTime);

        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
