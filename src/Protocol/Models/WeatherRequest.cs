namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape of <c>world.set_weather</c>. Field names deserialize from snake_case via <see cref="Json.ProtocolJson.Options"/>.</summary>
public sealed class WeatherRequest
{
    /// <summary>Weather type — one of <c>sun</c>, <c>rain</c>, <c>storm</c>, <c>snow</c>, <c>wind</c>, <c>festival</c>.</summary>
    public string Type { get; set; } = string.Empty;
}
