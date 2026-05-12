# SVE Slice 12: Player Effects, Swimming, And Timed State Design

## Context

Slice 11 added neutral fishing context, table, and deterministic sampling tools.
The next Stardew Valley Expanded pressure point is transient player state. SVE's
`CustomBuffs` feature applies one of several long-duration buffs after the local
player swims for three seconds in specific spring or hot-spring locations:
`Custom_GrandpasGrove`, `Custom_SpriteSpring2`, or
`Custom_FerngillRepublicFrontier_HotSpring`.

Frobby can already read basic `state.player` fields and poll `wait.player` for
health, location, and tile changes. It cannot yet explain whether the player is
swimming or wearing bathing clothes, inspect active buff metadata, or wait for a
specific timed player effect to appear. That makes SVE's swim-buff behavior hard
to validate without brittle screenshot or manual inspection.

## Goals

- Add neutral player transient-state fields to `state.player`.
- Add active buff summaries to `state.player`.
- Add a neutral setup action that can set player transient state for tests.
- Extend runner-side `wait.player` so scenarios can wait for transient state and
  active buffs without sleeps.
- Add one SVE proof scenario that validates SVE's swimming-location buff behavior.
- Keep all Frobby production code mod-agnostic.

## Non-Goals

- Do not implement full movement/pathing into water.
- Do not hardcode SVE location names, buff IDs, or effect choices in Frobby
  production code.
- Do not try to model every Stardew buff mechanic. Frobby should project the
  live game state rather than reimplementing buff rules.
- Do not require Frontier Farm for core SVE Slice 12 coverage. Frontier Farm's
  hot spring remains a future proof once alternate pack runs are stronger.
- Do not add a separate `state.buffs` RPC unless `state.player` becomes too large
  during implementation.

## Approaches Considered

### Recommended: Enrich `state.player`

Add transient-state booleans and active buff summaries directly to
`state.player`, then teach `wait.player` to filter those fields. This matches the
existing runner model: player-centric waits already poll `state.player`, and the
scenario DSL can keep assertions simple.

This is the smallest useful surface because the behavior is player-owned: swimming,
bathing clothes, stamina, health, and buffs all belong to the local farmer.

### Separate `state.player_effects`

Create a new RPC for transient state and buffs. This would keep `state.player`
small, but it would force scenario authors to compose two player snapshots for one
conceptual check. It is a reasonable future split if buff projection grows beyond
simple summaries.

### Movement-Only Test

Drive the player into spring water using click or movement commands and assert the
buff appears. This tests more of the real flow, but it couples Slice 12 to pathing,
collision, and map coordinates. It is better as later end-to-end coverage after
the state surface is reliable.

## Recommended Design

### Player Transient State

Extend `state.player` with additive fields:

- `swimming`: true when the local farmer is currently swimming.
- `bathing_clothes`: true when the local farmer is wearing bathing clothes.
- `is_busy`: best-effort true when the player is not in normal free control.
- `can_move`: best-effort true when the player can currently move.

Only `swimming` and `bathing_clothes` are required for Slice 12. `is_busy` and
`can_move` should be included if they are stable to read from the live player or
Stardew globals without adding fragile reflection.

These fields are additive, so existing scenarios and consumers remain compatible.

### Active Buff Summaries

Extend `state.player` with `buffs`, a list of active buff summaries. Each summary
should use stable, snake-case fields:

- `id`: buff id if available.
- `display_name`: player-facing name if available.
- `source`: source/id field if Stardew exposes one.
- `milliseconds_duration`: current remaining duration when available.
- `total_milliseconds_duration`: total duration when available.
- `effects`: a compact object or dictionary of nonzero numeric effects.
- `runtime_type`: CLR type name for diagnostics.

The effect projection should include common Stardew effect names such as:

- `farming_level`
- `fishing_level`
- `mining_level`
- `foraging_level`
- `luck_level`
- `attack`
- `defense`
- `speed`
- `magnet_radius`

Unknown or unavailable effect fields should be omitted, not treated as failure.
The response should preserve active buffs even when names or durations are empty.

### Transient State Setup Action

Add `player.set_transient_state`.

Request fields:

- `swimming`: optional boolean.
- `bathing_clothes`: optional boolean.

Response fields:

- `tick`
- `previous_swimming`
- `previous_bathing_clothes`
- `swimming`
- `bathing_clothes`

The handler should require a loaded world. It should only change fields supplied
in the request. It should not set location, stamina, buffs, or other player state.

This action is neutral: it lets tests put the local farmer into a state that the
game or mod already reacts to. SVE-specific behavior still comes from SVE's own
event handler.

### Runner Wait Support

Extend `wait.player` filters with:

- `swimming`: optional boolean.
- `bathing_clothes`: optional boolean.
- `buff_id`: optional exact id match across active buffs.
- `buff_source`: optional exact source match across active buffs.
- `buff_effect`: optional effect field name that must exist on any active buff.
- `buff_effect_gte`: optional numeric lower bound paired with `buff_effect`.
- `buff_count_gte`: optional minimum number of active buffs.

The common SVE proof should use broad effect matching, not a single exact buff id.
SVE chooses the buff type from save/day state, so a durable assertion is:

- `buff_count_gte: 1`
- `buff_effect` equal to one of the effect fields if a deterministic seed is known,
  or a scenario assertion that checks `state.player.buffs` contains any recognized
  positive effect.

If the current expression evaluator cannot express "any positive effect among this
set", add a small purpose-built `wait.player` predicate such as
`buff_any_effect_gte` with a list of effect names and a numeric threshold. Prefer
that over widening the general expression language.

Timeout details should include last observed location, tile, swimming state, and
buff count, with the first few buff ids/effects when available.

### Documentation And Schema

Update:

- `docs/rpc-schema.md` for `state.player` additions and
  `player.set_transient_state`.
- `docs/dsl-quickstart.md` with a short player-effect wait example.
- `schemas/scenario.schema.json` so scenario validation accepts the new action and
  `wait.player` arguments.
- `SVE_FROBBY_CAPABILITY_TODO.md` to mark Slice 12 active and then done.

### SVE Proof Scenario

Add a SVE repo scenario, likely `tests/sdv/17-sve-player-effects-swim-buff.test.json`.

Initial proof flow:

1. Load the standard SVE fixture with core SVE.
2. Set a stable time/date.
3. Warp to `Custom_SpriteSpring2` at a safe tile.
4. Wait for the location to settle.
5. Call `player.set_transient_state` with `swimming: true`.
6. Wait for SVE's one-second update loop to apply a buff.
7. Assert `state.player.swimming == true`.
8. Assert `state.player.buffs` contains a long-duration buff with a positive skill
   or attack effect.
9. Capture a frozen final screenshot for report context.

If `Custom_SpriteSpring2` is progression-gated in the existing fixture, use
`Custom_GrandpasGrove` if it loads under core SVE. The scenario may warp directly;
the goal is to verify the buff reaction, not the unlock path.

## Test Strategy

Use TDD for each behavior:

- Protocol serialization tests for the new `PlayerState` fields and buff summaries.
- Harness unit tests with a fake `IPlayerStateWorld` for projected transient state
  and buffs.
- Harness unit tests for `player.set_transient_state` using a small fake world
  wrapper so live `Farmer` mutation is not required for basic validation.
- Runner tests for `wait.player` matching and timeout diagnostics.
- Focused live SVE scenario run after the Frobby surfaces pass unit tests.

## Risks And Mitigations

- Stardew buff internals may vary between versions. Mitigation: use reflection
  helpers and best-effort projection, with empty fields instead of hard failures.
- SVE's buff applies on a one-second SMAPI update, not an in-game time tick.
  Mitigation: `wait.player` polls state instead of relying on `time.advance`.
- The exact SVE buff effect is save/day dependent. Mitigation: assert an allowed
  positive effect set instead of a single hardcoded buff unless the fixture makes
  the selection deterministic and documented.
- Directly setting `swimming` may not trigger all visual or clothing side effects.
  Mitigation: this slice validates mod reaction to transient state. Full movement
  into water can be a later end-to-end scenario.

## Completion Criteria

- `state.player` exposes transient-state fields and active buff summaries.
- `player.set_transient_state` can set swimming/bathing booleans and reports prior
  state.
- `wait.player` can wait for swimming/bathing and active buff conditions.
- Docs and schema describe the new surface.
- Frobby unit tests pass.
- A headless SVE scenario proves SVE's swim-buff behavior against a real mod run.
