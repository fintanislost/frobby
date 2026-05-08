using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.visual_effects</c> RPC method. Runs on the game thread.</summary>
public static class StateVisualEffectsHandler
{
    public const string Method = "state.visual_effects";

    private static readonly IVisualEffectsWorld ProductionWorld = new SdvVisualEffectsWorld();

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, ProductionWorld);

    internal static JsonElement Handle(JsonElement? paramsElement, IVisualEffectsWorld world)
    {
        var request = paramsElement.HasValue
            ? paramsElement.Value.Deserialize<VisualEffectsRequest>(ProtocolJson.Options) ?? new VisualEffectsRequest()
            : new VisualEffectsRequest();

        var requestedName = request.Location?.Trim();
        var hasRequestedLocation = !string.IsNullOrWhiteSpace(requestedName);
        var location = hasRequestedLocation
            ? world.GetLocation(requestedName!)
            : world.CurrentLocation;
        var fallbackLocationName = hasRequestedLocation ? requestedName! : string.Empty;

        return ProtocolJson.ToElement(VisualEffectsStateProjector.Project(
            location,
            fallbackLocationName,
            world.AmbientLight,
            world.LightSources,
            world.WeatherDebrisCount));
    }
}

internal sealed class SdvVisualEffectsWorld : IVisualEffectsWorld
{
    public IVisualEffectsLocation? CurrentLocation => Game1.currentLocation is { } location
        ? new SdvVisualEffectsLocation(location)
        : null;

    public int[] AmbientLight => ToColorArray(Game1.ambientLight);

    public IReadOnlyList<IVisualLightSource> LightSources => Game1.currentLightSources?.Values
        .Cast<object>()
        .Select(light => new ReflectedVisualLightSource(light))
        .Cast<IVisualLightSource>()
        .ToList() ?? new List<IVisualLightSource>();

    public int WeatherDebrisCount => CountWeatherDebris();

    public IVisualEffectsLocation? GetLocation(string name)
    {
        var location = Game1.getLocationFromName(name);
        return location is null ? null : new SdvVisualEffectsLocation(location);
    }

    private static int[] ToColorArray(Color color)
        => new[] { (int)color.R, color.G, color.B, color.A };

    private static int CountWeatherDebris()
    {
        foreach (var name in new[] { "weatherDebris", "debrisWeather" })
        {
            var value = ReflectionReader.ReadMember(Game1.game1, name)
                ?? ReflectionReader.ReadStaticMember(typeof(Game1), name);
            if (value is IEnumerable enumerable)
                return enumerable.Cast<object>().Count();
        }

        return 0;
    }
}

internal sealed class SdvVisualEffectsLocation : IVisualEffectsLocation
{
    private readonly GameLocation _location;

    public SdvVisualEffectsLocation(GameLocation location)
    {
        _location = location;
    }

    public string Name => _location.Name ?? string.Empty;

    public IReadOnlyList<IVisualTemporarySprite> TemporarySprites => _location.temporarySprites
        .Cast<object>()
        .Select(sprite => new ReflectedVisualTemporarySprite(sprite))
        .ToList();
}

internal sealed class ReflectedVisualTemporarySprite : IVisualTemporarySprite
{
    private readonly object _sprite;

    public ReflectedVisualTemporarySprite(object sprite)
    {
        _sprite = sprite;
    }

    public string? TextureAsset => ReflectionReader.ReadString(_sprite, "textureName", "TextureName");
    public int[]? SourceRect => ReflectionReader.ReadRectangle(_sprite, "sourceRect", "SourceRect", "sourceRectStartingPos");
    public float[]? Position => ReflectionReader.ReadVector(_sprite, "position", "Position");
    public float[]? Motion => ReflectionReader.ReadVector(_sprite, "motion", "Motion");
    public float[]? Acceleration => ReflectionReader.ReadVector(_sprite, "acceleration", "Acceleration");
    public int[]? Color => ReflectionReader.ReadColor(_sprite, "color", "Color");
    public float Alpha => ReflectionReader.ReadSingle(_sprite, "alpha", "Alpha");
    public float AlphaFade => ReflectionReader.ReadSingle(_sprite, "alphaFade", "AlphaFade");
    public float Scale => ReflectionReader.ReadSingle(_sprite, "scale", "Scale");
    public float ScaleChange => ReflectionReader.ReadSingle(_sprite, "scaleChange", "ScaleChange");
    public float Rotation => ReflectionReader.ReadSingle(_sprite, "rotation", "Rotation");
    public float RotationChange => ReflectionReader.ReadSingle(_sprite, "rotationChange", "RotationChange");
    public float LayerDepth => ReflectionReader.ReadSingle(_sprite, "layerDepth", "LayerDepth");
    public bool DrawAboveAlwaysFront => ReflectionReader.ReadBoolean(_sprite, "drawAboveAlwaysFront", "DrawAboveAlwaysFront");
    public string RuntimeType => _sprite.GetType().Name;
}

internal sealed class ReflectedVisualLightSource : IVisualLightSource
{
    private readonly object _light;

    public ReflectedVisualLightSource(object light)
    {
        _light = light;
    }

    public string Id => ReflectionReader.ReadString(_light, "Id", "id") ?? string.Empty;
    public float[]? Position => ReflectionReader.ReadVector(_light, "position", "Position");
    public float Radius => ReflectionReader.ReadSingle(_light, "radius", "Radius");
    public int[]? Color => ReflectionReader.ReadColor(_light, "color", "Color");
    public int TextureIndex => ReflectionReader.ReadInt32(_light, "textureIndex", "TextureIndex");
    public string Context => ReflectionReader.ReadString(_light, "lightContext", "LightContext", "context", "Context") ?? string.Empty;
}

internal static class ReflectionReader
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static object? ReadMember(object? instance, params string[] names)
    {
        if (instance is null)
            return null;

        var type = instance.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, MemberFlags);
            if (property is not null)
                return property.GetValue(instance);

            var field = type.GetField(name, MemberFlags);
            if (field is not null)
                return field.GetValue(instance);
        }

        return null;
    }

    public static object? ReadStaticMember(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            var property = type.GetProperty(name, MemberFlags);
            if (property is not null)
                return property.GetValue(null);

            var field = type.GetField(name, MemberFlags);
            if (field is not null)
                return field.GetValue(null);
        }

        return null;
    }

    public static string? ReadString(object instance, params string[] names)
        => ReadMember(instance, names)?.ToString();

    public static float ReadSingle(object instance, params string[] names)
    {
        var value = ReadMember(instance, names);
        return value switch
        {
            float number => number,
            double number => (float)number,
            int number => number,
            _ => 0f,
        };
    }

    public static int ReadInt32(object instance, params string[] names)
    {
        var value = ReadMember(instance, names);
        return value switch
        {
            int number => number,
            short number => number,
            byte number => number,
            _ => 0,
        };
    }

    public static bool ReadBoolean(object instance, params string[] names)
        => ReadMember(instance, names) is bool value && value;

    public static float[]? ReadVector(object instance, params string[] names)
    {
        var value = ReadMember(instance, names);
        return value switch
        {
            Vector2 vector => new[] { vector.X, vector.Y },
            float[] { Length: >= 2 } vector => new[] { vector[0], vector[1] },
            _ => null,
        };
    }

    public static int[]? ReadColor(object instance, params string[] names)
    {
        var value = ReadMember(instance, names);
        return value switch
        {
            Color color => new[] { (int)color.R, color.G, color.B, color.A },
            int[] { Length: >= 4 } color => new[] { color[0], color[1], color[2], color[3] },
            _ => null,
        };
    }

    public static int[]? ReadRectangle(object instance, params string[] names)
    {
        var value = ReadMember(instance, names);
        return value switch
        {
            Rectangle { Width: 0, Height: 0 } => null,
            Rectangle rectangle => new[] { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height },
            int[] { Length: >= 4 } rect when rect[2] != 0 || rect[3] != 0 => new[] { rect[0], rect[1], rect[2], rect[3] },
            _ => null,
        };
    }
}
