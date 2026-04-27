# Draw-Call Recorder

Implementation guidance for the core capture subsystem (spec §4.2).

## Patch targets

`SpriteBatch` has multiple `Draw` overloads. We need to patch all of them. Use `AccessTools.GetDeclaredMethods(typeof(SpriteBatch))` filtered to `Name == "Draw"`, then register one prefix per overload.

Known overloads (XNA/FNA, subject to drift):

1. `Draw(Texture2D, Vector2, Color)`
2. `Draw(Texture2D, Vector2, Rectangle?, Color)`
3. `Draw(Texture2D, Vector2, Rectangle?, Color, float, Vector2, float, SpriteEffects, float)`
4. `Draw(Texture2D, Vector2, Rectangle?, Color, float, Vector2, Vector2, SpriteEffects, float)`
5. `Draw(Texture2D, Rectangle, Color)`
6. `Draw(Texture2D, Rectangle, Rectangle?, Color)`
7. `Draw(Texture2D, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float)`

Normalize all into a single `DrawEvent` record with nullable fields for overload-specific bits.

## Recording state machine

```
DISARMED → (cmd: arm) → ARMED → (draw occurs) → RECORDING → (cmd: snapshot) → DISARMED
```

While DISARMED, the prefix does an early return — zero cost beyond a volatile read. This is critical: normal gameplay with the harness installed but not recording must have <1% frame-time overhead.

## Ring buffer

Fixed size (default 10,000 events, configurable). Thread-local per draw thread (singular in SDV's case, but defensive). Overflow drops oldest with a counter that surfaces in snapshot metadata.

Structure:
```csharp
public record struct DrawEvent(
    int Tick,
    int CallIndex,
    Texture2D Texture,       // held for later path resolution
    Rectangle? SourceRect,
    Rectangle DestRect,       // computed if overload gave Vector2
    Color Color,
    float Rotation,
    Vector2 Origin,
    SpriteEffects Effects,
    float LayerDepth
);
```

Note: hold the `Texture2D` reference, not a pre-resolved path. Path resolution happens at snapshot time so the recorder stays cheap.

## Texture → asset path resolution

Two-tier strategy per spec:

### Tier 1: content pipeline hook

Wrap `IGameContentHelper.Load<Texture2D>` (via SMAPI event) and SMAPI's `AssetRequested` event. Every load event enters a weak-reference map:

```csharp
ConditionalWeakTable<Texture2D, string> _textureToPath;
```

ConditionalWeakTable means we don't leak — if the texture is GC'd, the entry goes away.

### Tier 2: hash fallback

For textures that appear in draw events but aren't in the map (engine-loaded vanilla XNB, dynamic textures), hash the texture bytes:

```csharp
var data = new Color[texture.Width * texture.Height];
texture.GetData(data);
var hash = Sha256(MemoryMarshal.AsBytes(data));
```

Compare against a pre-built `hash → asset_path` manifest shipped with the framework (generated from a vanilla SDV install). Cache hits update the weak-ref map.

**Perf note:** hashing a large texture is expensive. Only hash on cache miss, and only when someone actually queries for the path. Raw draw events store the `Texture2D` reference; resolution is on-demand.

### Tier 3: anonymous

If neither tier resolves, emit the event with `texture_asset = null` and `texture_size = (W, H)` plus `content_hash`. Assertions can still match on these.

## Snapshot format

On `draw.snapshot` RPC:

1. Stop the world via FREEZE if not already frozen
2. Copy ring buffer to a local list
3. Resolve paths for every event (tier 1 → 2 → 3)
4. Serialize to JSON
5. Return

## Filter API

The query DSL (spec §4.2) maps to LINQ over the resolved list:

```csharp
public record DrawFilter(
    string? TextureAsset = null,
    Rectangle? InRect = null,              // dest_rect fully contained
    (float Min, float Max)? LayerDepth = null,
    Color? ColorExact = null,
    (int X, int Y, int W, int H)? SourceRect = null
);
```

All fields AND together. No OR at this level — compose via multiple calls and union client-side.

## What not to do

- Don't allocate per-draw-event. The ring buffer is pre-allocated. Use structs, not records-as-classes.
- Don't resolve paths in the prefix. Defer to snapshot time.
- Don't lock. The game thread is the only writer; the RPC thread reads only after FREEZE.
- Don't record when DISARMED. The whole point of the arm/disarm split is zero overhead when idle.
