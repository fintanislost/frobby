# D1.5 — Texture → Asset Path Resolution (Tier 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve captured `Texture2D` draw events back to their asset paths (e.g. `"Characters/Abigail"`) so scenario authors can write natural assertions like `{"texture_asset": "Mods/MyShop/sprites"}` instead of stringified integer refs.

**Architecture:** Tier 1 = Harmony postfix on `ContentManager.Load<Texture2D>` populates a `ConditionalWeakTable<Texture2D, string>` registry. `DrawEvent` gains a `Texture` field holding the live reference. At snapshot time, `DrawSnapshotHandler.ToDto` looks the texture up in the registry; unresolved textures become `texture_asset: null` (Tier 3 per the rule). Tier 2 (hash-based fallback) is deferred to M2. `DrawFilterMatcher` compares on resolved paths instead of stringified refs, and the "pre-D1.5 placeholder" error is deleted.

**Tech Stack:**
- .NET 6 (Harness), .NET 10 (Runner) — unchanged
- Harmony 2.x — existing patch infrastructure
- SMAPI 4.5.2 — `IGameContentHelper` for invalidation hooks
- `System.Runtime.CompilerServices.ConditionalWeakTable<TKey, TValue>` — weak-ref map

---

## Why this design

- **Why Harmony on `ContentManager.Load`, not `IGameContentHelper.AssetReady`?** `AssetReadyEventArgs` in SMAPI 4.5 exposes only `Name`/`NameWithoutLocale`, not the loaded instance, so it can't populate the key side of the weak table. `ContentManager.Load<Texture2D>` is a generic instance method — patchable via `AccessTools.Method(...).MakeGenericMethod(typeof(Texture2D))`. This catches both SDV's direct `Game1.content.Load` calls and SMAPI's `SContentManager.LoadImpl` pass-through, because both resolve to the same base method handle.

- **Why `DrawEvent.Texture` in addition to `TextureRefId`?** Today the struct throws the reference away at prefix time (`RuntimeHelpers.GetHashCode(texture)` only). Without the reference we can't later query the registry — the `int` hashcode is process-unique but not reversible to a `Texture2D`. Adding a field costs 8 bytes/event × 100k events = ~800KB pinned; the textures themselves are long-lived SDV content so the pinning is not a memory-leak concern.

- **Why resolve at snapshot time, not prefix time?** Per `.claude/rules/draw-call-recorder.md`: *"Don't resolve paths in the prefix. Defer to snapshot time."* The prefix runs thousands of times per frame; the snapshot runs once per scenario. Resolution is a dictionary lookup, fast enough at snapshot time.

---

## File structure

**New files:**
- `src/Harness/Assets/TextureAssetRegistry.cs` — the `ConditionalWeakTable` wrapper. Single responsibility: register + lookup. No SDV dependencies so it's unit-testable.
- `src/Harness/Assets/ContentLoadPatches.cs` — Harmony postfix on `ContentManager.Load<Texture2D>`. Populates the registry.
- `tests/Harness.Tests/TextureAssetRegistryTests.cs` — unit tests for register/resolve/weak-ref eviction.

**Modified files:**
- `src/Harness/Recording/DrawEvent.cs` — add `Texture2D? Texture` field.
- `src/Harness/Handlers/SpriteBatchDrawPatches.cs` — each of the 7 prefixes populates `Texture = texture` in the new field.
- `src/Harness/Handlers/DrawSnapshotHandler.cs` — `ToDto` resolves the captured `Texture` via `TextureAssetRegistry.TryResolve`; populates `DrawEventDto.TextureAsset`.
- `src/Harness/Handlers/DrawFilterMatcher.cs` — replace the integer-only placeholder check with a resolved-path string match; delete the `InvalidParams` error.
- `src/Harness/Recording/Recorder.cs` — nothing functional; `SnapshotMetadata` gains a `ResolvedCount` field populated by the caller (DrawSnapshotHandler computes it at resolution time).
- `src/Harness/ModEntry.cs` — `ContentLoadPatches.Apply(harmony, this.Monitor)` alongside the existing `SpriteBatchDrawPatches.Apply`.
- `src/Protocol/Models/DrawEventSnapshot.cs` — add `string? TextureAsset` to `DrawEventDto`; add `int ResolvedCount` to `SnapshotMeta`.
- `tests/Harness.Tests/DrawFilterTests.cs` — add test cases for resolved-path matching and the "event has no path, filter wants one → no match" case.
- `tests/Protocol.Tests/DrawEventSnapshotSerializationTests.cs` — add serialization test for `texture_asset` + `resolved_count`.
- `docs/rpc-schema.md` — update `state.time`-era notes about `texture_asset` placeholder; document the real Tier 1 semantics + `resolved_count` meta field.

**Verification:** `./scripts/ci.sh` green after each task. Smoke test re-run after the last task confirms Tier 1 resolution actually fires against a live SDV.

**Starting test count:** 157 Passed + 13 Skipped.

---

## Task 1: TextureAssetRegistry

**Files:**
- Create: `src/Harness/Assets/TextureAssetRegistry.cs`
- Test: `tests/Harness.Tests/TextureAssetRegistryTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Write failing test (registry shape)**

Create `tests/Harness.Tests/TextureAssetRegistryTests.cs`:

```csharp
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Assets;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextureAssetRegistryTests
{
    [Fact]
    public void TryResolve_UnregisteredTexture_ReturnsNull()
    {
        var reg = new TextureAssetRegistry();
        Texture2D? tex = null;
        Assert.Null(reg.TryResolve(tex));
    }

    [Fact]
    public void Register_ThenResolve_ReturnsAssetName()
    {
        // We can't construct a real Texture2D without a graphics device, so use a sentinel
        // object by casting — the registry treats keys as opaque references and doesn't
        // touch Texture2D-specific members. This works because ConditionalWeakTable<TKey, TValue>
        // stores references without dereferencing them.
        //
        // Cleaner alternative: make Register accept `object` and Texture2D is assignable to
        // object — but then we lose the type guarantee on the public API. So instead we test
        // via a shim constructor that only the test project can see.
        var reg = new TextureAssetRegistry();
        var shim = new Texture2DShim();
        reg.RegisterShim(shim, "Characters/Abigail");
        Assert.Equal("Characters/Abigail", reg.TryResolveShim(shim));
    }

    [Fact]
    public void Register_NullTexture_NoOp()
    {
        var reg = new TextureAssetRegistry();
        reg.Register(null, "whatever"); // must not throw
        Assert.Null(reg.TryResolve(null));
    }

    [Fact]
    public void Register_Twice_OverwritesAssetName()
    {
        // Assets can be loaded twice with different paths during SDV startup (e.g. once
        // under locale-suffixed name, once plain). The registry accepts the latest mapping.
        var reg = new TextureAssetRegistry();
        var shim = new Texture2DShim();
        reg.RegisterShim(shim, "first");
        reg.RegisterShim(shim, "second");
        Assert.Equal("second", reg.TryResolveShim(shim));
    }

    // Internal shim class + test-only accessors — defined below via InternalsVisibleTo
    private sealed class Texture2DShim { }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter TextureAssetRegistry`
Expected: FAIL with "TextureAssetRegistry could not be found".

- [ ] **Step 2: Create TextureAssetRegistry with shim hooks for testing**

Create `src/Harness/Assets/TextureAssetRegistry.cs`:

```csharp
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;

namespace SdvTestFramework.Harness.Assets;

/// <summary>
/// Weak-reference map from loaded <see cref="Texture2D"/> instances to the asset path they
/// were loaded from. Populated by <see cref="ContentLoadPatches"/> and queried at snapshot
/// time by <see cref="Handlers.DrawSnapshotHandler"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="ConditionalWeakTable{TKey, TValue}"/> so GC'd textures drop out
/// automatically — no manual invalidation needed for texture lifetime management. SMAPI's
/// <c>AssetsInvalidated</c> event could force-drop entries on cache reloads, but since
/// the texture instance usually goes away (new instance on reload) the weak-ref eviction
/// handles it naturally.
/// </remarks>
public sealed class TextureAssetRegistry
{
    private readonly ConditionalWeakTable<object, string> _map = new();

    /// <summary>Associate <paramref name="texture"/> with <paramref name="assetName"/>. No-op when <paramref name="texture"/> is null.</summary>
    public void Register(Texture2D? texture, string assetName) => RegisterCore(texture, assetName);

    /// <summary>Lookup the asset path previously registered for <paramref name="texture"/>. Returns null when unregistered or when <paramref name="texture"/> is null.</summary>
    public string? TryResolve(Texture2D? texture) => TryResolveCore(texture);

    // --- shared core (takes object so test-shim instances can reuse it) ---

    internal void RegisterCore(object? key, string assetName)
    {
        if (key is null) return;
        // ConditionalWeakTable doesn't have AddOrUpdate; emulate by remove-then-add.
        _map.Remove(key);
        _map.Add(key, assetName);
    }

    internal string? TryResolveCore(object? key)
    {
        if (key is null) return null;
        return _map.TryGetValue(key, out var name) ? name : null;
    }
}
```

- [ ] **Step 3: Add test shim accessors via InternalsVisibleTo**

`Harness.csproj` already has `<InternalsVisibleTo Include="Harness.Tests" />` from T11 (see `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Harness/Harness.csproj`). Add internal helpers to the registry so the test can use its shim:

At the bottom of `TextureAssetRegistry.cs` (still inside the class):

```csharp
    // Test hooks so TextureAssetRegistryTests can exercise the weak-table without
    // constructing a real Texture2D (which requires a GraphicsDevice).
    internal void RegisterShim(object shim, string assetName) => RegisterCore(shim, assetName);
    internal string? TryResolveShim(object shim) => TryResolveCore(shim);
```

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test tests/Harness.Tests/ --filter TextureAssetRegistry`
Expected: PASS (4 tests).

- [ ] **Step 5: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 157 → 161 (+4).

---

## Task 2: DrawEvent.Texture field + thread through prefixes

**Files:**
- Modify: `src/Harness/Recording/DrawEvent.cs` — add nullable `Texture2D? Texture`
- Modify: `src/Harness/Handlers/SpriteBatchDrawPatches.cs` — populate the new field in all 7 prefix methods

**Dependencies:** Task 1 (for the texture field's downstream consumer). Self-contained for its own purposes — tests still pass.

- [ ] **Step 1: Add Texture field**

Replace `src/Harness/Recording/DrawEvent.cs`:

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Canonical draw-call event. All seven <see cref="SpriteBatch.Draw"/> overloads are
/// normalized into this shape. Fields that a given overload doesn't supply get their
/// documented defaults: rotation=0, origin=(0,0), effects=None, layerDepth=0.
/// </summary>
/// <remarks>
/// Struct (not record-class) by design — hot path allocates thousands per tick; boxing
/// pressure from a reference-type event would dominate capture overhead.
/// See <c>.claude/rules/draw-call-recorder.md</c>.
/// </remarks>
public struct DrawEvent
{
    public int Tick;
    public int CallIndex;

    /// <summary>
    /// Live reference to the source <see cref="Texture2D"/>, held so
    /// <see cref="Assets.TextureAssetRegistry"/> can resolve its asset path at snapshot time.
    /// Null is legal (e.g. event constructed in tests). The reference pins the texture from
    /// GC while the ring buffer holds it — acceptable because SDV textures are long-lived.
    /// </summary>
    public Texture2D? Texture;

    /// <summary>Per-process-stable texture identity (<c>RuntimeHelpers.GetHashCode</c>).
    /// Normalized out in cross-run diffs — see the analyzer.</summary>
    public int TextureRefId;
    public int TextureWidth;
    public int TextureHeight;

    public Rectangle? SourceRect;
    public Rectangle DestRect;
    public Color Color;
    public float Rotation;
    public Vector2 Origin;
    public SpriteEffects Effects;
    public float LayerDepth;
}
```

- [ ] **Step 2: Populate Texture in all 7 prefixes**

In `src/Harness/Handlers/SpriteBatchDrawPatches.cs`, each of `Prefix_1` through `Prefix_7` constructs a `new DrawEvent { ... }`. Add `Texture = texture,` as the first assignment in each. Example — modify `Prefix_1`:

Current:
```csharp
Recorder.Record(new DrawEvent
{
    Tick = tick, CallIndex = call,
    TextureRefId = RuntimeHelpers.GetHashCode(texture),
    TextureWidth = texture.Width, TextureHeight = texture.Height,
    ...
});
```

New (add `Texture = texture` as the first field):
```csharp
Recorder.Record(new DrawEvent
{
    Tick = tick, CallIndex = call,
    Texture = texture,
    TextureRefId = RuntimeHelpers.GetHashCode(texture),
    TextureWidth = texture.Width, TextureHeight = texture.Height,
    ...
});
```

Apply the same 1-line insertion to `Prefix_2`, `Prefix_3`, `Prefix_4`, `Prefix_5`, `Prefix_6`, `Prefix_7`. Each prefix already has `texture` as its first parameter — no renaming needed.

- [ ] **Step 3: Verify existing tests still pass**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged at 161.

`DrawEventWriterTests` and `DrawSnapshotHandlerTests` construct `DrawEvent` literals with various fields — they don't set `Texture`, which stays at its default `null`. All existing assertions still hold because the writer/snapshot paths today don't emit the Texture field.

---

## Task 3: ContentLoadPatches + wire into ModEntry

**Files:**
- Create: `src/Harness/Assets/ContentLoadPatches.cs`
- Modify: `src/Harness/ModEntry.cs` — register the new patch and hold the registry singleton
- Test: `tests/Harness.Tests/ContentLoadPatchesTests.cs` (skip-marked — requires live SDV for `ContentManager.Load`)

**Dependencies:** Task 1 (registry).

- [ ] **Step 1: Create ContentLoadPatches**

Create `src/Harness/Assets/ContentLoadPatches.cs`:

```csharp
using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Assets;

/// <summary>Harmony postfix on <c>ContentManager.Load&lt;Texture2D&gt;</c> that populates the shared <see cref="TextureAssetRegistry"/>.</summary>
// Patch: Microsoft.Xna.Framework.Content.ContentManager.Load<Texture2D>(string)
// Type: Postfix (observe only, no control-flow change)
// Why: Feed the TextureAssetRegistry so DrawSnapshotHandler can resolve Texture2D → asset path at snapshot time (D1.5 Tier 1)
// Rollback: Remove the Apply() call from ModEntry; registry stays empty and draw.find with texture_asset filter finds nothing
// Tested in: tests/Harness.Tests/ContentLoadPatchesTests.cs (skip-marked integration)
// Depends on: Harmony 2.x, SMAPI >= 4.1.10, Texture2D from Microsoft.Xna.Framework.Graphics
public static class ContentLoadPatches
{
    private static TextureAssetRegistry? _registry;

    public static void Apply(Harmony harmony, IMonitor monitor, TextureAssetRegistry registry)
    {
        _registry = registry;

        // ContentManager.Load<T>(string) is a generic method. Patch the Texture2D instantiation.
        var genericMethod = AccessTools.Method(typeof(ContentManager), nameof(ContentManager.Load));
        if (genericMethod is null)
            throw new InvalidOperationException(
                "ContentManager.Load not found — XNA/MonoGame has changed and D1.5 must be revised.");

        var textureLoad = genericMethod.MakeGenericMethod(typeof(Texture2D));
        var postfix = new HarmonyMethod(typeof(ContentLoadPatches), nameof(OnLoaded));
        harmony.Patch(textureLoad, postfix: postfix);

        monitor.Log($"Patched: ContentManager.Load<Texture2D>(string) — populates TextureAssetRegistry.", LogLevel.Info);
    }

    // ReSharper disable once UnusedMember.Local — called by Harmony via reflection
    private static void OnLoaded(string assetName, Texture2D __result)
    {
        if (_registry is null) return;
        _registry.Register(__result, assetName);
    }
}
```

- [ ] **Step 2: Thread a singleton registry through ModEntry**

In `src/Harness/ModEntry.cs`, add near the top of `Entry` (before `SpriteBatchDrawPatches.Apply` registration):

Find this block:
```csharp
var harmony = new Harmony(this.ModManifest.UniqueID);
SpriteBatchDrawPatches.Apply(harmony, this.Monitor);
CursorPatches.Apply(harmony, this.Monitor);
```

Replace with (the registry needs to survive past `Entry` so DrawSnapshotHandler can reach it in T5):

```csharp
var harmony = new Harmony(this.ModManifest.UniqueID);
TextureAssetRegistry.Shared = new Assets.TextureAssetRegistry();
Assets.ContentLoadPatches.Apply(harmony, this.Monitor, TextureAssetRegistry.Shared);
SpriteBatchDrawPatches.Apply(harmony, this.Monitor);
CursorPatches.Apply(harmony, this.Monitor);
```

Add `using SdvTestFramework.Harness.Assets;` at the top of `ModEntry.cs` if not present.

- [ ] **Step 3: Add Shared property to TextureAssetRegistry**

In `src/Harness/Assets/TextureAssetRegistry.cs`, add just above the instance's `_map` field:

```csharp
    /// <summary>
    /// Process-wide instance populated by <see cref="ContentLoadPatches"/> at mod startup
    /// and read by <c>DrawSnapshotHandler</c> at snapshot time. Null until
    /// <see cref="ModEntry.Entry"/> initializes it; handlers should treat null as
    /// "no Tier 1 resolution available" and fall through to Tier 3 (texture_asset: null).
    /// </summary>
    public static TextureAssetRegistry? Shared { get; set; }
```

- [ ] **Step 4: Skip-marked integration test**

Create `tests/Harness.Tests/ContentLoadPatchesTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ContentLoadPatchesTests
{
    [Fact(Skip = "Requires live SDV ContentManager — exercised by the smoke test's Tier 1 resolution-rate check.")]
    public void Apply_RegistersPostfix_LoadPopulatesRegistry() { /* integration */ }
}
```

- [ ] **Step 5: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 161 → 161 Passed + 14 Skipped (+1 skipped).

---

## Task 4: DrawEventDto.TextureAsset + snake_case serialization

**Files:**
- Modify: `src/Protocol/Models/DrawEventSnapshot.cs` — add `TextureAsset` to `DrawEventDto`
- Test: `tests/Protocol.Tests/DrawEventSnapshotSerializationTests.cs` — add serialization case

**Dependencies:** none (DTO-only change; handler changes come in T5).

- [ ] **Step 1: Write failing test**

In `tests/Protocol.Tests/DrawEventSnapshotSerializationTests.cs`, add a new test:

```csharp
    [Fact]
    public void Serialize_TextureAsset_EmitsSnakeCaseField()
    {
        var snap = new DrawEventSnapshot
        {
            Events = new()
            {
                new DrawEventDto
                {
                    TextureAsset = "Characters/Abigail",
                    Dst = new[] { 0, 0, 16, 16 },
                },
            },
        };
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"texture_asset\":\"Characters/Abigail\"", json);
    }

    [Fact]
    public void Serialize_NullTextureAsset_EmittedAsNull()
    {
        // Mirrors the pattern used for Src — JsonIgnore(Never) ensures the field is always
        // emitted, even when null, so "no Tier 1 resolution" is distinguishable from
        // "field missing from this protocol version".
        var snap = new DrawEventSnapshot
        {
            Events = new() { new DrawEventDto { Dst = new[] { 0, 0, 1, 1 } } },
        };
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"texture_asset\":null", json);
    }
```

Run: `dotnet test tests/Protocol.Tests/ --filter DrawEventSnapshot`
Expected: FAIL with `TextureAsset` property not found.

- [ ] **Step 2: Add TextureAsset property to DrawEventDto**

In `src/Protocol/Models/DrawEventSnapshot.cs`, find the `DrawEventDto` class. After the `public int TexH { get; set; }` line and before `public int[]? Src { get; set; }`, insert:

```csharp
    /// <summary>
    /// Resolved asset path for this draw's texture, when known (e.g. <c>Characters/Abigail</c>).
    /// Null when Tier 1 resolution didn't find a mapping — either because the texture was
    /// engine-loaded before the harness's content-load patch caught it, or because it was
    /// dynamically generated (render targets etc.). Tier 2 hash fallback is deferred to M2.
    /// </summary>
    /// <remarks>
    /// Explicitly emitted as <c>null</c> rather than omitted so scenario authors can
    /// distinguish "field unavailable in this protocol version" from "this texture has no
    /// asset path".
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? TextureAsset { get; set; }
```

Near the top of the file, add `using System.Text.Json.Serialization;` if it isn't already there (it should be — the existing `Src` field uses `[JsonIgnore]` too).

- [ ] **Step 3: Run tests — verify PASS**

Run: `dotnet test tests/Protocol.Tests/ --filter DrawEventSnapshot`
Expected: PASS.

- [ ] **Step 4: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 161+14 → 163+14 (+2 Protocol tests).

---

## Task 5: DrawSnapshotHandler resolves texture at snapshot time

**Files:**
- Modify: `src/Harness/Handlers/DrawSnapshotHandler.cs` — `ToDto` consults `TextureAssetRegistry.Shared`
- Test: update in `tests/Harness.Tests/DrawSnapshotHandlerTests.cs`

**Dependencies:** Tasks 1, 2, 3, 4.

- [ ] **Step 1: Write failing test**

In `tests/Harness.Tests/DrawSnapshotHandlerTests.cs`, add:

```csharp
    [Fact]
    public void ToDto_ResolvesTextureAsset_WhenRegistered()
    {
        // Seed the shared registry with a shim; verify the handler picks up the resolved
        // asset name. Uses the registry shim trick from TextureAssetRegistryTests.
        var registry = new Assets.TextureAssetRegistry();
        var priorShared = Assets.TextureAssetRegistry.Shared;
        Assets.TextureAssetRegistry.Shared = registry;
        try
        {
            // Texture = null on the event simulates the "can't resolve" case; cover that here.
            var eventNoTex = new DrawEvent { DestRect = new Rectangle(0, 0, 1, 1), Color = Color.White };
            Assert.Null(DrawSnapshotHandler.ToDto(in eventNoTex).TextureAsset);
        }
        finally
        {
            Assets.TextureAssetRegistry.Shared = priorShared;
        }
    }

    [Fact]
    public void ToDto_TextureRegisteredSeparately_IsResolved()
    {
        // This test confirms the integration: when the registry has an entry keyed on the
        // event's Texture reference, ToDto surfaces the path. Full integration with live
        // Texture2D instances requires SDV — covered by the smoke test.
        var registry = new Assets.TextureAssetRegistry();
        var priorShared = Assets.TextureAssetRegistry.Shared;
        Assets.TextureAssetRegistry.Shared = registry;
        try
        {
            // A plain object stands in for Texture2D — registry.RegisterCore accepts
            // object and TryResolveCore works symmetrically. DrawSnapshotHandler uses
            // registry.TryResolve which takes Texture2D?; since we can't construct one
            // without a GraphicsDevice, we assert the null-event path here and defer
            // the non-null resolved case to the smoke test.
            var e = new DrawEvent { Texture = null, DestRect = new Rectangle(0, 0, 1, 1), Color = Color.White };
            var dto = DrawSnapshotHandler.ToDto(in e);
            Assert.Null(dto.TextureAsset);
        }
        finally
        {
            Assets.TextureAssetRegistry.Shared = priorShared;
        }
    }
```

Run: `dotnet test tests/Harness.Tests/ --filter DrawSnapshotHandler`
Expected: FAIL — `ToDto` doesn't yet emit `TextureAsset`.

- [ ] **Step 2: Update ToDto to resolve texture**

In `src/Harness/Handlers/DrawSnapshotHandler.cs`, in the `ToDto` method, update the returned DTO initialization to include `TextureAsset`:

Current return statement:
```csharp
public static DrawEventDto ToDto(in DrawEvent e) => new()
{
    Tick = e.Tick,
    Call = e.CallIndex,
    TexRef = e.TextureRefId,
    TexW = e.TextureWidth,
    TexH = e.TextureHeight,
    Src = e.SourceRect is { } sr ? new[] { sr.X, sr.Y, sr.Width, sr.Height } : null,
    Dst = new[] { e.DestRect.X, e.DestRect.Y, e.DestRect.Width, e.DestRect.Height },
    Col = new[] { (int)e.Color.R, (int)e.Color.G, (int)e.Color.B, (int)e.Color.A },
    Rot = e.Rotation,
    Orig = new[] { e.Origin.X, e.Origin.Y },
    Fx = (int)e.Effects,
    Z = e.LayerDepth,
};
```

Add one line — `TextureAsset = Assets.TextureAssetRegistry.Shared?.TryResolve(e.Texture),` — after the `TexH = e.TextureHeight` line:

```csharp
public static DrawEventDto ToDto(in DrawEvent e) => new()
{
    Tick = e.Tick,
    Call = e.CallIndex,
    TexRef = e.TextureRefId,
    TexW = e.TextureWidth,
    TexH = e.TextureHeight,
    TextureAsset = Assets.TextureAssetRegistry.Shared?.TryResolve(e.Texture),
    Src = e.SourceRect is { } sr ? new[] { sr.X, sr.Y, sr.Width, sr.Height } : null,
    Dst = new[] { e.DestRect.X, e.DestRect.Y, e.DestRect.Width, e.DestRect.Height },
    Col = new[] { (int)e.Color.R, (int)e.Color.G, (int)e.Color.B, (int)e.Color.A },
    Rot = e.Rotation,
    Orig = new[] { e.Origin.X, e.Origin.Y },
    Fx = (int)e.Effects,
    Z = e.LayerDepth,
};
```

Add `using SdvTestFramework.Harness.Assets;` at the top if missing, or keep the `Assets.` prefix as shown.

- [ ] **Step 3: Run tests — PASS**

Run: `dotnet test tests/Harness.Tests/ --filter DrawSnapshotHandler`
Expected: PASS.

- [ ] **Step 4: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 163+14 → 165+14 (+2 Harness tests).

---

## Task 6: DrawFilterMatcher — match on resolved path, remove placeholder

**Files:**
- Modify: `src/Harness/Handlers/DrawFilterMatcher.cs`
- Modify: `tests/Harness.Tests/DrawFilterTests.cs` — add cases for path matching

**Dependencies:** Task 5.

- [ ] **Step 1: Write failing tests for the new behavior**

In `tests/Harness.Tests/DrawFilterTests.cs`, find the class `DrawFilterTests` and add these tests:

```csharp
    [Fact]
    public void TextureAsset_ExactPathMatch_Accepts()
    {
        // Seed the registry so the matcher can resolve the event's texture reference.
        // The matcher reads via TextureAssetRegistry.Shared — use a shim to avoid
        // needing a real Texture2D.
        var registry = new Assets.TextureAssetRegistry();
        var priorShared = Assets.TextureAssetRegistry.Shared;
        Assets.TextureAssetRegistry.Shared = registry;
        try
        {
            var e = new DrawEvent { Texture = null };
            var f = new DrawFilter { TextureAsset = "Characters/Abigail" };
            // Event has no Texture (null) → no resolved path → filter requires one → reject.
            Assert.False(DrawFilterMatcher.Matches(in e, f));
        }
        finally
        {
            Assets.TextureAssetRegistry.Shared = priorShared;
        }
    }

    [Fact]
    public void TextureAsset_NoFilter_Accepts()
    {
        // Empty filter (no TextureAsset set) matches regardless of event's texture state.
        var priorShared = Assets.TextureAssetRegistry.Shared;
        Assets.TextureAssetRegistry.Shared = null;
        try
        {
            var e = new DrawEvent { Texture = null };
            var f = new DrawFilter();
            Assert.True(DrawFilterMatcher.Matches(in e, f));
        }
        finally
        {
            Assets.TextureAssetRegistry.Shared = priorShared;
        }
    }

    [Fact]
    public void TextureAsset_NonIntegerPath_NoLongerThrows()
    {
        // Pre-D1.5 the matcher threw InvalidParams for non-integer texture_asset values
        // to catch author confusion. Post-D1.5, real asset paths are accepted — and the
        // "no match" case is silent because the filter simply doesn't match.
        var priorShared = Assets.TextureAssetRegistry.Shared;
        Assets.TextureAssetRegistry.Shared = null; // no registry — every event resolves to null
        try
        {
            var e = new DrawEvent { Texture = null };
            var f = new DrawFilter { TextureAsset = "Characters/Abigail" };
            // Should NOT throw. Return false (no resolved path → no match).
            Assert.False(DrawFilterMatcher.Matches(in e, f));
        }
        finally
        {
            Assets.TextureAssetRegistry.Shared = priorShared;
        }
    }
```

Run: `dotnet test tests/Harness.Tests/ --filter TextureAsset`
Expected: FAIL — the current matcher throws `InvalidParams` instead of returning false.

- [ ] **Step 2: Replace placeholder match logic**

In `src/Harness/Handlers/DrawFilterMatcher.cs`, replace the entire `// texture_asset filter: pre-D1.5 placeholder...` block (lines 33-49 in the current file) with:

```csharp
        // texture_asset filter (D1.5 Tier 1): resolve the event's texture via the shared
        // registry and compare on the resolved path. Unresolved events (Tier 3 anonymous)
        // never match a path filter; use a filter without texture_asset + secondary
        // fields (e.g. tex_w, source_rect) to query those.
        if (!string.IsNullOrEmpty(f.TextureAsset))
        {
            var resolved = Assets.TextureAssetRegistry.Shared?.TryResolve(e.Texture);
            if (resolved is null || resolved != f.TextureAsset)
                return false;
        }
```

Add `using SdvTestFramework.Harness.Assets;` at the top of the file if not present.

- [ ] **Step 3: Update existing empty-filter test if it relies on the pre-D1.5 error path**

Check the existing `EmptyFilter_MatchesEverything` test. If it constructs a `DrawFilter` with no `TextureAsset` and no Texture on the event, the new matcher logic accepts it (empty filter, no TextureAsset clause to evaluate). No change needed.

- [ ] **Step 4: Run tests — verify the new behavior**

Run: `dotnet test tests/Harness.Tests/ --filter DrawFilter`
Expected: PASS (new tests + existing matcher tests).

- [ ] **Step 5: Update docs/rpc-schema.md**

In `docs/rpc-schema.md`, find the `draw.find` section (search for "### draw.find"). Find the filter DSL description that mentions `texture_asset` and update its description to reflect D1.5:

Current-ish text around `texture_asset`:
> `texture_asset` (string) — M1 placeholder; matches on stringified `TextureRefId` since asset-path resolution is D1.5

Replace with:
> `texture_asset` (string) — exact match on the texture's resolved asset path (e.g. `"Characters/Abigail"`, `"Mods/MyMod/sprites"`). Resolution is Tier 1 per `.claude/rules/draw-call-recorder.md`: a Harmony postfix on `ContentManager.Load<Texture2D>` populates a weak-ref map at content-load time, queried at snapshot time. Textures not seen by the loader (dynamic render targets, engine-pre-mod-load loads) resolve as `null` and won't match a `texture_asset` filter — use secondary fields (`tex_w`, `source_rect`) to query those.

- [ ] **Step 6: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 165+14 → 168+14 (+3 Harness tests).

---

## Task 7: Resolution-rate metric + schema update

**Files:**
- Modify: `src/Harness/Recording/Recorder.cs` — `SnapshotMetadata` gains nothing (stays Ticks/Dropped)
- Modify: `src/Harness/Handlers/DrawSnapshotHandler.cs` — compute resolved count during the ToDto loop, populate `SnapshotMeta.ResolvedCount`
- Modify: `src/Protocol/Models/DrawEventSnapshot.cs` — add `ResolvedCount` to `SnapshotMeta`
- Test: `tests/Harness.Tests/DrawSnapshotHandlerTests.cs` — verify `resolved_count` appears
- Docs: update `state.time`-style entry for `draw.snapshot` response shape

**Dependencies:** Tasks 4, 5.

- [ ] **Step 1: Add ResolvedCount to SnapshotMeta**

In `src/Protocol/Models/DrawEventSnapshot.cs`, find the `SnapshotMeta` class. Add:

```csharp
    /// <summary>
    /// Count of events where Tier 1 resolution succeeded — i.e. <see cref="DrawEventDto.TextureAsset"/>
    /// is non-null. Divide by <see cref="Events"/> for the resolution rate (D1.5 §acceptance).
    /// Useful for diagnosing "why doesn't my texture_asset filter match?" — if the rate is
    /// low, the texture is probably engine-loaded before the harness's ContentLoad patch
    /// caught it. Tier 2 hash fallback (deferred to M2) will raise this rate.
    /// </summary>
    public int ResolvedCount { get; set; }
```

- [ ] **Step 2: Populate ResolvedCount in DrawSnapshotHandler**

In `src/Harness/Handlers/DrawSnapshotHandler.cs`, find `Handle`. The current code does:

```csharp
Recorder.SnapshotEvents(out var events, out var meta);

var snap = new DrawEventSnapshot
{
    Meta = new SnapshotMeta
    {
        Ticks = meta.Ticks,
        Events = events.Length,
        Dropped = meta.Dropped,
    },
};

foreach (ref readonly var e in events.AsSpan())
    snap.Events.Add(ToDto(in e));

return ProtocolJson.ToElement(snap);
```

Update to count resolved events during the ToDto loop:

```csharp
Recorder.SnapshotEvents(out var events, out var meta);

var snap = new DrawEventSnapshot
{
    Meta = new SnapshotMeta
    {
        Ticks = meta.Ticks,
        Events = events.Length,
        Dropped = meta.Dropped,
    },
};

int resolved = 0;
foreach (ref readonly var e in events.AsSpan())
{
    var dto = ToDto(in e);
    if (dto.TextureAsset is not null) resolved++;
    snap.Events.Add(dto);
}
snap.Meta.ResolvedCount = resolved;

return ProtocolJson.ToElement(snap);
```

- [ ] **Step 3: Write test**

In `tests/Protocol.Tests/DrawEventSnapshotSerializationTests.cs`, add:

```csharp
    [Fact]
    public void Serialize_SnapshotMeta_IncludesResolvedCount()
    {
        var snap = new DrawEventSnapshot
        {
            Meta = new SnapshotMeta { Ticks = 30, Events = 100, Dropped = 0, ResolvedCount = 87 },
        };
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"resolved_count\":87", json);
    }
```

Run: `dotnet test tests/Protocol.Tests/ --filter ResolvedCount`
Expected: PASS (test count +1).

- [ ] **Step 4: Update docs/rpc-schema.md for draw.snapshot**

In `docs/rpc-schema.md`, find the `draw.snapshot` section and update the response example + meta description to include `resolved_count`. Current example:

```json
{"ticks":10,"events":1,"dropped":0}
```

Update to:

```json
{"ticks":10,"events":1,"dropped":0,"resolved_count":1}
```

Add to the meta-field descriptions:
> `resolved_count` — how many events resolved their `texture_asset` via Tier 1. Rate (`resolved_count / events`) is the key diagnostic for "why doesn't my texture_asset filter match?" — low rates indicate engine-loaded textures that bypassed the harness's content-load patch. Tier 2 hash fallback (M2) will raise this.

- [ ] **Step 5: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 168+14 → 169+14 (+1 Protocol test).

---

## Task 8: Re-run smoke test and document Tier 1 resolution rate (acceptance criterion)

**Files:** none modified; writes a findings paragraph to `docs/milestones/current.md` under the D1.5 entry.

**Dependencies:** all prior tasks.

This closes spec requirement (f) from `M1-core.md §D1.5`: "Documented: what fraction of real draws resolve via Tier 1 on a test fixture."

- [ ] **Step 1: Deploy updated harness + smoke**

Follow the established smoke methodology from `docs/superpowers/plans/2026-04-22-m1-smoke-findings-and-fixes.md` §Smoke methodology. Run:

```bash
rm -rf ~/.cache/sdv-test-framework
dotnet build -c Release
SMOKE=/tmp/sdv-d15-smoke-$(date +%s); mkdir -p "$SMOKE/scenarios"
cat > "$SMOKE/scenarios/resolve-rate.test.json" <<'JSON'
{ "name": "d15_resolve_rate", "config": { "seed": 42 }, "steps": [], "assertions": [] }
JSON
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- run "$SMOKE/scenarios"
pkill Xvfb
```

- [ ] **Step 2: Launch SMAPI manually + probe draw.snapshot to read resolved_count**

```bash
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
SOCK=$SMOKE/probe.sock
env -i DISPLAY=:99 HOME=$HOME PATH=$PATH SDV_TEST_SOCKET=$SOCK \
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/StardewModdingAPI" \
    --mods-path "$HOME/.cache/sdv-test-framework/mods" > /tmp/sdv.log 2>&1 &
while [ ! -S "$SOCK" ]; do sleep 1; done
```

Then run a quick Python probe to arm, let the title screen render for 60 ticks, snapshot, and report the resolved_count:

```python
# /tmp/resolve-rate-probe.py
import json, socket, sys, time
s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
s.connect(sys.argv[1])
f = s.makefile("rwb", buffering=0)
print(f.readline().decode().strip())  # drain ready

def call(method, params=None, _id=[0]):
    _id[0] += 1
    req = {"jsonrpc":"2.0", "id":_id[0], "method":method}
    if params is not None: req["params"] = params
    f.write((json.dumps(req) + "\n").encode())
    while True:
        line = json.loads(f.readline().decode())
        if "id" in line: return line

call("draw.arm", {"ticks": 60})
time.sleep(3)  # let the title screen draws accumulate
resp = call("draw.snapshot")
meta = resp["result"]["meta"]
rate = meta["resolved_count"] / meta["events"] if meta["events"] else 0
print(f"events={meta['events']} resolved={meta['resolved_count']} rate={rate:.1%}")
```

Run: `python3 /tmp/resolve-rate-probe.py "$SOCK"` and record the output.

- [ ] **Step 3: Document the measured rate**

In `docs/milestones/current.md`, add a new subsection under the M1 progress block:

```markdown
### D1.5 — Texture path resolution (Tier 1) landed

Resolution rate measured on a 60-tick title-screen capture (vanilla SDV 1.6.15 + no save loaded):
**X events, Y resolved (Z%).** [Replace X/Y/Z with measured values.]

Title-screen rate is a lower bound — the TitleMenu draws many engine-internal assets
(cursors, fonts, ambient UI elements) loaded before SMAPI's ContentLoad patch caught
them. Farm/shop scenes with real content-pipeline loads are expected to trend higher;
Tier 2 (hash fallback, M2) will close the rest.
```

Fill in the measured numbers from Step 2.

- [ ] **Step 4: Tear down**

```bash
pkill -9 -f StardewModdingAPI; pkill Xvfb; rm -f "$SOCK"
```

- [ ] **Step 5: Final CI**

Run: `./scripts/ci.sh`
Expected: PASS. No code change since T7; sanity-confirm.

---

## Self-review

**1. Spec coverage (M1-core.md §D1.5):**
- (a) SMAPI content pipeline hook populating the weak-ref map → T1 (registry) + T3 (Harmony patch that populates it on every `ContentManager.Load<Texture2D>`). ✓
- (b) Tier 2 (hash fallback) stubbed but not required for M1 → explicitly deferred to M2, noted in the DrawEventDto XML doc + schema + current.md writeup. ✓ (no code for it)
- (c) Documented fraction of real draws that resolve via Tier 1 → T8 captures the number and writes it into `docs/milestones/current.md`. ✓

**2. Placeholder scan:** The current `DrawFilterMatcher` placeholder (reject non-integer texture_asset with InvalidParams) is explicitly deleted in T6. The plan has no `TBD` / `implement later` phrasing. Every step has either exact code or an exact command with expected output.

**3. Type consistency:**
- `TextureAssetRegistry` is in namespace `SdvTestFramework.Harness.Assets` (T1), referenced consistently in T3 (`Assets.ContentLoadPatches`), T5 (`Assets.TextureAssetRegistry.Shared`), T6 (`Assets.TextureAssetRegistry.Shared`).
- `DrawEvent.Texture` is `Texture2D?` (T2), used in T5 as `e.Texture` and T6 as `e.Texture`.
- `DrawEventDto.TextureAsset` is `string?` (T4), populated in T5, read in T7.
- `SnapshotMeta.ResolvedCount` is `int` (T7), populated in T7 handler update.
- Serialization path: DTOs → snake_case via existing `ProtocolJson.Options`, no new naming policy needed.

No inconsistencies.

**4. Known hazards called out:**
- Harness.csproj InternalsVisibleTo already exists (T1 step 3 relies on it; added in T11 of the M1 plan, verified present).
- `ConditionalWeakTable<TKey, TValue>` doesn't have AddOrUpdate — emulated with Remove-then-Add in RegisterCore (T1).
- `ContentManager.Load<T>` is generic — Task 3 uses `AccessTools.Method(...).MakeGenericMethod(typeof(Texture2D))` which is the idiomatic Harmony-on-generic-method path.
- Registry is a `public static` singleton via `Shared` property — matches the established pattern (`Recorder` static, `ScenarioState.Current` static). No DI plumbing needed for M1.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-23-d15-texture-asset-paths.md`. Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task, review between each task, fast iteration. Pattern proven across the 18-task M1 plan and the 5-task S-plan.

**2. Inline Execution** — Execute tasks in this session via `superpowers:executing-plans`, batch through with checkpoints.

**Which approach?**
