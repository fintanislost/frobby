using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateVisualEffectsHandlerTests
{
    [Fact]
    public void Handle_NoCurrentLocation_ReturnsEmptySnapshotWithAmbientLightAndWeather()
    {
        var world = new FakeVisualEffectsWorld
        {
            CurrentLocation = null,
            AmbientLight = new[] { 12, 34, 56, 78 },
            WeatherDebrisCount = 5,
        };

        var result = StateVisualEffectsHandler.Handle(null, world);
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("", state.Location);
        Assert.Equal(new[] { 12, 34, 56, 78 }, state.AmbientLight);
        Assert.Empty(state.TemporarySprites);
        Assert.Empty(state.LightSources);
        Assert.Equal(5, state.WeatherDebrisCount);
    }

    [Fact]
    public void Handle_CurrentLocation_ProjectsTemporarySpriteAndLight()
    {
        var world = new FakeVisualEffectsWorld
        {
            CurrentLocation = new FakeVisualEffectsLocation
            {
                Name = "Custom_Summit",
                TemporarySprites = new[]
                {
                    new FakeVisualTemporarySprite
                    {
                        TextureAsset = "Mods\\Example\\Glow",
                        SourceRect = new[] { 1, 2, 16, 32 },
                        Position = new[] { 64f, 128f },
                        Motion = new[] { 0.5f, -0.25f },
                        Acceleration = new[] { 0.01f, 0.02f },
                        Color = new[] { 10, 20, 30, 40 },
                        Alpha = 0.75f,
                        AlphaFade = 0.05f,
                        Scale = 2f,
                        ScaleChange = 0.1f,
                        Rotation = 1.5f,
                        RotationChange = 0.25f,
                        LayerDepth = 0.9f,
                        DrawAboveAlwaysFront = true,
                        RuntimeType = "TemporaryAnimatedSprite",
                    },
                },
            },
            LightSources = new[]
            {
                new FakeVisualLightSource
                {
                    Id = "light-1",
                    Position = new[] { 256f, 512f },
                    Radius = 3.5f,
                    Color = new[] { 200, 180, 160, 255 },
                    TextureIndex = 4,
                    Context = "Map",
                },
            },
        };

        var result = StateVisualEffectsHandler.Handle(null, world);
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("Custom_Summit", state.Location);
        Assert.Collection(state.TemporarySprites,
            sprite =>
            {
                Assert.Equal("Mods/Example/Glow", sprite.TextureAsset);
                Assert.Equal(new[] { 1, 2, 16, 32 }, sprite.SourceRect);
                Assert.Equal(new[] { 64f, 128f }, sprite.Position);
                Assert.Equal(new[] { 0.5f, -0.25f }, sprite.Motion);
                Assert.Equal(new[] { 0.01f, 0.02f }, sprite.Acceleration);
                Assert.Equal(new[] { 10, 20, 30, 40 }, sprite.Color);
                Assert.Equal(0.75f, sprite.Alpha);
                Assert.Equal(0.05f, sprite.AlphaFade);
                Assert.Equal(2f, sprite.Scale);
                Assert.Equal(0.1f, sprite.ScaleChange);
                Assert.Equal(1.5f, sprite.Rotation);
                Assert.Equal(0.25f, sprite.RotationChange);
                Assert.Equal(0.9f, sprite.LayerDepth);
                Assert.True(sprite.DrawAboveAlwaysFront);
                Assert.Equal("TemporaryAnimatedSprite", sprite.RuntimeType);
            });
        Assert.Collection(state.LightSources,
            light =>
            {
                Assert.Equal("light-1", light.Id);
                Assert.Equal(new[] { 256f, 512f }, light.Position);
                Assert.Equal(3.5f, light.Radius);
                Assert.Equal(new[] { 200, 180, 160, 255 }, light.Color);
                Assert.Equal(4, light.TextureIndex);
                Assert.Equal("Map", light.Context);
            });
    }

    [Fact]
    public void Handle_RequestedNamedLocation_ProjectsThatLocation()
    {
        var world = new FakeVisualEffectsWorld
        {
            CurrentLocation = new FakeVisualEffectsLocation { Name = "Farm" },
            Locations =
            {
                ["ExampleMod.Forest"] = new FakeVisualEffectsLocation
                {
                    Name = "ExampleMod.Forest",
                    TemporarySprites = new[]
                    {
                        new FakeVisualTemporarySprite { RuntimeType = "ForestEffect" },
                    },
                },
            },
        };
        var request = JsonSerializer.SerializeToElement(
            new VisualEffectsRequest { Location = "ExampleMod.Forest" },
            ProtocolJson.Options);

        var result = StateVisualEffectsHandler.Handle(request, world);
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("ExampleMod.Forest", state.Location);
        Assert.Collection(state.TemporarySprites,
            sprite => Assert.Equal("ForestEffect", sprite.RuntimeType));
    }

    [Fact]
    public void Handle_UnknownRequestedLocation_PreservesNameAndKeepsGlobalLights()
    {
        var world = new FakeVisualEffectsWorld
        {
            CurrentLocation = new FakeVisualEffectsLocation
            {
                Name = "Farm",
                TemporarySprites = new[]
                {
                    new FakeVisualTemporarySprite { RuntimeType = "CurrentLocationEffect" },
                },
            },
            LightSources = new[]
            {
                new FakeVisualLightSource { Id = "b-light", Context = "Global" },
                new FakeVisualLightSource { Id = "a-light", Context = "Global" },
            },
        };
        var request = JsonSerializer.SerializeToElement(
            new VisualEffectsRequest { Location = "ExampleMod.MissingLocation" },
            ProtocolJson.Options);

        var result = StateVisualEffectsHandler.Handle(request, world);
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("ExampleMod.MissingLocation", state.Location);
        Assert.Empty(state.TemporarySprites);
        Assert.Collection(state.LightSources,
            light => Assert.Equal("a-light", light.Id),
            light => Assert.Equal("b-light", light.Id));
    }

    private sealed class FakeVisualEffectsWorld : IVisualEffectsWorld
    {
        public IVisualEffectsLocation? CurrentLocation { get; init; } = new FakeVisualEffectsLocation();
        public Dictionary<string, IVisualEffectsLocation> Locations { get; } = new();
        public int[] AmbientLight { get; init; } = new[] { 255, 255, 255, 255 };
        public IReadOnlyList<IVisualLightSource> LightSources { get; init; } = new List<IVisualLightSource>();
        public int WeatherDebrisCount { get; init; }

        public IVisualEffectsLocation? GetLocation(string name)
            => Locations.TryGetValue(name, out var location) ? location : null;
    }

    private sealed class FakeVisualEffectsLocation : IVisualEffectsLocation
    {
        public string Name { get; init; } = "Farm";
        public IReadOnlyList<IVisualTemporarySprite> TemporarySprites { get; init; } = new List<IVisualTemporarySprite>();
    }

    private sealed class FakeVisualTemporarySprite : IVisualTemporarySprite
    {
        public string? TextureAsset { get; init; }
        public int[]? SourceRect { get; init; }
        public float[]? Position { get; init; }
        public float[]? Motion { get; init; }
        public float[]? Acceleration { get; init; }
        public int[]? Color { get; init; }
        public float Alpha { get; init; }
        public float AlphaFade { get; init; }
        public float Scale { get; init; }
        public float ScaleChange { get; init; }
        public float Rotation { get; init; }
        public float RotationChange { get; init; }
        public float LayerDepth { get; init; }
        public bool DrawAboveAlwaysFront { get; init; }
        public string RuntimeType { get; init; } = string.Empty;
    }

    private sealed class FakeVisualLightSource : IVisualLightSource
    {
        public string Id { get; init; } = string.Empty;
        public float[]? Position { get; init; }
        public float Radius { get; init; }
        public int[]? Color { get; init; }
        public int TextureIndex { get; init; }
        public string Context { get; init; } = string.Empty;
    }
}
