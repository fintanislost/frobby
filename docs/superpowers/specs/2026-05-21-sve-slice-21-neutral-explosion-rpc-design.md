# SVE Slice 21 Neutral Explosion RPC Design

## Goal

Add a generic Frobby explosion primitive so tests can trigger Stardew-native explosion behavior at a specific tile without relying on player inventory, bomb placement, fuse timing, or UI input. The first proof should use SVE's corrupt mummy flow: isolate the real mod-spawned mummy in `Frobby_CombatLab`, then remove it with an explosion.

## Context

Slice 20 let Frobby relocate a real mod-spawned monster into the Combat Lab without reconstructing SVE or Farm Type Manager data. That gave tests a clean arena for SVE's corrupt mummy, but it left one important combat gap: mummy-style monsters can require bomb or explosion semantics for true removal.

Frobby already has player-like combat and tool actions. Explosion support should be similarly neutral: it must not encode SVE monster names, SVE map rules, or SpaceCore/Farm Type Manager behavior. SVE is the pressure test because the corrupt mummy is a real mod-configured runtime monster with custom health, damage, dodge, and sprite settings.

## Recommended Approach

Add a new harness RPC and scenario action:

- `world.explode_tile`

The action runs on the game thread, resolves a loaded location, validates the requested tile/radius, and invokes Stardew's native explosion path at that tile. It should be a direct testing primitive, not a player simulation. Player-like bomb placement can be added later once the deterministic explosion path is stable.

This keeps the first slice focused on semantics Frobby needs for many mods: "make the game process an explosion here and let normal game systems react."

## Request Shape

Example JSON scenario step:

```json
{
  "action": "world.explode_tile",
  "args": {
    "location": "Frobby_CombatLab",
    "x": 9,
    "y": 8,
    "radius": 2,
    "damage_player": false
  }
}
```

Request fields:

- `location`: optional location name. If omitted, use the player's current location.
- `x` and `y`: required non-negative tile coordinates.
- `radius`: optional explosion radius, defaulting to a small bomb-like radius. The handler should enforce a practical upper bound so tests cannot accidentally blanket a large map.
- `damage_player`: optional boolean, default `false`. Most test setup explosions should not damage the farmer unless the test explicitly opts in.

Do not require a bomb item, inventory state, held object, or player proximity for this RPC.

## Response Shape

Example response:

```json
{
  "ok": true,
  "location": "Frobby_CombatLab",
  "tile": { "x": 9, "y": 8 },
  "radius": 2,
  "tick": 123456,
  "monsters_before": 1,
  "monsters_after": 0,
  "debris_before": 0,
  "debris_after": 1
}
```

The count fields are useful diagnostics, not the primary assertion API. Tests should continue to assert content through `wait.location_content`, because that keeps waits, screenshots, and reports consistent with the rest of Frobby.

If the exact native Stardew call cannot reliably produce all count information in one frame, the response may report only the validated location/tile/radius/tick and leave world-state assertions to runner waits.

## Explosion Semantics

The handler should invoke Stardew's normal explosion handling instead of adding visual-only sprites or directly deleting monsters. The expected behavior is:

- monsters inside the blast radius receive explosion/bomb-style effects when the game exposes them through the native path;
- terrain, debris, objects, temporary sprites, and sounds follow the same game systems available in the loaded mod stack;
- mods observing native explosion events have the best chance to see the action as a real explosion;
- Frobby does not special-case mummy removal or any SVE monster type.

The action should be deterministic enough for tests, but it does not need to simulate a placed bomb's fuse, placement rules, animation timing, or inventory consumption.

## Error Handling

The handler should validate before invoking the explosion:

- reject calls before the world is loaded;
- reject blank or unknown `location` values;
- reject negative `x` or `y`;
- reject radius values below one;
- reject radius values above the configured safety bound;
- reject tiles outside the resolved map bounds when bounds are available.

Validation errors should return the same structured harness error style used by nearby world/combat handlers. Diagnostics should include the requested location, tile, and radius.

## SVE Proof Scenario

Add a new SVE scenario after the current Combat Lab relocation proof:

- `tests/sdv/29-sve-combat-lab-explode-mummy.test.json`

Preferred flow:

1. Load the SVE core profile and standard fixture.
2. Set time/weather and cross a real day boundary so Farm Type Manager spawns the Crimson Badlands corrupt mummy.
3. Warp to `Custom_CrimsonBadlands` near the known guard spawn.
4. Wait for the corrupt mummy at tile `(20,144)` with sprite texture `Characters/Monsters/CorruptMummy`.
5. Give the player a reliable weapon if the fixture does not already have one.
6. Reset `Frobby_CombatLab` and warp the player there.
7. Relocate the matching corrupt mummy into the lab with label `corrupt-mummy`.
8. Use existing player-like combat to bring the mummy into the state that needs explosion cleanup.
9. Call `world.explode_tile` on or near the labelled mummy's current lab tile.
10. Wait for zero lab monsters with label `corrupt-mummy`.
11. Capture a final frozen screenshot.

The scenario proves the RPC can finish a real mod-configured mummy through native explosion semantics while keeping SVE-specific coordinates and sprite paths inside the SVE test suite.

## Alternatives Considered

### Player-Like Bomb Placement First

Placing an actual bomb is the most player-realistic path, but it adds inventory setup, placement validity, fuse delay, animation waits, and player-positioning concerns. Those are valuable later. They are not needed to prove the core explosion behavior.

### Visual-Only Explosion Effects

Visual effects would be easy to implement, but they would not validate monster removal, object damage, mod event hooks, or mummy-specific bomb semantics. This slice requires game-state impact, not just screenshots.

### Direct Monster Kill Command

A kill command would make the SVE scenario pass, but it would not test the behavior a mod developer cares about: whether the loaded game stack processes an explosion correctly. Direct deletion also risks becoming a mod-specific shortcut.

## Testing Strategy

Use TDD.

Protocol tests:

- serialize and deserialize `WorldExplodeTileRequest` and `WorldExplodeTileResult` with snake-case JSON;
- preserve existing protocol compatibility for world, combat, and lab actions.

Harness tests:

- reject calls before the world is ready;
- reject unknown locations;
- reject negative coordinates;
- reject invalid radius values;
- reject out-of-bounds tiles when a map is available;
- invoke the explosion service for one valid request and report the resolved location/tile/radius.

Runner tests:

- parse and dispatch `world.explode_tile`;
- include meaningful report labels for explosion steps;
- keep screenshots and assertions using existing report conventions.

Live verification:

- run the new SVE scenario headlessly;
- rerun the Slice 20 relocation scenario to ensure relocation still works;
- rerun a vanilla Combat Lab removal scenario to ensure existing combat lab behavior still works;
- run the focused Frobby protocol, harness, runner, and DSL tests touched by the slice.

## Documentation

Update Frobby docs and wiki pages with:

- the `world.explode_tile` RPC schema;
- a DSL quickstart example;
- guidance that the action is a direct deterministic test primitive, not a player-like bomb placement flow;
- an SVE example linking the corrupt mummy scenario;
- the follow-up note that actual bomb placement is still a separate capability.

Update SVE docs with the new scenario summary and the reason the scenario exists: validating explosion-based cleanup for a real mod-spawned mummy.

## Follow-Up Work

Track player-like bomb placement as a later slice once direct explosion support is stable. That follow-up can cover inventory setup, held item selection, placement validation, fuse waits, and blast-result observation.
