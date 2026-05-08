using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>state.visual_effects</c>.</summary>
public sealed class VisualEffectsRequest
{
    /// <summary>Optional location name; omitted to use the current location.</summary>
    public string? Location { get; set; }
}

/// <summary>Snapshot of visual effects for a location. Response shape of <c>state.visual_effects</c>.</summary>
public sealed class VisualEffectsState
{
    /// <summary>Location name the visual effects were captured from.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Ambient light RGBA channels.</summary>
    public int[] AmbientLight { get; set; } = new[] { 255, 255, 255, 255 };

    /// <summary>Temporary sprites currently tracked by the location.</summary>
    public List<TemporarySpriteSummary> TemporarySprites { get; set; } = new();

    /// <summary>Light sources currently active in the location.</summary>
    public List<LightSourceSummary> LightSources { get; set; } = new();

    /// <summary>Number of weather debris particles currently active.</summary>
    public int WeatherDebrisCount { get; set; }
}

/// <summary>Minimal descriptor for a temporary sprite visual effect.</summary>
public sealed class TemporarySpriteSummary
{
    public string? TextureAsset { get; set; }
    public int[]? SourceRect { get; set; }
    public float[] Position { get; set; } = new[] { 0f, 0f };
    public float[] Motion { get; set; } = new[] { 0f, 0f };
    public float[] Acceleration { get; set; } = new[] { 0f, 0f };
    public int[] Color { get; set; } = new[] { 255, 255, 255, 255 };
    public float Alpha { get; set; }
    public float AlphaFade { get; set; }
    public float Scale { get; set; }
    public float ScaleChange { get; set; }
    public float Rotation { get; set; }
    public float RotationChange { get; set; }
    public float LayerDepth { get; set; }
    public bool DrawAboveAlwaysFront { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}

/// <summary>Minimal descriptor for a location light source.</summary>
public sealed class LightSourceSummary
{
    public string Id { get; set; } = string.Empty;
    public float[] Position { get; set; } = new[] { 0f, 0f };
    public float Radius { get; set; }
    public int[] Color { get; set; } = new[] { 255, 255, 255, 255 };
    public int TextureIndex { get; set; }
    public string Context { get; set; } = string.Empty;
}
