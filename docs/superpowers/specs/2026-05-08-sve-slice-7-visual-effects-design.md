# SVE Slice 7 Visual Effects Design

## Purpose

Use Stardew Valley Expanded's temporary sprite effects, custom ambience, and
lighting changes as a pressure test for neutral Frobby support around visual
effect observability. Slice 7 answers the question "can a mod test prove a
runtime visual effect exists and is rendering without relying only on brittle
whole-screen screenshots?"

The first pass should be a state-first visual-effects foundation. Frobby already
captures draw calls and screenshots, but draw events alone do not tell a test
author whether an effect failed to spawn, failed to render, or rendered with the
wrong texture/color/depth. A compact runtime visual-effects snapshot gives tests
that missing diagnostic layer.

## Goals

- Add a neutral `state.visual_effects` snapshot for live runtime visual state.
- Expose temporary animated sprites, light sources, ambient light, and weather
  debris counts without adding mod-specific knowledge to Frobby.
- Add runner-side `wait.visual_effects` polling so scenarios can wait for
  effects that spawn on update ticks.
- Keep draw-call assertions as the rendering proof, using state snapshots to
  make failures diagnosable.
- Add an SVE scenario proving a real SVE visual effect exists and renders.
- Document the visual-effects testing flow for other mod repos.

## Non-Goals

- No SVE-specific effect IDs, location names, or texture paths in Frobby
  production code.
- No pixel-diff-first validation. Bitmap assertions remain a fallback, not the
  primary Slice 7 mechanism.
- No full animation timeline engine in the first pass.
- No assertion that one temporary sprite advances through multiple frames over a
  precise tick range. This can be added later if a real mod scenario needs it.
- No direct mutation of a mod's private effect state. Frobby should observe
  Stardew runtime state and use existing player/world setup helpers.
- No attempt to parse Content Patcher map files for visual effects. Frobby
  should inspect live game objects after the mod has loaded.

## Current State

Frobby already has:

- `draw.arm`, `draw.snapshot`, `draw.find`, and `draw.assert_contains` for
  captured `SpriteBatch.Draw` calls.
- Texture-asset resolution and hash/size fallback for draw events.
- `source_rect`, `color`, `layer_depth_range`, `texture_asset`,
  `content_hash`, `texture_size`, and `in_rect` draw filters.
- `screenshot.capture_next_frame` and bitmap fallback assertions.
- `world.set_weather`, `time.set`, `time.advance`, `player.warp`, and
  `wait.location` setup primitives.
- `state.location` for map/world content, but no visual-effects-specific state.

The gaps for visual-effect testing are:

- No state projection for `GameLocation.temporarySprites`.
- No state projection for `Game1.currentLightSources`.
- No state projection for ambient light or active weather debris counts.
- No scenario-level wait for effects that spawn asynchronously after warp or
  update ticks.
- Draw assertions cannot easily express "a runtime temporary sprite spawned with
  this texture/source/color/depth" before searching the full draw buffer.

SVE is a strong testbed because it includes:

- `CustomCauldronEffects`, which spawns `TemporaryAnimatedSprite` instances on
  locations such as `Custom_GrandpasGrove` and `Custom_CrimsonBadlands`.
- `LocationEffects`, which changes `Game1.ambientLight`, adjusts map lights,
  and draws weather debris in locations such as `Custom_JunimoWoods` and
  `Custom_SpriteSpring2`.
- `ConditionalLightSources`, which adds light sources based on live location
  tiles.

The first proof scenario should use SVE's cauldron-style temporary sprites,
because they exercise a missing Frobby capability directly and can still be
verified with existing draw assertions.

## Architecture

Slice 7 adds a new read-only harness state method:

```json
{ "jsonrpc": "2.0", "id": 21, "method": "state.visual_effects" }
```

The method projects runtime visual state from the current location by default,
or from `params.location` when supplied and loaded. The response stays additive
and mod-neutral. It should tolerate missing/empty Stardew collections by
returning empty arrays and zero counts.

The runner adds `wait.visual_effects`, implemented by polling
`state.visual_effects` until minimum count criteria are met. This keeps waits in
the runner, consistent with existing `wait.location`, `wait.npc_location`, and
`wait.location_content` helpers.

Rendering proof remains separate:

1. Use state/wait to prove the effect exists in runtime state.
2. Use `draw.arm` plus `draw.contains` to prove a matching draw call appears.
3. Capture a final screenshot for human inspection.

This structure makes failures actionable. If state wait fails, the effect did
not spawn. If state passes but draw fails, the effect exists but did not render
with the expected texture/source/color/depth.

## `state.visual_effects`

Request, current location:

```json
{ "jsonrpc": "2.0", "id": 21, "method": "state.visual_effects" }
```

Request, named loaded location:

```json
{
  "jsonrpc": "2.0",
  "id": 21,
  "method": "state.visual_effects",
  "params": { "location": "Custom_GrandpasGrove" }
}
```

Example response:

```json
{
  "location": "Custom_GrandpasGrove",
  "ambient_light": [150, 120, 50, 255],
  "temporary_sprites": [
    {
      "texture_asset": "LooseSprites/Cursors",
      "source_rect": [372, 1956, 10, 10],
      "position": [1536.0, 1472.0],
      "motion": [0.0, -0.35],
      "acceleration": [0.0, 0.0],
      "color": [240, 248, 255, 255],
      "alpha": 0.45,
      "alpha_fade": 0.0009,
      "scale": 4.0,
      "scale_change": 0.01,
      "rotation": 0.0,
      "rotation_change": 0.0,
      "layer_depth": 0.144,
      "draw_above_always_front": false,
      "runtime_type": "TemporaryAnimatedSprite"
    }
  ],
  "light_sources": [
    {
      "id": "SVE_FH_10_12_FrontTile3189",
      "position": [672.0, 800.0],
      "radius": 1.0,
      "color": [127, 127, 0, 191],
      "texture_index": 4,
      "context": "None"
    }
  ],
  "weather_debris_count": 25
}
```

Fields:

- `location`: projected location name, empty when no loaded location is found.
- `ambient_light`: current global ambient light as `[r, g, b, a]`.
- `temporary_sprites`: live temporary animated sprites for the location.
- `light_sources`: live current light sources from Stardew's light-source
  dictionary.
- `weather_debris_count`: best-effort count of live weather debris for the
  current visual environment.

Temporary sprite fields are best-effort. Stardew exposes many of these as public
fields on `TemporaryAnimatedSprite`; if a field is not available on the runtime
version, Frobby should omit it or return a neutral default rather than fail the
whole RPC. Texture asset resolution should use the same registry/fallback
approach as draw snapshots.

Light-source projection should not try to decide whether a light is visible on
screen. It should report the live source, position, radius, color, texture index,
and context when Stardew exposes them.

## `wait.visual_effects`

`wait.visual_effects` is a runner-only step.

Example:

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
    "timeout_ms": 12000,
    "poll_ms": 100
  }
}
```

Accepted criteria:

- `temporary_sprites.min_count`
- `temporary_sprites.texture_asset`
- `temporary_sprites.source_rect`
- `temporary_sprites.color`
- `light_sources.min_count`
- `light_sources.id_contains`
- `ambient_light`
- `weather_debris_min_count`

The first pass only needs AND semantics across supplied criteria. If no criteria
are supplied, the runner should reject the step as invalid. Timeout errors should
include the last observed counts so failed reports are useful.

## Draw Filter Enhancements

The first implementation can likely reuse existing draw filters:

- `texture_asset`
- `source_rect`
- `color`
- `layer_depth_range`
- `in_rect`

If implementation proves that SVE's visual-effect draws are hard to target
because they move partially offscreen or overlap viewport bounds, add
`intersects_rect` to `DrawFilter`. This field should match when the draw event's
destination rectangle intersects the provided rectangle. It is more appropriate
for particles and moving effects than `in_rect`, which requires full
containment.

`intersects_rect` is optional for Slice 7. It should be added only if the SVE
proof scenario needs it.

## SVE Scenario 09: Visual Effects

Add `tests/sdv/09-sve-visual-effects.test.json`.

Scenario shape:

1. Load the existing SVE fixture.
2. Warp to a selected SVE location with deterministic temporary sprite effects.
3. Use `wait.location` to avoid transition frames.
4. Use `wait.visual_effects` to wait for at least one matching temporary sprite.
5. Arm draw capture for a short window.
6. Wait briefly so the sprite can draw.
7. Assert `draw.contains` with the matching texture/source/color or depth.
8. Capture `screenshot.capture_next_frame` for the report.

Preferred candidate:

- `Custom_GrandpasGrove`, because `CustomCauldronEffects` defines many
  waterfall-style `LooseSprites/Cursors` temporary sprites there.

Fallback candidate:

- `Custom_CrimsonBadlands`, because it uses `Maps/SandstormEffect` and many
  temporary sprite emitters. This is visually distinctive, but it may require
  more game-state setup if access is progression-gated.

The final candidate should be selected during implementation by running a small
headless probe and choosing the location that yields stable temporary sprite
state and draw events with the fewest setup assumptions.

## Testing Strategy

Frobby unit tests:

- Protocol serialization for `VisualEffectsState`, `TemporarySpriteSummary`,
  and `LightSourceSummary`.
- `StateVisualEffectsHandler` returns empty state when no world/location is
  loaded.
- `VisualEffectsStateProjector` maps temporary sprite fields into stable DTOs.
- `VisualEffectsStateProjector` maps light-source fields into stable DTOs.
- `VisualEffectsStateProjector` exposes ambient light as `[r, g, b, a]`.
- Runner tests for `wait.visual_effects` success, timeout detail, and invalid
  no-criteria input.

SVE verification:

- Run scenario 09 headlessly by itself.
- Run SVE smoke subset including scenarios 01, 04, and 09.
- Inspect the generated report to confirm the final screenshot is useful for
  human review.

Docs:

- Update `docs/rpc-schema.md` with `state.visual_effects`.
- Update `docs/dsl-quickstart.md` and `README.md` with a compact visual-effects
  testing example.
- Update SVE `docs/FROBBY.md` to describe scenario 09.

## Risks And Mitigations

- Temporary sprites are short-lived and spawn on randomized intervals. Mitigate
  with `wait.visual_effects` polling and a generous timeout rather than fixed
  sleeps.
- Texture path resolution may return null for some generated or preloaded
  textures. Mitigate by also exposing source rect, texture size, color, and
  runtime type.
- Some SVE visual effects may be progression-gated. Start with a location/effect
  that can be entered from the existing fixture and direct warp.
- Draw assertions may fail if particles move outside a strict `in_rect`.
  Mitigate by using state checks first, broad draw filters second, and only
  adding `intersects_rect` if a real scenario needs it.
- Light-source state is global in Stardew. The first pass reports live current
  light sources and does not promise per-location isolation beyond what Stardew
  exposes.
