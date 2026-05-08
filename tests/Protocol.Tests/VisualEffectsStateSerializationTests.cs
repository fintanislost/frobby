using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class VisualEffectsStateSerializationTests
{
    [Fact]
    public void Request_UsesSnakeCaseLocation()
    {
        var req = JsonSerializer.Deserialize<VisualEffectsRequest>(
            "{\"location\":\"Custom_GrandpasGrove\"}",
            ProtocolJson.Options)!;

        Assert.Equal("Custom_GrandpasGrove", req.Location);
    }

    [Fact]
    public void State_SerializesTemporarySpritesLightsAndWeather()
    {
        var state = new VisualEffectsState
        {
            Location = "Custom_GrandpasGrove",
            AmbientLight = new[] { 12, 34, 56, 255 },
            WeatherDebrisCount = 3,
            TemporarySprites =
            {
                new TemporarySpriteSummary
                {
                    TextureAsset = "LooseSprites/Cursors",
                    SourceRect = new[] { 372, 1956, 10, 10 },
                    Position = new[] { 1024f, 2048f },
                    Motion = new[] { 0f, -0.35f },
                    Acceleration = new[] { 0f, 0f },
                    Color = new[] { 240, 248, 255, 255 },
                    Alpha = 0.45f,
                    AlphaFade = 0.0009f,
                    Scale = 4f,
                    ScaleChange = 0f,
                    Rotation = 0f,
                    RotationChange = 0f,
                    LayerDepth = 0.144f,
                    DrawAboveAlwaysFront = false,
                    RuntimeType = "TemporaryAnimatedSprite",
                },
            },
            LightSources =
            {
                new LightSourceSummary
                {
                    Id = "SVE_FH_Lantern",
                    Position = new[] { 320f, 512f },
                    Radius = 2.5f,
                    Color = new[] { 255, 220, 160, 255 },
                    TextureIndex = 4,
                    Context = "MapLight",
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"temporary_sprites\"", json);
        Assert.Contains("\"texture_asset\":\"LooseSprites/Cursors\"", json);
        Assert.Contains("\"source_rect\":[372,1956,10,10]", json);
        Assert.Contains("\"draw_above_always_front\":false", json);
        Assert.Contains("\"light_sources\"", json);
        Assert.Contains("\"weather_debris_count\":3", json);

        var roundTrip = JsonSerializer.Deserialize<VisualEffectsState>(json, ProtocolJson.Options)!;
        Assert.Equal("Custom_GrandpasGrove", roundTrip.Location);
        Assert.Equal("LooseSprites/Cursors", Assert.Single(roundTrip.TemporarySprites).TextureAsset);
        Assert.Equal(new[] { 255, 220, 160, 255 }, Assert.Single(roundTrip.LightSources).Color);
    }
}
