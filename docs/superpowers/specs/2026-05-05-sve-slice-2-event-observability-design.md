# SVE Slice 2 Event Observability Design

## Purpose

Use Stardew Valley Expanded as a real cutscene-heavy testbed to add neutral Frobby support for observing active Stardew events. Slice 2 does not force, skip, or branch events yet. It gives test authors enough runtime state to prove that an event is active, inspect what the player can see, and wait until control returns to normal.

## Goals

- Add a `state.event` RPC that describes the currently active event or returns an inactive state.
- Expose active event context without hardcoding SVE names, ids, maps, actors, or dialogue.
- Add runner waits for event start and event completion.
- Improve dialogue/menu observation enough for cutscene assertions.
- Add one SVE scenario that proves the new tools against an actual SVE event or event-like flow.
- Keep deterministic freeze semantics unchanged: active events remain incompatible with `freeze.begin`.

## Non-Goals

- No `event.trigger` RPC in this slice.
- No forced event skipping, event command stepping, or fork/choice selection in this slice.
- No SVE-specific harness branches.
- No attempt to parse Content Patcher JSON directly inside Frobby.
- No freeze support during active events. Screenshots during events use existing live or next-frame capture paths.

## Current State

Frobby can already manipulate setup state with tools such as `time.set`, `time.advance`, `player.add_mail`, `player.warp`, `world.interact_npc`, and fixture loading. It can observe menus through `state.menu`, including the active `DialogueBox` speaker, but it cannot describe `Game1.CurrentEvent`, the event actor list, camera state, event id, or completion state.

SVE uses many Stardew event scripts with `speak`, `message`, `question`, `fork`, `viewport`, `changeLocation`, `warp`, `addTemporaryActor`, mail/event-seen gates, and event-completion side effects. Tests need a generic event state surface before they can safely drive or validate those flows.

## Architecture

Slice 2 adds event observation as a harness-side state projection and keeps waiting logic in the runner.

- `StateEventHandler` reads current game state on the game thread and returns an `EventState` DTO.
- `EventStateProjector` isolates reflection and Stardew object traversal from the handler.
- `ScenarioRunner` adds `wait.event_active` and `wait.event_complete` client-side polling actions.
- `StateMenuHandler` adds conservative dialogue/message text extras for visible menu text that can be read safely.
- Documentation and schema files describe the new RPC and scenario actions.

This matches the existing pattern used by `state.location`, `state.locations`, `state.map_tile`, and `wait.location`: the harness reports neutral state, and the runner turns that state into test-friendly waits.

## `state.event` Contract

When no event is active:

```json
{
  "active": false,
  "event_up": false,
  "location": "",
  "id": "",
  "actors": [],
  "dialogue": null,
  "viewport": null
}
```

When an event is active:

```json
{
  "active": true,
  "event_up": true,
  "location": "Town",
  "id": "60367",
  "is_festival": false,
  "is_skippable": true,
  "player_control_locked": true,
  "actors": [
    {
      "name": "Robin",
      "tile": { "x": 22, "y": 13 },
      "pixel": { "x": 1408, "y": 832 },
      "facing_direction": 0,
      "current_frame": 0
    }
  ],
  "dialogue": {
    "menu_type": "DialogueBox",
    "speaker": "Robin",
    "text": "..."
  },
  "viewport": {
    "x": 1472,
    "y": 896,
    "width": 1280,
    "height": 720
  }
}
```

Field rules:

- `active` is true when `Game1.CurrentEvent` or `Game1.currentLocation.currentEvent` is non-null, or when `Game1.eventUp` indicates a cutscene/event state.
- `event_up` mirrors `Game1.eventUp`.
- `location` is the current location name when available.
- `id` is best-effort. The projector reads known public or non-public event id fields/properties by reflection. If Stardew exposes no id for a specific event object, this is an empty string.
- `is_festival`, `is_skippable`, and `player_control_locked` are best-effort booleans. Missing fields become false rather than throwing.
- `actors` includes event actors that can be read from the active event plus the farmer when visible. Missing actor collections return an empty list.
- `dialogue` summarizes the visible active menu when it is a dialogue or message menu. It does not scrape arbitrary rendered text.
- `viewport` reports `Game1.viewport`.

## Dialogue Observation

`state.menu` keeps its current response shape and adds extra fields only when available:

- `dialogue_text`: visible dialogue text for `DialogueBox` when readable.
- `message_text`: visible message text for message-style menus when readable.
- `question_text`: visible question prompt when readable.

These are best-effort extras. If Stardew stores text in a field that is unavailable or still being animated, the key is omitted or empty. Tests should prefer `state.event.dialogue` for event flow checks, and can still use `draw.text_snapshot` when exact rendered text is required.

## Runner Waits

`wait.event_active`:

- Args: `timeout_ms` default 10000, `poll_ms` default 100, optional `id`, optional `location`.
- Polls `state.event` until `active == true`.
- If `id` is supplied, it must match `state.event.id`.
- If `location` is supplied, it must match `state.event.location`.
- On timeout, reports the last observed event state.

`wait.event_complete`:

- Args: `timeout_ms` default 30000, `poll_ms` default 100, optional `id`.
- Polls `state.event` until `active == false` and `event_up == false`.
- If `id` is supplied, the wait first observes that active id before accepting completion. This prevents a test from passing immediately before the target event starts.
- On timeout, reports the last observed event state.

## SVE Scenario Shape

Add a new SVE scenario after the current location-registration scenario. The scenario should:

1. Load the SVE fixture.
2. Set neutral preconditions with existing Frobby tools.
3. Enter a location or interaction path that starts a real SVE event or event-like scripted scene.
4. Use `wait.event_active`.
5. Assert `state.event.active == true`.
6. Assert at least one useful observable such as location, actor presence, dialogue speaker/text, or viewport.
7. Capture a live or next-frame screenshot while the event is active.
8. Use existing input/click/key tools only if the event needs normal player acknowledgement.
9. Use `wait.event_complete`.
10. Freeze and assert normal post-event state.

If a reliable SVE event cannot be started through existing setup tools during implementation, the fallback scenario uses a vanilla event first to verify the neutral Frobby capabilities, while the SVE capability list remains open for event triggering/control in a later slice. The Frobby implementation must not add an SVE-only shortcut.

## Error Handling

- `state.event` never fails because no event is active.
- Reflection failures are contained to the missing field and recorded by omission or default values.
- If world state is not ready, `state.event` returns inactive state with `location == ""` instead of requiring a loaded save.
- Wait actions validate `timeout_ms >= 1` and `poll_ms >= 1`.
- Wait timeout messages include the last observed event state so scenario authors can debug whether the event never started or never completed.

## Compatibility

- Existing RPCs keep their current fields and behavior.
- `state.menu` only adds optional `extra` keys.
- `freeze.begin` continues to reject `Game1.eventUp` so deterministic screenshot behavior is not weakened.
- Starberg scenarios should not need changes unless they choose to use the new event waits.
- The capability is mod-agnostic and should apply to vanilla Stardew, SVE, Starberg, and other SMAPI/Content Patcher mods.

## Testing

Frobby tests:

- Unit test inactive `state.event` projection.
- Unit test active event projection with fake/reflection-friendly event objects where possible.
- Unit test actor projection from fake NPC/farmer-like objects where possible.
- Unit test `state.menu` dialogue extras without changing existing `DialogueBox` behavior.
- Runner tests for `wait.event_active`, including success and timeout.
- Runner tests for `wait.event_complete`, including the supplied-id guard.
- Schema/docs checks for the new scenario actions.

Live verification:

- Run the Frobby unit suite.
- Run a small Starberg smoke after the RPC registration changes.
- Run SVE scenario 01 and 02 to prove existing SVE scaffold still works.
- Run the new SVE event-observability scenario headless.

## Future Work

After Slice 2 proves observation, later slices can add:

- `event.trigger` that respects normal location event data and conditions.
- Event skip/advance commands.
- Question/fork selection helpers.
- Event command trace snapshots for debugging long cutscenes.
- Event-seen flag setters/removers if direct setup proves necessary.
