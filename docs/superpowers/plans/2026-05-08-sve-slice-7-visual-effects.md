# SVE Slice 7 Visual Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby visual-effect observability, runner waits, and one SVE scenario proving temporary animated sprites and draw evidence.

**Architecture:** Frobby gets a new `state.visual_effects` RPC that snapshots temporary animated sprites, ambient light, active light sources, and best-effort weather debris counts from the live Stardew runtime. The runner adds `wait.visual_effects` as a polling convenience over that state, while render proof stays on existing `draw.*` and screenshot capture. SVE only supplies the real-world test scenario; no SVE location names or asset assumptions go into Frobby production code.

**Tech Stack:** C#/.NET, SMAPI/Stardew Valley runtime types, Frobby JSON-RPC protocol DTOs, xUnit, existing `ProtocolJson` snake-case serialization, existing headless `sdv-test` runner.

---

## File Structure

Frobby worktree:

- Create `src/Protocol/Models/VisualEffectsState.cs`
  - DTOs for `state.visual_effects` request and response.
- Create `tests/Protocol.Tests/VisualEffectsStateSerializationTests.cs`
  - Locks wire shape to snake_case and proves round-trip behavior.
- Create `src/Harness/Handlers/StateVisualEffectsHandler.cs`
  - RPC handler, production world adapter, and narrow internal interfaces for unit tests.
- Create `src/Harness/Handlers/VisualEffectsStateProjector.cs`
  - Pure projector from internal visual-effect interfaces to protocol DTOs.
- Create `tests/Harness.Tests/StateVisualEffectsHandlerTests.cs`
  - Unit tests against fake world data; no live Stardew process needed.
- Modify `src/Harness/ModEntry.cs`
  - Register `state.visual_effects` and include it in the startup method list.
- Modify `src/Runner.Dsl/State.cs`
  - Add `State.VisualEffects(location?)`.
- Modify `tests/Runner.Dsl.Tests/Facets/StateTests.cs`
  - Add DSL invocation/deserialize coverage.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add runner-side `wait.visual_effects`, validation, filters, and report label.
- Modify `tests/Runner.Tests/ScenarioRunnerTests.cs`
  - Add wait polling, timeout, and validation tests.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, `README.md`
  - Document the new state RPC and wait step.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Mark Slice 7 active while building, then done after SVE verification.

SVE repo:

- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/09-sve-visual-effects.test.json`
  - Scenario proving Grandpa's Grove cauldron-style temporary sprites through `wait.visual_effects`, draw assertion, and screenshot.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
  - Add the visual-effects scenario and command.

Safety:

- Keep SVE changes on its existing feature branch. Do not merge SVE into `master`.
- Frobby changes are allowed to merge back to `main` after tests pass.

---

## Task 1: Protocol DTOs For Visual Effects State

**Files:**
- Create: `src/Protocol/Models/VisualEffectsState.cs`
- Create: `tests/Protocol.Tests/VisualEffectsStateSerializationTests.cs`

- [ ] **Step 1: Write the failing protocol serialization tests**

Create `tests/Protocol.Tests/VisualEffectsStateSerializationTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the protocol test and confirm it fails**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter FullyQualifiedName~VisualEffectsStateSerializationTests
```

Expected: compile failure because `VisualEffectsRequest`, `VisualEffectsState`, `TemporarySpriteSummary`, and `LightSourceSummary` do not exist.

- [ ] **Step 3: Add protocol models**

Create `src/Protocol/Models/VisualEffectsState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>state.visual_effects</c>.</summary>
public sealed class VisualEffectsRequest
{
    public string? Location { get; set; }
}

/// <summary>Snapshot of location/global visual-effect state. Response shape of <c>state.visual_effects</c>.</summary>
public sealed class VisualEffectsState
{
    public string Location { get; set; } = string.Empty;
    public int[] AmbientLight { get; set; } = new[] { 255, 255, 255, 255 };
    public List<TemporarySpriteSummary> TemporarySprites { get; set; } = new();
    public List<LightSourceSummary> LightSources { get; set; } = new();
    public int WeatherDebrisCount { get; set; }
}

/// <summary>Stable summary of a live Stardew temporary animated sprite.</summary>
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

/// <summary>Stable summary of an active Stardew light source.</summary>
public sealed class LightSourceSummary
{
    public string Id { get; set; } = string.Empty;
    public float[] Position { get; set; } = new[] { 0f, 0f };
    public float Radius { get; set; }
    public int[] Color { get; set; } = new[] { 255, 255, 255, 255 };
    public int TextureIndex { get; set; }
    public string Context { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Run the protocol test and confirm it passes**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter FullyQualifiedName~VisualEffectsStateSerializationTests
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

Run:

```bash
git add src/Protocol/Models/VisualEffectsState.cs tests/Protocol.Tests/VisualEffectsStateSerializationTests.cs
git commit -m "feat: add visual effects protocol state"
```

---

## Task 2: Harness Projection And RPC Handler

**Files:**
- Create: `src/Harness/Handlers/VisualEffectsStateProjector.cs`
- Create: `src/Harness/Handlers/StateVisualEffectsHandler.cs`
- Create: `tests/Harness.Tests/StateVisualEffectsHandlerTests.cs`

- [ ] **Step 1: Write failing harness tests**

Create `tests/Harness.Tests/StateVisualEffectsHandlerTests.cs`:

```csharp
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
    public void Handle_NoCurrentLocation_ReturnsEmptySnapshot()
    {
        var result = StateVisualEffectsHandler.Handle(null, new FakeVisualEffectsWorld { CurrentLocation = null });
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("", state.Location);
        Assert.Equal(new[] { 8, 9, 10, 255 }, state.AmbientLight);
        Assert.Empty(state.TemporarySprites);
        Assert.Empty(state.LightSources);
        Assert.Equal(2, state.WeatherDebrisCount);
    }

    [Fact]
    public void Handle_CurrentLocation_ProjectsSpritesLightsAndAmbientState()
    {
        var result = StateVisualEffectsHandler.Handle(null, new FakeVisualEffectsWorld());
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("Custom_GrandpasGrove", state.Location);
        Assert.Equal(new[] { 8, 9, 10, 255 }, state.AmbientLight);
        Assert.Equal(2, state.WeatherDebrisCount);

        var sprite = Assert.Single(state.TemporarySprites);
        Assert.Equal("LooseSprites/Cursors", sprite.TextureAsset);
        Assert.Equal(new[] { 372, 1956, 10, 10 }, sprite.SourceRect);
        Assert.Equal(new[] { 1024f, 2048f }, sprite.Position);
        Assert.Equal(new[] { 0f, -0.35f }, sprite.Motion);
        Assert.Equal(new[] { 240, 248, 255, 255 }, sprite.Color);
        Assert.Equal(0.45f, sprite.Alpha);
        Assert.Equal(0.0009f, sprite.AlphaFade);
        Assert.Equal(4f, sprite.Scale);
        Assert.Equal(0.144f, sprite.LayerDepth);
        Assert.Equal("TemporaryAnimatedSprite", sprite.RuntimeType);

        var light = Assert.Single(state.LightSources);
        Assert.Equal("SVE_FH_Lantern", light.Id);
        Assert.Equal(new[] { 320f, 512f }, light.Position);
        Assert.Equal(2.5f, light.Radius);
        Assert.Equal(new[] { 255, 220, 160, 255 }, light.Color);
        Assert.Equal(4, light.TextureIndex);
        Assert.Equal("MapLight", light.Context);
    }

    [Fact]
    public void Handle_RequestedLocation_ProjectsNamedLocation()
    {
        var args = JsonDocument.Parse("{\"location\":\"Custom_CrimsonBadlands\"}").RootElement;
        var result = StateVisualEffectsHandler.Handle(args, new FakeVisualEffectsWorld());
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("Custom_CrimsonBadlands", state.Location);
        Assert.Equal("Maps/SandstormEffect", Assert.Single(state.TemporarySprites).TextureAsset);
    }

    [Fact]
    public void Handle_UnknownRequestedLocation_ReturnsEmptyNamedSnapshot()
    {
        var args = JsonDocument.Parse("{\"location\":\"Missing_Location\"}").RootElement;
        var result = StateVisualEffectsHandler.Handle(args, new FakeVisualEffectsWorld());
        var state = JsonSerializer.Deserialize<VisualEffectsState>(result, ProtocolJson.Options)!;

        Assert.Equal("Missing_Location", state.Location);
        Assert.Empty(state.TemporarySprites);
        Assert.Single(state.LightSources);
    }

    private sealed class FakeVisualEffectsWorld : IVisualEffectsWorld
    {
        public IVisualEffectsLocation? CurrentLocation { get; init; } = FakeVisualEffectsLocation.Grove;
        public int[] AmbientLight => new[] { 8, 9, 10, 255 };
        public IReadOnlyList<IVisualLightSource> LightSources { get; } = new[] { FakeVisualLightSource.Lantern };
        public int WeatherDebrisCount => 2;

        public IVisualEffectsLocation? GetLocation(string name)
            => name == "Custom_CrimsonBadlands" ? FakeVisualEffectsLocation.Badlands : null;
    }

    private sealed class FakeVisualEffectsLocation : IVisualEffectsLocation
    {
        public static readonly FakeVisualEffectsLocation Grove = new(
            "Custom_GrandpasGrove",
            new FakeVisualTemporarySprite("LooseSprites/Cursors"));

        public static readonly FakeVisualEffectsLocation Badlands = new(
            "Custom_CrimsonBadlands",
            new FakeVisualTemporarySprite("Maps/SandstormEffect"));

        private FakeVisualEffectsLocation(string name, IVisualTemporarySprite sprite)
        {
            Name = name;
            TemporarySprites = new[] { sprite };
        }

        public string Name { get; }
        public IReadOnlyList<IVisualTemporarySprite> TemporarySprites { get; }
    }

    private sealed class FakeVisualTemporarySprite : IVisualTemporarySprite
    {
        public FakeVisualTemporarySprite(string textureAsset) => TextureAsset = textureAsset;

        public string? TextureAsset { get; }
        public int[]? SourceRect => new[] { 372, 1956, 10, 10 };
        public float[] Position => new[] { 1024f, 2048f };
        public float[] Motion => new[] { 0f, -0.35f };
        public float[] Acceleration => new[] { 0f, 0f };
        public int[] Color => new[] { 240, 248, 255, 255 };
        public float Alpha => 0.45f;
        public float AlphaFade => 0.0009f;
        public float Scale => 4f;
        public float ScaleChange => 0f;
        public float Rotation => 0f;
        public float RotationChange => 0f;
        public float LayerDepth => 0.144f;
        public bool DrawAboveAlwaysFront => false;
        public string RuntimeType => "TemporaryAnimatedSprite";
    }

    private sealed class FakeVisualLightSource : IVisualLightSource
    {
        public static readonly FakeVisualLightSource Lantern = new();

        public string Id => "SVE_FH_Lantern";
        public float[] Position => new[] { 320f, 512f };
        public float Radius => 2.5f;
        public int[] Color => new[] { 255, 220, 160, 255 };
        public int TextureIndex => 4;
        public string Context => "MapLight";
    }
}
```

- [ ] **Step 2: Run the harness test and confirm it fails**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StateVisualEffectsHandlerTests
```

Expected: compile failure because handler/projector/interfaces do not exist.

- [ ] **Step 3: Add the pure projector and internal interfaces**

Create `src/Harness/Handlers/VisualEffectsStateProjector.cs`:

```csharp
using System;
using System.Linq;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

internal static class VisualEffectsStateProjector
{
    public static VisualEffectsState Project(
        IVisualEffectsLocation? location,
        string fallbackLocationName,
        int[] ambientLight,
        System.Collections.Generic.IReadOnlyList<IVisualLightSource> lightSources,
        int weatherDebrisCount)
    {
        var state = new VisualEffectsState
        {
            Location = location?.Name ?? fallbackLocationName,
            AmbientLight = NormalizeColor(ambientLight),
            WeatherDebrisCount = Math.Max(0, weatherDebrisCount),
        };

        if (location is not null)
        {
            foreach (var sprite in location.TemporarySprites)
            {
                state.TemporarySprites.Add(new TemporarySpriteSummary
                {
                    TextureAsset = NormalizeAssetName(sprite.TextureAsset),
                    SourceRect = sprite.SourceRect,
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
                });
            }
        }

        foreach (var light in lightSources.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            state.LightSources.Add(new LightSourceSummary
            {
                Id = light.Id,
                Position = NormalizeVector(light.Position),
                Radius = light.Radius,
                Color = NormalizeColor(light.Color),
                TextureIndex = light.TextureIndex,
                Context = light.Context,
            });
        }

        return state;
    }

    private static string? NormalizeAssetName(string? asset)
        => string.IsNullOrWhiteSpace(asset) ? null : asset.Replace('\\', '/');

    private static float[] NormalizeVector(float[]? values)
        => values is { Length: >= 2 } ? new[] { values[0], values[1] } : new[] { 0f, 0f };

    private static int[] NormalizeColor(int[]? values)
        => values is { Length: >= 4 } ? new[] { values[0], values[1], values[2], values[3] } : new[] { 255, 255, 255, 255 };
}

internal interface IVisualEffectsWorld
{
    IVisualEffectsLocation? CurrentLocation { get; }
    IVisualEffectsLocation? GetLocation(string name);
    int[] AmbientLight { get; }
    System.Collections.Generic.IReadOnlyList<IVisualLightSource> LightSources { get; }
    int WeatherDebrisCount { get; }
}

internal interface IVisualEffectsLocation
{
    string Name { get; }
    System.Collections.Generic.IReadOnlyList<IVisualTemporarySprite> TemporarySprites { get; }
}

internal interface IVisualTemporarySprite
{
    string? TextureAsset { get; }
    int[]? SourceRect { get; }
    float[] Position { get; }
    float[] Motion { get; }
    float[] Acceleration { get; }
    int[] Color { get; }
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
    float[] Position { get; }
    float Radius { get; }
    int[] Color { get; }
    int TextureIndex { get; }
    string Context { get; }
}
```

- [ ] **Step 4: Add the RPC handler and production adapters**

Create `src/Harness/Handlers/StateVisualEffectsHandler.cs`:

```csharp
using System;
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
        var request = paramsElement is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<VisualEffectsRequest>(obj.GetRawText(), ProtocolJson.Options) ?? new VisualEffectsRequest()
            : new VisualEffectsRequest();

        var requestedName = request.Location;
        var location = string.IsNullOrWhiteSpace(requestedName)
            ? world.CurrentLocation
            : world.GetLocation(requestedName);
        var fallbackName = requestedName ?? string.Empty;

        return ProtocolJson.ToElement(VisualEffectsStateProjector.Project(
            location,
            fallbackName,
            world.AmbientLight,
            world.LightSources,
            world.WeatherDebrisCount));
    }
}

internal sealed class SdvVisualEffectsWorld : IVisualEffectsWorld
{
    public IVisualEffectsLocation? CurrentLocation
        => Game1.currentLocation is null ? null : new SdvVisualEffectsLocation(Game1.currentLocation);

    public int[] AmbientLight => ColorArray(Game1.ambientLight);

    public IReadOnlyList<IVisualLightSource> LightSources
        => Game1.currentLightSources?.Values
            .Select(source => new SdvVisualLightSource(source))
            .Cast<IVisualLightSource>()
            .ToArray()
            ?? Array.Empty<IVisualLightSource>();

    public int WeatherDebrisCount => CountEnumerableMember(Game1.instance, "weatherDebris")
        ?? CountEnumerableMember(typeof(Game1), "weatherDebris")
        ?? CountEnumerableMember(Game1.instance, "debrisWeather")
        ?? CountEnumerableMember(typeof(Game1), "debrisWeather")
        ?? 0;

    public IVisualEffectsLocation? GetLocation(string name)
    {
        var location = Game1.getLocationFromName(name);
        return location is null ? null : new SdvVisualEffectsLocation(location);
    }

    internal static int[] ColorArray(Color color)
        => new[] { color.R, color.G, color.B, color.A };

    private static int? CountEnumerableMember(object? target, string memberName)
    {
        if (target is null)
            return null;

        var type = target as Type ?? target.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        object? value = type.GetField(memberName, flags)?.GetValue(target is Type ? null : target)
            ?? type.GetProperty(memberName, flags)?.GetValue(target is Type ? null : target);

        return value switch
        {
            null => null,
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count(),
            _ => null,
        };
    }
}

internal sealed class SdvVisualEffectsLocation : IVisualEffectsLocation
{
    private readonly GameLocation _location;

    public SdvVisualEffectsLocation(GameLocation location) => _location = location;

    public string Name => _location.NameOrUniqueName;

    public IReadOnlyList<IVisualTemporarySprite> TemporarySprites
        => _location.temporarySprites
            .Select(sprite => new SdvVisualTemporarySprite(sprite))
            .Cast<IVisualTemporarySprite>()
            .ToArray();
}

internal sealed class SdvVisualTemporarySprite : IVisualTemporarySprite
{
    private readonly object _sprite;

    public SdvVisualTemporarySprite(object sprite) => _sprite = sprite;

    public string? TextureAsset => NormalizeAssetName(Read<string>("textureName"));
    public int[]? SourceRect => RectArray(Read<Rectangle>("sourceRect"));
    public float[] Position => VectorArray(Read<Vector2>("position"));
    public float[] Motion => VectorArray(Read<Vector2>("motion"));
    public float[] Acceleration => VectorArray(Read<Vector2>("acceleration"));
    public int[] Color => SdvVisualEffectsWorld.ColorArray(Read<Color>("color"));
    public float Alpha => Read<float>("alpha");
    public float AlphaFade => Read<float>("alphaFade");
    public float Scale => Read<float>("scale");
    public float ScaleChange => Read<float>("scaleChange");
    public float Rotation => Read<float>("rotation");
    public float RotationChange => Read<float>("rotationChange");
    public float LayerDepth => Read<float>("layerDepth");
    public bool DrawAboveAlwaysFront => Read<bool>("drawAboveAlwaysFront");
    public string RuntimeType => _sprite.GetType().Name;

    private T Read<T>(string name)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = _sprite.GetType();
        var value = type.GetField(name, flags)?.GetValue(_sprite)
            ?? type.GetProperty(name, flags)?.GetValue(_sprite);
        return value is T typed ? typed : default!;
    }

    private static string? NormalizeAssetName(string? asset)
        => string.IsNullOrWhiteSpace(asset) ? null : asset.Replace('\\', '/');

    private static int[]? RectArray(Rectangle rect)
        => rect.Width == 0 && rect.Height == 0 ? null : new[] { rect.X, rect.Y, rect.Width, rect.Height };

    private static float[] VectorArray(Vector2 value)
        => new[] { value.X, value.Y };
}

internal sealed class SdvVisualLightSource : IVisualLightSource
{
    private readonly object _lightSource;

    public SdvVisualLightSource(object lightSource) => _lightSource = lightSource;

    public string Id => Read<string>("Id") ?? Read<string>("id") ?? string.Empty;
    public float[] Position => VectorArray(Read<Vector2>("position"));
    public float Radius => Read<float>("radius");
    public int[] Color => SdvVisualEffectsWorld.ColorArray(Read<Color>("color"));
    public int TextureIndex => Read<int>("textureIndex");
    public string Context => Read<object>("lightContext")?.ToString() ?? string.Empty;

    private T? Read<T>(string name)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = _lightSource.GetType();
        var value = type.GetField(name, flags)?.GetValue(_lightSource)
            ?? type.GetProperty(name, flags)?.GetValue(_lightSource);
        return value is T typed ? typed : default;
    }

    private static float[] VectorArray(Vector2 value)
        => new[] { value.X, value.Y };
}
```

The reflection access is deliberate: it keeps Frobby tolerant of Stardew/SMAPI minor field/property differences while still projecting a neutral, bounded summary.

- [ ] **Step 5: Run the harness tests and confirm they pass**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StateVisualEffectsHandlerTests
```

Expected: PASS.

- [ ] **Step 6: Commit Task 2**

Run:

```bash
git add src/Harness/Handlers/StateVisualEffectsHandler.cs src/Harness/Handlers/VisualEffectsStateProjector.cs tests/Harness.Tests/StateVisualEffectsHandlerTests.cs
git commit -m "feat: expose visual effects state"
```

---

## Task 3: Register RPC And Add DSL Facet

**Files:**
- Modify: `src/Harness/ModEntry.cs`
- Modify: `src/Runner.Dsl/State.cs`
- Modify: `tests/Runner.Dsl.Tests/Facets/StateTests.cs`

- [ ] **Step 1: Write failing DSL test**

Append this test to `tests/Runner.Dsl.Tests/Facets/StateTests.cs` before the closing class brace:

```csharp
[Fact]
public async Task VisualEffects_InvokesStateVisualEffectsAndDeserializes()
{
    SdvTestSession.ResetForTests();
    var inv = new StubInvoker
    {
        NextJson = "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[8,9,10,255],\"temporary_sprites\":[{\"texture_asset\":\"LooseSprites/Cursors\",\"source_rect\":[372,1956,10,10],\"position\":[1024,2048],\"motion\":[0,-0.35],\"acceleration\":[0,0],\"color\":[240,248,255,255],\"alpha\":0.45,\"alpha_fade\":0.0009,\"scale\":4,\"scale_change\":0,\"rotation\":0,\"rotation_change\":0,\"layer_depth\":0.144,\"draw_above_always_front\":false,\"runtime_type\":\"TemporaryAnimatedSprite\"}],\"light_sources\":[],\"weather_debris_count\":0}",
    };
    SdvTestSession.InitializeForTests(inv);
    try
    {
        var state = await State.VisualEffects("Custom_GrandpasGrove");

        Assert.Equal("state.visual_effects", inv.LastMethod);
        Assert.Equal("Custom_GrandpasGrove", inv.LastParams!.Value.GetProperty("location").GetString());
        Assert.Equal("Custom_GrandpasGrove", state.Location);
        Assert.Equal("LooseSprites/Cursors", Assert.Single(state.TemporarySprites).TextureAsset);
    }
    finally { SdvTestSession.ResetForTests(); }
}
```

- [ ] **Step 2: Run the DSL test and confirm it fails**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter FullyQualifiedName~StateTests.VisualEffects
```

Expected: compile failure because `State.VisualEffects` does not exist.

- [ ] **Step 3: Add `State.VisualEffects`**

In `src/Runner.Dsl/State.cs`, add this method after `Shop` and before `Event`:

```csharp
public static async Task<VisualEffectsState> VisualEffects(string? location = null, CancellationToken ct = default)
{
    var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
    JsonElement? p = location is null
        ? null
        : JsonSerializer.SerializeToElement(new VisualEffectsRequest { Location = location }, ProtocolJson.Options);
    var resp = await s.InvokeAsync("state.visual_effects", p, ct);
    return Deserialize<VisualEffectsState>(resp, "state.visual_effects");
}
```

- [ ] **Step 4: Register the RPC in the harness**

In `src/Harness/ModEntry.cs`, add registration after `StateShopHandler`:

```csharp
_rpc.Register(StateVisualEffectsHandler.Method, p => StateVisualEffectsHandler.Handle(p));
```

Update the startup log method list from:

```text
state.menu, state.shop, state.event, state.mods
```

to:

```text
state.menu, state.shop, state.visual_effects, state.event, state.mods
```

- [ ] **Step 5: Run focused tests**

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter FullyQualifiedName~StateTests.VisualEffects
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StateVisualEffectsHandlerTests
```

Expected: both PASS.

- [ ] **Step 6: Commit Task 3**

Run:

```bash
git add src/Harness/ModEntry.cs src/Runner.Dsl/State.cs tests/Runner.Dsl.Tests/Facets/StateTests.cs
git commit -m "feat: register visual effects state rpc"
```

---

## Task 4: Runner `wait.visual_effects`

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner tests**

Add these tests to `tests/Runner.Tests/ScenarioRunnerTests.cs` near the existing `WaitLocationContent_*` tests:

```csharp
[Fact]
public async Task WaitVisualEffects_PollsStateUntilTemporarySpriteMatches()
{
    var socket = SocketPath();
    var calls = new List<string>();
    var polls = 0;
    JsonElement? lastParams = null;
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                calls.Add(req.Method);
                if (req.Method == "state.visual_effects")
                    lastParams = req.Params;

                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.visual_effects" => JsonDocument.Parse(polls++ == 0
                        ? "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[255,255,255,255],\"temporary_sprites\":[],\"light_sources\":[],\"weather_debris_count\":0}"
                        : "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[255,255,255,255],\"temporary_sprites\":[{\"texture_asset\":\"LooseSprites/Cursors\",\"source_rect\":[372,1956,10,10],\"position\":[1024,2048],\"motion\":[0,-0.35],\"acceleration\":[0,0],\"color\":[240,248,255,255],\"alpha\":0.45,\"alpha_fade\":0.0009,\"scale\":4,\"scale_change\":0,\"rotation\":0,\"rotation_change\":0,\"layer_depth\":0.144,\"draw_above_always_front\":false,\"runtime_type\":\"TemporaryAnimatedSprite\"}],\"light_sources\":[],\"weather_debris_count\":0}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    for (int i = 0; i < 40 && !File.Exists(socket); i++)
        await Task.Delay(50, cts.Token);

    using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);

    var runner = new ScenarioRunner(client);
    var report = await runner.RunAsync(new ScenarioSpec
    {
        Name = "wait_visual_effects",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.visual_effects",
                Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"temporary_sprites\":{\"texture_asset\":\"LooseSprites/Cursors\",\"source_rect\":[372,1956,10,10],\"min_count\":1},\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.Equal(2, polls);
    Assert.DoesNotContain("wait.visual_effects", calls);
    Assert.Contains("state.visual_effects", calls);
    Assert.Equal("Custom_GrandpasGrove", lastParams!.Value.GetProperty("location").GetString());

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}

[Fact]
public async Task WaitVisualEffects_PollsStateUntilLightAndAmbientMatch()
{
    var socket = SocketPath();
    var polls = 0;
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.visual_effects" => JsonDocument.Parse(polls++ == 0
                        ? "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[255,255,255,255],\"temporary_sprites\":[],\"light_sources\":[],\"weather_debris_count\":0}"
                        : "{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[8,9,10,255],\"temporary_sprites\":[],\"light_sources\":[{\"id\":\"SVE_FH_Lantern\",\"position\":[320,512],\"radius\":2.5,\"color\":[255,220,160,255],\"texture_index\":4,\"context\":\"MapLight\"}],\"weather_debris_count\":0}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    for (int i = 0; i < 40 && !File.Exists(socket); i++)
        await Task.Delay(50, cts.Token);

    using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);

    var runner = new ScenarioRunner(client);
    var report = await runner.RunAsync(new ScenarioSpec
    {
        Name = "wait_visual_effects_light",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.visual_effects",
                Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[8,9,10,255],\"light_sources\":{\"id_contains\":\"SVE_FH\",\"min_count\":1},\"timeout_ms\":1000,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.True(report.Passed);
    Assert.Equal(2, polls);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}

[Fact]
public async Task WaitVisualEffects_TimeoutIncludesLastObservedCounts()
{
    var socket = SocketPath();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "state.visual_effects" => JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"ambient_light\":[255,255,255,255],\"temporary_sprites\":[{\"texture_asset\":\"LooseSprites/Cursors\",\"source_rect\":[0,0,10,10],\"position\":[0,0],\"motion\":[0,0],\"acceleration\":[0,0],\"color\":[255,255,255,255],\"alpha\":1,\"alpha_fade\":0,\"scale\":1,\"scale_change\":0,\"rotation\":0,\"rotation_change\":0,\"layer_depth\":0,\"draw_above_always_front\":false,\"runtime_type\":\"TemporaryAnimatedSprite\"}],\"light_sources\":[],\"weather_debris_count\":0}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    for (int i = 0; i < 40 && !File.Exists(socket); i++)
        await Task.Delay(50, cts.Token);

    using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);

    var runner = new ScenarioRunner(client);
    var report = await runner.RunAsync(new ScenarioSpec
    {
        Name = "wait_visual_effects_timeout",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.visual_effects",
                Args = JsonDocument.Parse("{\"location\":\"Custom_GrandpasGrove\",\"temporary_sprites\":{\"texture_asset\":\"Maps/SandstormEffect\",\"min_count\":1},\"timeout_ms\":20,\"poll_ms\":1}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    var failure = Assert.Single(report.Failures);
    Assert.Contains("wait.visual_effects timed out after 20ms waiting for at least 1 temporary_sprites in Custom_GrandpasGrove", failure);
    Assert.Contains("last observed 0 matched temporary_sprites out of 1", failure);

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}

[Fact]
public async Task WaitVisualEffects_RejectsInvalidSourceRect()
{
    var socket = SocketPath();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    for (int i = 0; i < 40 && !File.Exists(socket); i++)
        await Task.Delay(50, cts.Token);

    using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);

    var runner = new ScenarioRunner(client);
    var report = await runner.RunAsync(new ScenarioSpec
    {
        Name = "wait_visual_effects_bad_rect",
        Steps = new()
        {
            new ScenarioStep
            {
                Action = "wait.visual_effects",
                Args = JsonDocument.Parse("{\"location\":\"Farm\",\"temporary_sprites\":{\"source_rect\":[1,2,3],\"timeout_ms\":20,\"poll_ms\":1}}").RootElement,
            },
        },
    }, cts.Token);

    Assert.False(report.Passed);
    Assert.Contains("wait.visual_effects requires args.temporary_sprites.source_rect to have 4 values", Assert.Single(report.Failures));

    cts.Cancel();
    try { await serverTask; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Run the runner tests and confirm they fail**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitVisualEffects"
```

Expected: FAIL because `wait.visual_effects` is sent as a raw RPC method and the fake server returns default `{"ok":true}` instead of the runner polling `state.visual_effects`.

- [ ] **Step 3: Wire the new wait action in the step switch**

In `src/Runner/Scenarios/ScenarioRunner.cs`, add a branch immediately after `wait.location_content`:

```csharp
else if (step.Action == "wait.visual_effects")
{
    await InvokeWaitVisualEffectsAsync(step, ct);
}
```

Add a label in `DescribeStep`:

```csharp
"wait.visual_effects" => $"Wait for visual effects in {GetStringArg(step.Args, "location") ?? "current location"}",
```

Add it to `ShouldAutoCaptureStep` as a non-capturing wait:

```csharp
"wait.visual_effects" => false,
```

- [ ] **Step 4: Add wait args and matching helpers**

Add these nested classes near the existing `WaitLocationContentStepArgs` class:

```csharp
private sealed class WaitVisualEffectsStepArgs
{
    public string? Location { get; set; }
    public VisualEffectTemporarySpriteCriteria? TemporarySprites { get; set; }
    public VisualEffectLightSourceCriteria? LightSources { get; set; }
    public int[]? AmbientLight { get; set; }
    public int? WeatherDebrisMinCount { get; set; }
    public int TimeoutMs { get; set; } = 10000;
    public int PollMs { get; set; } = 100;
}

private sealed class VisualEffectTemporarySpriteCriteria
{
    public string? TextureAsset { get; set; }
    public int[]? SourceRect { get; set; }
    public int[]? Color { get; set; }
    public string? RuntimeType { get; set; }
    public float? MinLayerDepth { get; set; }
    public float? MaxLayerDepth { get; set; }
    public int MinCount { get; set; } = 1;
    public int? MaxCount { get; set; }
}

private sealed class VisualEffectLightSourceCriteria
{
    public string? Id { get; set; }
    public string? IdContains { get; set; }
    public string? Context { get; set; }
    public int[]? Color { get; set; }
    public int MinCount { get; set; } = 1;
    public int? MaxCount { get; set; }
}
```

Add these methods after `InvokeWaitLocationContentAsync`:

```csharp
private async Task InvokeWaitVisualEffectsAsync(ScenarioStep step, CancellationToken ct)
{
    var args = step.Args is { ValueKind: JsonValueKind.Object } obj
        ? JsonSerializer.Deserialize<WaitVisualEffectsStepArgs>(obj.GetRawText(), ProtocolJson.Options)
            ?? new WaitVisualEffectsStepArgs()
        : new WaitVisualEffectsStepArgs();

    ValidateWaitVisualEffectsArgs(args);

    JsonElement? request = string.IsNullOrWhiteSpace(args.Location)
        ? null
        : ProtocolJson.ToElement(new VisualEffectsRequest { Location = args.Location });
    var elapsed = Stopwatch.StartNew();
    int lastSpriteMatched = 0;
    int lastSpriteTotal = 0;
    int lastLightMatched = 0;
    int lastLightTotal = 0;
    int lastWeatherDebrisCount = 0;
    bool lastAmbientMatched = args.AmbientLight is null;

    while (elapsed.ElapsedMilliseconds < args.TimeoutMs)
    {
        ct.ThrowIfCancellationRequested();
        var resp = await _session.InvokeAsync("state.visual_effects", request, ct);
        if (resp.Error is { } error)
            throw new InvalidOperationException($"wait.visual_effects failed during state.visual_effects: {error.Message}");

        if (resp.Result is { } root)
        {
            lastSpriteMatched = CountTemporarySpriteMatches(root, args.TemporarySprites, out lastSpriteTotal);
            lastLightMatched = CountLightSourceMatches(root, args.LightSources, out lastLightTotal);
            lastWeatherDebrisCount = GetIntProperty(root, "weather_debris_count") ?? 0;
            lastAmbientMatched = args.AmbientLight is null || IntArrayPropertyMatches(root, "ambient_light", args.AmbientLight);

            if (VisualEffectsCriteriaSatisfied(args, lastSpriteMatched, lastLightMatched, lastWeatherDebrisCount, lastAmbientMatched))
                return;
        }

        await Task.Delay(args.PollMs, ct);
    }

    throw new TimeoutException(
        $"wait.visual_effects timed out after {args.TimeoutMs}ms waiting for {FormatExpectedVisualEffects(args)} in {args.Location ?? "current location"}; " +
        $"last observed {lastSpriteMatched} matched temporary_sprites out of {lastSpriteTotal}, " +
        $"{lastLightMatched} matched light_sources out of {lastLightTotal}, ambient_light matched={lastAmbientMatched}, " +
        $"weather_debris_count={lastWeatherDebrisCount}");
}

private static void ValidateWaitVisualEffectsArgs(WaitVisualEffectsStepArgs args)
{
    if (args.TimeoutMs < 1)
        throw new InvalidOperationException("wait.visual_effects requires args.timeout_ms >= 1");
    if (args.PollMs < 1)
        throw new InvalidOperationException("wait.visual_effects requires args.poll_ms >= 1");
    if (args.TemporarySprites is null && args.LightSources is null && args.AmbientLight is null && args.WeatherDebrisMinCount is null)
        throw new InvalidOperationException("wait.visual_effects requires at least one of args.temporary_sprites, args.light_sources, args.ambient_light, or args.weather_debris_min_count");

    if (args.TemporarySprites is { } sprite)
    {
        ValidateRect("wait.visual_effects requires args.temporary_sprites.source_rect to have 4 values", sprite.SourceRect);
        ValidateColor("wait.visual_effects requires args.temporary_sprites.color to have 4 values", sprite.Color);
        ValidateCount("wait.visual_effects", "temporary_sprites", sprite.MinCount, sprite.MaxCount);
    }

    if (args.LightSources is { } light)
    {
        ValidateColor("wait.visual_effects requires args.light_sources.color to have 4 values", light.Color);
        ValidateCount("wait.visual_effects", "light_sources", light.MinCount, light.MaxCount);
    }

    ValidateColor("wait.visual_effects requires args.ambient_light to have 4 values", args.AmbientLight);
}

private static void ValidateRect(string message, int[]? rect)
{
    if (rect is not null && rect.Length != 4)
        throw new InvalidOperationException(message);
}

private static void ValidateColor(string message, int[]? color)
{
    if (color is not null && color.Length != 4)
        throw new InvalidOperationException(message);
}

private static void ValidateCount(string action, string collection, int minCount, int? maxCount)
{
    if (minCount < 1)
        throw new InvalidOperationException($"{action} requires args.{collection}.min_count >= 1");
    if (maxCount is not null && maxCount < 1)
        throw new InvalidOperationException($"{action} requires args.{collection}.max_count >= 1");
    if (maxCount is not null && maxCount < minCount)
        throw new InvalidOperationException($"{action} requires args.{collection}.max_count >= args.{collection}.min_count");
}

private static bool VisualEffectsCriteriaSatisfied(
    WaitVisualEffectsStepArgs args,
    int spriteMatched,
    int lightMatched,
    int weatherDebrisCount,
    bool ambientMatched)
{
    return CountCriteriaSatisfied(args.TemporarySprites?.MinCount, args.TemporarySprites?.MaxCount, spriteMatched)
        && CountCriteriaSatisfied(args.LightSources?.MinCount, args.LightSources?.MaxCount, lightMatched)
        && (args.WeatherDebrisMinCount is null || weatherDebrisCount >= args.WeatherDebrisMinCount.Value)
        && ambientMatched;
}

private static bool CountCriteriaSatisfied(int? minCount, int? maxCount, int count)
{
    if (minCount is null)
        return true;

    return count >= minCount.Value && (maxCount is null || count <= maxCount.Value);
}

private static int CountTemporarySpriteMatches(JsonElement root, VisualEffectTemporarySpriteCriteria? criteria, out int totalCount)
{
    totalCount = 0;
    if (criteria is null)
        return 0;
    if (!root.TryGetProperty("temporary_sprites", out var array) || array.ValueKind != JsonValueKind.Array)
        return 0;

    var matched = 0;
    foreach (var element in array.EnumerateArray())
    {
        totalCount++;
        if (StringFilterMatches(element, "texture_asset", criteria.TextureAsset)
            && IntArrayPropertyMatches(element, "source_rect", criteria.SourceRect)
            && IntArrayPropertyMatches(element, "color", criteria.Color)
            && StringFilterMatches(element, "runtime_type", criteria.RuntimeType)
            && FloatRangeMatches(element, "layer_depth", criteria.MinLayerDepth, criteria.MaxLayerDepth))
        {
            matched++;
        }
    }

    return matched;
}

private static int CountLightSourceMatches(JsonElement root, VisualEffectLightSourceCriteria? criteria, out int totalCount)
{
    totalCount = 0;
    if (criteria is null)
        return 0;
    if (!root.TryGetProperty("light_sources", out var array) || array.ValueKind != JsonValueKind.Array)
        return 0;

    var matched = 0;
    foreach (var element in array.EnumerateArray())
    {
        totalCount++;
        if (StringFilterMatches(element, "id", criteria.Id)
            && StringContainsFilterMatches(element, "id", criteria.IdContains)
            && StringFilterMatches(element, "context", criteria.Context)
            && IntArrayPropertyMatches(element, "color", criteria.Color))
        {
            matched++;
        }
    }

    return matched;
}

private static bool StringContainsFilterMatches(JsonElement element, string property, string? expectedSubstring)
{
    if (expectedSubstring is null)
        return true;

    return element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && (value.GetString() ?? string.Empty).Contains(expectedSubstring, StringComparison.Ordinal);
}

private static bool IntArrayPropertyMatches(JsonElement element, string property, int[]? expected)
{
    if (expected is null)
        return true;
    if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        return false;

    var actual = array.EnumerateArray()
        .Select(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : int.MinValue)
        .ToArray();
    return actual.SequenceEqual(expected);
}

private static bool FloatRangeMatches(JsonElement element, string property, float? min, float? max)
{
    if (min is null && max is null)
        return true;
    if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var actual))
        return false;
    return (min is null || actual >= min.Value) && (max is null || actual <= max.Value);
}

private static int? GetIntProperty(JsonElement element, string property)
{
    return element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
        ? parsed
        : null;
}

private static string FormatExpectedVisualEffects(WaitVisualEffectsStepArgs args)
{
    var pieces = new List<string>();
    if (args.TemporarySprites is { } sprite)
        pieces.Add($"{FormatExpectedCount(sprite.MinCount, sprite.MaxCount)} temporary_sprites");
    if (args.LightSources is { } light)
        pieces.Add($"{FormatExpectedCount(light.MinCount, light.MaxCount)} light_sources");
    if (args.AmbientLight is not null)
        pieces.Add("ambient_light");
    if (args.WeatherDebrisMinCount is not null)
        pieces.Add($"at least {args.WeatherDebrisMinCount} weather debris");

    return pieces.Count == 0 ? "visual effects" : string.Join(", ", pieces);
}

private static string FormatExpectedCount(int minCount, int? maxCount)
    => maxCount is null
        ? $"at least {minCount}"
        : minCount == maxCount.Value
            ? $"exactly {minCount}"
            : $"between {minCount} and {maxCount.Value}";
```

- [ ] **Step 5: Run runner tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitVisualEffects"
```

Expected: PASS.

- [ ] **Step 6: Commit Task 4**

Run:

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: wait for visual effects in scenarios"
```

---

## Task 5: SVE Visual Effects Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/09-sve-visual-effects.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Confirm SVE branch safety**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded branch --show-current
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short
```

Expected: branch is not `master`; current branch should be `feature/frobby-sve-slice-1-tile-action-warp` unless the user explicitly changed it.

- [ ] **Step 2: Write SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/09-sve-visual-effects.test.json`:

```json
{
  "name": "sve_visual_effects_grandpas_grove",
  "fixture": "m0spike_436515781",
  "config": {
    "seed": 436515781
  },
  "steps": [
    {
      "action": "player.warp",
      "args": {
        "location": "Custom_GrandpasGrove",
        "x": 24,
        "y": 30
      }
    },
    {
      "action": "wait.location",
      "args": {
        "location": "Custom_GrandpasGrove",
        "timeout_ms": 10000,
        "poll_ms": 100
      }
    },
    {
      "action": "wait.visual_effects",
      "args": {
        "location": "Custom_GrandpasGrove",
        "temporary_sprites": {
          "texture_asset": "LooseSprites/Cursors",
          "source_rect": [372, 1956, 10, 10],
          "min_layer_depth": 0.13,
          "max_layer_depth": 0.16,
          "min_count": 1
        },
        "timeout_ms": 15000,
        "poll_ms": 100
      }
    },
    {
      "action": "draw.arm",
      "args": {
        "ticks": 30
      }
    },
    {
      "action": "wait.ms",
      "args": {
        "ms": 500
      }
    },
    {
      "action": "freeze.begin",
      "args": {}
    },
    {
      "action": "screenshot.capture_next_frame",
      "args": {
        "name": "grandpas-grove-visual-effects",
        "timeout_ms": 3000
      }
    },
    {
      "action": "freeze.end",
      "args": {}
    }
  ],
  "assertions": [
    {
      "type": "draw.contains",
      "label": "Grandpa's Grove cauldron-style temporary sprites rendered",
      "filter": {
        "texture_asset": "LooseSprites/Cursors",
        "source_rect": [372, 1956, 10, 10],
        "layer_depth_range": [0.13, 0.16]
      },
      "min_count": 1
    }
  ]
}
```

If SVE moves the player to a different visible tile but the scenario still reaches `Custom_GrandpasGrove`, keep the location and sprite assertions unchanged. The feature under test is temporary sprite state and draw proof, not exact player placement.

- [ ] **Step 3: Update SVE Frobby docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add an entry for scenario 09 near the scenario list:

```markdown
- `tests/sdv/09-sve-visual-effects.test.json`: warps to Grandpa's Grove, waits for SVE temporary animated sprites through `state.visual_effects`, captures a frozen screenshot, and proves the sprite draw through `draw.contains`.
```

Add a command example:

```bash
dotnet run --project /home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj -- \
  repo run \
  --repo-root /home/fintan/stardewRepos/StardewValleyExpanded \
  --headless \
  --mod-set core \
  --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-7-visual-effects \
  tests/sdv/09-sve-visual-effects.test.json
```

- [ ] **Step 4: Run scenario 09 headlessly**

From the Frobby worktree, build once:

```bash
dotnet build sdv-test-framework.slnx
```

Then run the SVE scenario:

```bash
dotnet run --no-build --project src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-7-visual-effects tests/sdv/09-sve-visual-effects.test.json
```

Expected: scenario passes and report directory contains the frozen screenshot plus draw assertion result.

- [ ] **Step 5: If the draw assertion fails but `wait.visual_effects` passes**

Run a one-scenario debug capture:

```bash
dotnet run --no-build --project src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-7-visual-effects-debug tests/sdv/09-sve-visual-effects.test.json
```

Inspect the draw failure details in the generated HTML report. If the report still shows `LooseSprites/Cursors` with source rect `[372,1956,10,10]`, treat the failure as a timing issue and increase the scenario's `draw.arm` ticks from `30` to `60`, then rerun Step 4. If the report does not show that texture/source-rect pair, stop and report the observed draw metadata before changing Frobby; the state RPC already proved the temporary sprite existed, and the next fix needs to target rendering evidence rather than SVE-specific state.

- [ ] **Step 6: Commit SVE scenario/docs**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add tests/sdv/09-sve-visual-effects.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: cover visual effects with frobby"
```

---

## Task 6: Frobby Docs, Capability Status, And Full Verification

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Document the RPC in `docs/rpc-schema.md`**

Add a method section near other `state.*` entries:

````markdown
### `state.visual_effects`

Snapshots live visual-effect state for the current location or a named location.

Request:

```json
{
  "location": "Custom_GrandpasGrove"
}
```

`location` is optional. When omitted, Frobby uses the current location. When a named location is not loaded, the response preserves the requested location name and returns empty temporary sprite data.

Response:

```json
{
  "location": "Custom_GrandpasGrove",
  "ambient_light": [255, 255, 255, 255],
  "temporary_sprites": [
    {
      "texture_asset": "LooseSprites/Cursors",
      "source_rect": [372, 1956, 10, 10],
      "position": [1024, 2048],
      "motion": [0, -0.35],
      "acceleration": [0, 0],
      "color": [240, 248, 255, 255],
      "alpha": 0.45,
      "alpha_fade": 0.0009,
      "scale": 4,
      "scale_change": 0,
      "rotation": 0,
      "rotation_change": 0,
      "layer_depth": 0.144,
      "draw_above_always_front": false,
      "runtime_type": "TemporaryAnimatedSprite"
    }
  ],
  "light_sources": [
    {
      "id": "Example.Light",
      "position": [320, 512],
      "radius": 2.5,
      "color": [255, 220, 160, 255],
      "texture_index": 4,
      "context": "MapLight"
    }
  ],
  "weather_debris_count": 0
}
```

Use this for state-level evidence that animated sprites or lighting exist. Use `draw.*` or bitmap assertions for final rendered proof.
````

- [ ] **Step 2: Document the DSL helper**

In `docs/dsl-quickstart.md`, add:

````markdown
```csharp
var effects = await State.VisualEffects("Custom_GrandpasGrove");
Assert.Contains(effects.TemporarySprites, sprite =>
    sprite.TextureAsset == "LooseSprites/Cursors"
    && sprite.SourceRect is [372, 1956, 10, 10]);
```
````

- [ ] **Step 3: Document the scenario wait in `README.md`**

Add to the scenario action examples:

```json
{
  "action": "wait.visual_effects",
  "args": {
    "location": "Custom_GrandpasGrove",
    "temporary_sprites": {
      "texture_asset": "LooseSprites/Cursors",
      "source_rect": [372, 1956, 10, 10],
      "min_count": 1
    },
    "timeout_ms": 15000,
    "poll_ms": 100
  }
}
```

Add one sentence:

```markdown
`wait.visual_effects` is runner-side polling over `state.visual_effects`; it is useful for animated sprites and lighting that may appear after location update ticks, while final rendering should still be asserted through `draw.*` or screenshot/bitmap tools.
```

- [ ] **Step 4: Update Slice 7 status**

In `SVE_FROBBY_CAPABILITY_TODO.md`, update Slice 7 from planning to done:

```markdown
- [x] Done: Slice 7, sprites, temporary animations, lighting, and weather-like visual effects.
  - Design spec: `docs/superpowers/specs/2026-05-08-sve-slice-7-visual-effects-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-08-sve-slice-7-visual-effects.md`.
  - SVE pressure: temporary animated sprites, custom cauldron effects, map lighting changes, recolors, mist effects, and location-specific ambience.
  - Frobby goal: expose enough render/state metadata to assert animated sprites and lighting effects without brittle whole-screen diffs.
  - Done: `state.visual_effects`, runner-side `wait.visual_effects`, DSL access, and SVE scenario 09 against Grandpa's Grove temporary sprites.
```

- [ ] **Step 5: Run focused Frobby tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter FullyQualifiedName~VisualEffectsStateSerializationTests
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~StateVisualEffectsHandlerTests
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --filter FullyQualifiedName~StateTests.VisualEffects
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~WaitVisualEffects"
```

Expected: all PASS.

- [ ] **Step 6: Run full Frobby suite**

Run:

```bash
dotnet test sdv-test-framework.slnx
```

Expected: PASS with existing skipped live tests unchanged.

- [ ] **Step 7: Run neutral Starberg smoke**

Run a small Starberg smoke after adding the neutral RPC to make sure existing reports still work:

```bash
dotnet run --no-build --project src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/stonks --headless --mod-set core --report-dir /tmp/starberg-frobby-results-0.1.0/slice-7-smoke tests/sdv/01-starberg-terminal-open.test.json
```

Expected: PASS.

- [ ] **Step 8: Run SVE scenario 09**

Run:

```bash
dotnet run --no-build --project src/Runner/Runner.csproj -- repo run --repo-root /home/fintan/stardewRepos/StardewValleyExpanded --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-7-visual-effects tests/sdv/09-sve-visual-effects.test.json
```

Expected: PASS.

- [ ] **Step 9: Scan Frobby production/docs for SVE-specific leakage**

Run:

```bash
rg -n "FlashShifter|StardewValleyExpanded|Custom_GrandpasGrove|Custom_CrimsonBadlands|SandstormEffect" src tests README.md docs/rpc-schema.md docs/dsl-quickstart.md
```

Expected: no output. SVE-specific strings may remain in `SVE_FROBBY_CAPABILITY_TODO.md`, specs, and this plan because those are planning/proof documents.

- [ ] **Step 10: Commit Frobby docs/status**

Run:

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md README.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document visual effects testing"
```

---

## Final Verification And Merge

- [ ] **Step 1: Verify Frobby branch history and status**

Run:

```bash
git status --short
git log --oneline --decorate -8
```

Expected: clean Frobby worktree on `feature/sve-slice-7-visual-effects` with Task 1-6 commits present.

- [ ] **Step 2: Merge Frobby feature branch to main**

Run:

```bash
git switch main
git merge --no-ff feature/sve-slice-7-visual-effects
```

Expected: merge succeeds.

- [ ] **Step 3: Leave SVE branch unmerged**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded branch --show-current
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short
```

Expected: SVE remains on its feature branch or has committed work on a non-master branch. Do not merge SVE into `master`.

---

## Self-Review

Spec coverage:

- `state.visual_effects`: Task 1 and Task 2.
- Temporary animated sprite metadata: Task 1 and Task 2.
- Ambient light, active light sources, weather-like count: Task 1 and Task 2.
- Runner `wait.visual_effects`: Task 4.
- Render proof remains draw/screenshot: Task 5.
- SVE proof scenario: Task 5.
- Frobby neutral docs and leakage scan: Task 6.
- No SVE master merge: Task 5 and final verification.

Placeholder scan:

- The plan contains no open implementation placeholders. The only `TODO` string appears in the real file name `SVE_FROBBY_CAPABILITY_TODO.md`.

Type consistency:

- Protocol DTO names used by the DSL, handler, and runner are defined in Task 1.
- Handler interfaces used by tests are defined in Task 2.
- Runner wait argument names serialize to the scenario JSON fields through `ProtocolJson` snake_case conversion.
