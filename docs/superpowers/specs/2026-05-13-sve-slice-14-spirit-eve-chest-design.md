# SVE Slice 14: Spirit's Eve Chest Coverage Design

## Context

Stardew Valley Expanded customizes some festival and special-map behavior. A useful next Frobby hardening slice is Spirit's Eve because it exercises a path that normal object and event tests do not cover:

- the test must enter a festival context, not just start an ordinary event;
- SVE reacts to the player's warp into the festival location;
- the mod mutates a chest's tile and contained items after the festival starts;
- the useful assertion is about the contents of a container object in the active map.

This makes the slice a good neutral capability check for Frobby. The implementation should not special-case SVE, Spirit's Eve, Golden Pumpkins, or any SVE map. It should add general festival setup and container inspection tools that any Stardew mod can use.

## Goals

- Add neutral Frobby support for inspecting chest/container contents through `state.location.objects`.
- Add neutral wait support for location objects that contain matching items.
- Add a neutral festival entry action that starts the current date's festival through the game's normal festival path.
- Extend event waits so scenarios can wait for an active festival state.
- Prove the flow with one SVE scenario that validates the year-one Spirit's Eve chest customization.

## Non-Goals

- Do not implement movie theater NPC coverage in this slice.
- Do not implement grange judging coverage in this slice.
- Do not add SVE-specific handler logic, tile names, item ids, or hard-coded festival ids to Frobby production code.
- Do not try to solve every container type in Stardew if it requires risky reflection. Chests and chest-like objects are sufficient for this slice as long as the projection remains neutral and fails softly for non-container objects.

## Approaches Considered

### Use Existing `event.start`

`event.start` can start ordinary events by id and location, but SVE's Spirit's Eve chest editor is triggered from `Player.Warped` when the new location already has a festival event. Starting a festival directly may skip the same lifecycle that a player uses to enter a festival. That makes this approach too brittle for the proof scenario.

### Script the Player Through the World Entrance

Driving the player to the festival entrance would be realistic, but it would make the scenario slow and dependent on map pathing, exact time, and current location. This is not the right foundation for reusable mod test setup.

### Add a Neutral `festival.start` Action

Add a setup action that enters the current date's festival through the game's native festival entry path. This gives tests a concise setup primitive while preserving the important game lifecycle. This is the preferred approach.

## Recommended Design

### Container Projection

Extend `ObjectSummary` in `state.location.objects` with container fields:

- `is_chest`: true when the object is a chest or chest-like Stardew container;
- `item_count`: number of projected contained items when available;
- `items_truncated`: true when Frobby intentionally limits projected contents;
- `items`: projected contained item summaries.

Contained item summaries should use the same wire shape as player inventory items:

- `slot`
- `id`
- `item_id`
- `qualified_id`
- `name`
- `stack`
- `quality`
- `category`
- `runtime_type`

The projection should be conservative:

- non-container objects keep the existing object summary behavior;
- empty containers report an empty `items` array and `item_count` of zero;
- inaccessible or unsupported container internals should not fail the whole state request;
- large containers can be capped with `items_truncated` so state payloads stay bounded.

### `wait.location_content` Contained-Item Filters

Extend location content object filters with optional contained-item predicates. The first proof scenario only needs a chest at a specific tile containing one Golden Pumpkin, but the filters should be generic:

- `contains_item_id`
- `contains_item_qualified_id`
- `contains_item_name`
- `contains_item_stack`
- `contains_item_stack_gte`
- `contains_item_quality`
- `contains_item_category`

The wait should pass when at least one location object matches the normal object filters and at least one contained item on that object matches all supplied contained-item filters.

Example intended shape:

```json
{
  "action": "wait.location_content",
  "args": {
    "objects": [
      {
        "tile": { "x": 63, "y": 16 },
        "runtime_type": "Chest",
        "contains_item_qualified_id": "(O)373",
        "contains_item_stack": 1
      }
    ]
  }
}
```

### `festival.start`

Add a neutral setup action:

```json
{
  "action": "festival.start",
  "args": {
    "location": "Town"
  }
}
```

The handler should:

- use the current in-game date and time to enter the available festival through Stardew's native festival path;
- fire the same player warp lifecycle that mods observe when a player enters the festival;
- optionally validate the resulting location when `location` is supplied;
- return the current tick, location, event id when available, and whether the current event is a festival;
- fail with a clear error when no festival is available for the current date.

The action should not know about SVE. It should work for vanilla festivals and custom festivals when the loaded mods register them through Stardew's normal event/festival data.

### `wait.event_active` Festival Filter

Extend `wait.event_active` args with:

- `is_festival`

The wait should continue to support existing `id` and `location` filters. When `is_festival` is present, the wait should compare against `state.event.is_festival`.

Example:

```json
{
  "action": "wait.event_active",
  "args": {
    "is_festival": true,
    "location": "Town"
  }
}
```

### SVE Proof Scenario

Add a new SVE scenario after the current Slice 13 scenario:

- set date to fall 27, year 1;
- set time to a valid Spirit's Eve entry time;
- start the festival through `festival.start`;
- wait for an active festival event;
- wait for a chest at tile `(63,16)` containing one Golden Pumpkin `(O)373`;
- capture a screenshot using `screenshot.capture_next_frame`;
- assert the container state through the new object projection.

The proof target comes from SVE's Spirit's Eve chest behavior:

- odd year triggers the SVE chest editor;
- year `% 4 == 1` moves the chest to `(63,16)`;
- year `% 4 == 1` gives the chest one Golden Pumpkin.

## Test Strategy

Use TDD before implementation:

- protocol/model tests for serializing container fields without breaking old object summaries;
- harness projector tests for chest item projection, empty chests, non-chest objects, and capped contents;
- runner tests for `wait.location_content` contained-item predicates;
- runner tests for `wait.event_active` with `is_festival`;
- handler tests for `festival.start` validation and failure response when no festival is active for the date;
- live headless SVE scenario for the Spirit's Eve chest proof;
- a Starberg smoke test after the Frobby changes to catch compatibility regressions.

## Risks

- Stardew's festival entry path may not be exposed behind a single clean public method. If so, implementation should still preserve the player warp lifecycle and remain generic.
- Some chest-like modded objects may not expose items through the same API as vanilla `Chest`. The first implementation should support the Stardew container path needed by vanilla and SVE while failing softly for unknown types.
- Festival timing can be fragile if the test starts from an invalid time. The SVE scenario should set both date and time explicitly before `festival.start`.

## Completion Criteria

- Frobby exposes container contents in location object state without breaking existing state consumers.
- `wait.location_content` can wait for an object containing a matching item.
- `festival.start` can enter the current date's festival through neutral game behavior.
- `wait.event_active` can filter on festival state.
- The new SVE Spirit's Eve scenario passes headless.
- Existing Frobby protocol, harness, runner, and a Starberg smoke test still pass.
- The SVE capability TODO marks this slice complete and leaves movie theater and grange coverage pending.
