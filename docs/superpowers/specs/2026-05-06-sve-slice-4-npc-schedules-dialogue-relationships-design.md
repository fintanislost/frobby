# SVE Slice 4 NPC Schedules Dialogue Relationships Design

## Purpose

Use Stardew Valley Expanded as a custom-NPC-heavy testbed to add neutral Frobby support for observing NPC placement and schedules, setting relationship state, and validating normal NPC dialogue interactions. Slice 4 answers the question "can a test author put the game into a relationship/schedule state, find the NPC the way Stardew does, talk to them, and assert the visible result?"

## Goals

- Add a `state.npcs` RPC that lists known/runtime NPCs with compact social and placement summaries.
- Expand `state.npc` with backward-compatible schedule and action metadata where Stardew exposes it safely.
- Add `player.set_friendship` so scenarios can deterministically test friendship-gated dialogue and content for vanilla or custom NPCs.
- Improve JSON scenario ergonomics enough to wait for a named NPC's location and assert normal dialogue menu state.
- Add one SVE scenario proving the tools against Sophia, a custom NPC with custom data, schedules, locations, sprites, portraits, and dialogue.
- Keep every Frobby addition mod-agnostic; SVE only supplies examples and pressure.

## Non-Goals

- No dialogue-choice selection in this first Slice 4 pass. That remains a Slice 2 follow-up.
- No custom Content Patcher parser for NPC JSON, schedules, or dialogue.
- No attempt to force SDV's schedule engine to recompute every possible branch from source data.
- No new fixture save file unless the live scenario proves the existing core fixture cannot reach Sophia deterministically.
- No friendship decay, gift simulation, dating, marriage, roommate, divorce, or bouquet/pendant flows in the first pass.
- No sprite animation or portrait pixel assertions; Slice 7 owns deeper visual-effect and animation coverage.

## Current State

Frobby already has:

- `state.npc(name)` with name, current location, tile, friendship points, hearts, gift-given-today, and portrait base name.
- `state.location` with the NPCs present in one location.
- `world.interact_npc(name)` for invoking `NPC.checkAction` when the NPC is in the current location.
- `state.menu` dialogue extras for visible dialogue/menu text when readable.
- State setup helpers such as `time.set`, `time.advance`, `time.next_day`, `player.add_mail`, `player.warp`, and `world.set_weather`.
- `content.asset` assertions for runtime `Data/Characters`, schedule dictionaries, dialogue dictionaries, and texture assets.

The gaps are discovery and deterministic setup. A scenario can ask about one named NPC, but it cannot list known NPCs, cannot inspect schedule/current-action details, cannot create or set friendship state for a custom NPC, and has no runner-level wait for "Sophia has arrived at this location" before interaction.

SVE is a strong testbed because Sophia and other custom NPCs are defined through runtime `Data/Characters`, custom home locations, rich schedule dictionaries, schedule-dialogue strings, many friendship/day/season dialogue keys, and Content Patcher conditions.

## Architecture

Slice 4 extends the existing state/mutator pattern:

- `StateNpcsHandler` returns a bounded list of NPC summaries from the active game's known NPCs.
- `NpcStateProjector` centralizes NPC projection for both `state.npc` and `state.npcs`, so new fields stay consistent.
- `StateNpcHandler` keeps its current response shape and adds optional schedule/action fields.
- `PlayerSetFriendshipHandler` mutates the master farmer's friendship entry for a named NPC.
- `ScenarioRunner` adds runner-side polling for `wait.npc_location` and uses existing `state.assert` / `state.menu` assertions for dialogue.
- Protocol DTOs and docs define the RPC contracts without depending on SVE types.

This follows the pattern used by `state.locations`, `state.location`, `state.map_tile`, and `wait.location`: the harness reports neutral state on the game thread, while runner-side waits compose those facts into test-friendly control flow.

## `state.npcs` Contract

Request:

```json
{
  "include_offscreen": true,
  "limit": 200
}
```

Fields:

- `include_offscreen`: optional boolean, default true. When true, include NPCs known to the game even if they are not in the current location. When false, only include NPCs in `Game1.currentLocation`.
- `limit`: optional integer, default 200, valid range 1-1000.

Response:

```json
{
  "npcs": [
    {
      "name": "Sophia",
      "display_name": "Sophia",
      "location": "Custom_BlueMoonVineyard",
      "tile": { "x": 20, "y": 32 },
      "friendship_points": 500,
      "hearts": 2,
      "gift_given_today": false,
      "talked_to_today": false,
      "portrait": "Sophia",
      "current_schedule_key": "Mon",
      "current_schedule_time": 900,
      "current_schedule_location": "Custom_BlueMoonVineyard",
      "current_schedule_animation": "Sophia_Farm2"
    }
  ]
}
```

Field rules:

- `name` is the runtime NPC name/key used by Stardew lookups.
- `display_name` is best-effort localized/display text. If unavailable, it falls back to `name`.
- `location` is the NPC's current location name or an empty string when absent.
- `tile` is the NPC's current tile. Missing/offscreen NPCs use `{ "x": 0, "y": 0 }`.
- Friendship fields come from the local/master farmer's `friendshipData`; missing entries report zero points and false flags.
- Schedule fields are best-effort. Missing schedule metadata is omitted or returned as default values rather than failing the whole response.

## Expanded `state.npc`

The existing `state.npc` response stays compatible. Existing fields keep their names and meanings:

```json
{
  "name": "Sophia",
  "location": "Custom_BlueMoonVineyard",
  "tile": { "x": 20, "y": 32 },
  "friendship_points": 500,
  "hearts": 2,
  "gift_given_today": false,
  "portrait": "Sophia"
}
```

Slice 4 adds optional fields:

- `display_name`
- `talked_to_today`
- `current_schedule_key`
- `current_schedule_time`
- `current_schedule_location`
- `current_schedule_tile`
- `current_schedule_direction`
- `current_schedule_animation`
- `is_villager`
- `can_socialize`

Projection must be defensive. Stardew 1.6 and mods may store schedule state in private fields, public properties, or not at all. If a field is not discoverable, Frobby should return the rest of the NPC state.

## `player.set_friendship` Contract

Request:

```json
{
  "npc": "Sophia",
  "points": 500,
  "talked_to_today": false,
  "gifts_today": 0,
  "gifts_this_week": 0
}
```

Fields:

- `npc`: required NPC name/key. It may refer to a vanilla or custom NPC.
- `points`: required integer, valid range 0-2500, matching Stardew's practical social range for normal villagers.
- `talked_to_today`: optional boolean. When omitted, preserve the existing value or false for a new entry.
- `gifts_today`: optional integer, valid range 0-2. When omitted, preserve existing value or zero.
- `gifts_this_week`: optional integer, valid range 0-2. When omitted, preserve existing value or zero.

Response:

```json
{
  "ok": true,
  "tick": 1234
}
```

Behavior:

- Requires a loaded save/world, like other mutators.
- Creates a `Friendship` entry if one does not exist.
- Preserves relationship status fields not mentioned by this first pass, such as dating/marriage status.
- Does not simulate gift history, dialogue memory, bouquet status, or mail/event side effects.

## Runner Waits And Assertions

Add `wait.npc_location` as a runner-side polling action:

```json
{
  "action": "wait.npc_location",
  "args": {
    "name": "Sophia",
    "location": "Custom_BlueMoonVineyard",
    "timeout_ms": 10000,
    "poll_ms": 100
  }
}
```

Optional args:

- `x` and `y`: if supplied, the NPC's tile must match.
- `timeout_ms`: default 10000.
- `poll_ms`: default 100.

The runner invokes `state.npc` until the location and optional tile match. This keeps the harness small and gives report failures useful state context.

Existing `state.assert` can validate new fields:

```json
{
  "action": "state.assert",
  "args": {
    "expr": "state.npc.hearts == 2",
    "message": "Sophia friendship should be set to two hearts"
  }
}
```

Existing `state.menu` can validate dialogue speaker after `world.interact_npc`:

```json
{
  "action": "state.assert",
  "args": {
    "expr": "state.menu.extra.character == 'Sophia'",
    "message": "Talking to Sophia should open Sophia dialogue"
  }
}
```

## SVE Scenario Shape

Add `05-sve-npc-schedules-dialogue-relationships.test.json`.

The scenario should:

1. Load the existing SVE fixture.
2. Set a deterministic date/time/weather where Sophia has a stable schedule target. A good initial target is spring/summer Monday around 09:00 when Sophia's schedule places her in `Custom_BlueMoonVineyard`.
3. Wait for Sophia to be present at the expected location.
4. Assert `state.npcs` includes Sophia.
5. Assert `state.npc` reports Sophia's location, portrait/name, and schedule metadata when available.
6. Set Sophia friendship to 500 points.
7. Assert `state.npc.hearts == 2` and `state.npc.friendship_points == 500`.
8. Warp the player near Sophia or use existing map/location helpers to reach her location.
9. Call `world.interact_npc` and assert `state.menu` reports a dialogue menu with Sophia as the speaker.
10. Capture a final screenshot for report context.

The exact time/location/tile should be validated during implementation against live SVE behavior. If Sophia's schedule is delayed by save-load timing, the scenario should use `wait.npc_location` rather than hard-coded sleeps.

## Error Handling

- `state.npcs` rejects invalid `limit` values with `InvalidParams`.
- `state.npc` keeps the existing `InvalidParams` error for missing/wrong-type names and `GameStateInvalid` for a named NPC that cannot be found.
- `player.set_friendship` rejects blank NPC names, missing points, out-of-range points, and out-of-range gift counts with `InvalidParams`.
- `player.set_friendship` returns `GameStateInvalid` if no save/world is loaded.
- Schedule projection failures omit schedule fields instead of failing the NPC query.
- `wait.npc_location` fails with a timeout that includes the last observed NPC location/tile when available.

## Compatibility

- Existing `state.npc` consumers keep working because fields are only added.
- Existing Starberg scenarios do not need changes.
- `world.interact_npc` retains its current rule that the NPC must be in the player's current location.
- `state.menu` remains best-effort for dialogue text. Slice 4 relies on speaker/type assertions first, with exact text assertions only where the runtime text is stable.
- The new friendship mutator is explicit and opt-in; it does not run during fixture loading or scenario begin.

## Open Follow-Ups

- Dialogue-choice selection and branching remains a Slice 2 follow-up.
- Event-seen/mail mutation helpers for relationship-gated cutscenes remain a Slice 2 follow-up unless the Sophia scenario proves they are required.
- Richer schedule-source reporting, such as "which schedule key won and why", can be added later if runtime reflection alone is not enough for debugging.
- Sprite/portrait visual validation stays in Slice 7.
