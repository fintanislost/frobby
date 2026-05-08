using System;
using System.Collections.Generic;
using System.Linq;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal static class VisualEffectsStateProjector
{
    public static VisualEffectsState Project(
        IVisualEffectsLocation? location,
        string fallbackLocationName,
        int[] ambientLight,
        IReadOnlyList<IVisualLightSource> lightSources,
        int weatherDebrisCount)
    {
        return new VisualEffectsState
        {
            Location = location?.Name ?? fallbackLocationName,
            AmbientLight = NormalizeColor(ambientLight),
            TemporarySprites = location?.TemporarySprites
                .Select(sprite => new TemporarySpriteSummary
                {
                    TextureAsset = NormalizeAssetName(sprite.TextureAsset),
                    SourceRect = NormalizeSourceRect(sprite.SourceRect),
                    Position = NormalizeVector(sprite.Position),
                    Motion = NormalizeVector(sprite.Motion),
                    Acceleration = NormalizeVector(sprite.Acceleration),
                    Color = NormalizeColor(sprite.Color),
                    Alpha = sprite.Alpha,
                    AlphaFade = sprite.AlphaFade,
                    Scale = sprite.Scale,
                    ScaleChange = sprite.ScaleChange,
                    Rotation = sprite.Rotation,
                    RotationChange = sprite.RotationChange,
                    LayerDepth = sprite.LayerDepth,
                    DrawAboveAlwaysFront = sprite.DrawAboveAlwaysFront,
                    RuntimeType = sprite.RuntimeType,
                })
                .ToList() ?? new List<TemporarySpriteSummary>(),
            LightSources = lightSources
                .OrderBy(light => light.Id, StringComparer.Ordinal)
                .Select(light => new LightSourceSummary
                {
                    Id = light.Id,
                    Position = NormalizeVector(light.Position),
                    Radius = light.Radius,
                    Color = NormalizeColor(light.Color),
                    TextureIndex = light.TextureIndex,
                    Context = light.Context,
                })
                .ToList(),
            WeatherDebrisCount = Math.Max(0, weatherDebrisCount),
        };
    }

    private static string? NormalizeAssetName(string? assetName)
        => assetName?.Replace('\\', '/');

    private static float[] NormalizeVector(float[]? vector)
        => vector is { Length: >= 2 }
            ? new[] { vector[0], vector[1] }
            : new[] { 0f, 0f };

    private static int[] NormalizeColor(int[]? color)
        => color is { Length: >= 4 }
            ? new[] { color[0], color[1], color[2], color[3] }
            : new[] { 255, 255, 255, 255 };

    private static int[]? NormalizeSourceRect(int[]? sourceRect)
        => sourceRect is { Length: >= 4 }
            ? new[] { sourceRect[0], sourceRect[1], sourceRect[2], sourceRect[3] }
            : null;
}

internal interface IVisualEffectsWorld
{
    IVisualEffectsLocation? CurrentLocation { get; }
    int[] AmbientLight { get; }
    IReadOnlyList<IVisualLightSource> LightSources { get; }
    int WeatherDebrisCount { get; }
    IVisualEffectsLocation? GetLocation(string name);
}

internal interface IVisualEffectsLocation
{
    string Name { get; }
    IReadOnlyList<IVisualTemporarySprite> TemporarySprites { get; }
}

internal interface IVisualTemporarySprite
{
    string? TextureAsset { get; }
    int[]? SourceRect { get; }
    float[]? Position { get; }
    float[]? Motion { get; }
    float[]? Acceleration { get; }
    int[]? Color { get; }
    float Alpha { get; }
    float AlphaFade { get; }
    float Scale { get; }
    float ScaleChange { get; }
    float Rotation { get; }
    float RotationChange { get; }
    float LayerDepth { get; }
    bool DrawAboveAlwaysFront { get; }
    string RuntimeType { get; }
}

internal interface IVisualLightSource
{
    string Id { get; }
    float[]? Position { get; }
    float Radius { get; }
    int[]? Color { get; }
    int TextureIndex { get; }
    string Context { get; }
}
