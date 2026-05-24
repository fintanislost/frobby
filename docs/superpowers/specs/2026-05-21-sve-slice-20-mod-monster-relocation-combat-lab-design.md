# SVE Slice 20 Mod Monster Relocation Combat Lab Design

## Goal

Extend the Combat Lab so tests can isolate and target a mod-spawned monster without Frobby constructing mod-specific monsters. The first proof should use an SVE/Farm Type Manager monster that spawned through the normal mod runtime, move that exact monster into `Frobby_CombatLab`, assign a run-local identity/label, and prove player-like attack/removal there.

## Context

Slice 19 added a neutral Combat Lab that can reset a clean arena, spawn a small vanilla monster set, project run-local `monster_id` / `label`, and let runner waits and `combat.attack` target by identity. That solved exact-instance assertions for monsters Frobby creates.

The next gap is mod monster isolation. SVE’s existing combat proofs use Farm Type Manager runtime spawns such as the Crimson Badlands corrupt mummy. Those monsters carry modded runtime settings like sprite overrides, health, damage, dodge, and loot. Frobby should not parse or recreate those SVE/FTM rules in this slice.

The safer path is relocation: let the real mod stack create the monster, then move one matching runtime monster object into the lab.

## Recommended Approach

Add a new harness RPC and scenario action:

- `combat_lab.relocate_monster`

The action requires `combat_lab.reset` to have created the lab first. It reads a source location, finds exactly one monster matching neutral runtime filters, removes that monster object from the source location, places it at a requested Combat Lab tile, updates its `currentLocation`, assigns a Frobby run-local identity and optional label, and returns the identity and source metadata.

This keeps Frobby mod-agnostic. SVE-specific coordinates and sprite paths stay in the SVE scenario.

## Request Shape

Example JSON scenario step:

```json
{
  "action": "combat_lab.relocate_monster",
  "args": {
    "from_location": "Custom_CrimsonBadlands",
    "label": "corrupt-mummy",
    "target_x": 9,
    "target_y": 8,
    "match": {
      "x": 20,
      "y": 144,
      "sprite_texture": "Characters/Monsters/CorruptMummy",
      "health": 2000,
      "max_health": 2000
    }
  }
}
```

Request fields:

- `from_location`: required source location name.
- `label`: optional Frobby label to assign after relocation.
- `target_x` / `target_y`: required target tile inside `Frobby_CombatLab`.
- `match`: required object containing neutral monster filters.

Initial `match` filters should support these existing `state.location.monsters` summary fields:

- `x` and `y` for source tile.
- `monster_id` and `label` for already-bound monsters.
- `name`, `type`, and `sprite_texture`.
- exact numeric `health`, `max_health`, and `damage`.

Do not add SVE-specific selectors.

## Response Shape

Example response:

```json
{
  "ok": true,
  "monster_id": "frobby-monster-1",
  "label": "corrupt-mummy",
  "from_location": "Custom_CrimsonBadlands",
  "source_tile": { "x": 20, "y": 144 },
  "location": "Frobby_CombatLab",
  "tile": { "x": 9, "y": 8 },
  "name": "Mummy",
  "type": "Mummy",
  "sprite_texture": "Characters/Monsters/CorruptMummy",
  "health": 2000,
  "max_health": 2000
}
```

The result should report the monster as it exists after relocation, plus the original source location/tile for debugging.

## Identity Semantics

Relocated monsters are Frobby-bound, not Frobby-spawned.

Update the identity registry so assignment can distinguish:

- `spawned_by_frobby: true` for `combat_lab.spawn_monster`.
- `spawned_by_frobby: false` for `combat_lab.relocate_monster`.

The existing `monster_id` and `label` fields are still valid for both cases. A relocated monster gets a stable run-local identity for the current scenario, but that identity is not save-stable and should be cleared by lab reset and scenario end like vanilla lab-spawned identities.

No new `state.location` field is required for the first slice. If later reports need clearer diagnostics, a follow-up can add `relocated_by_frobby` or `source_location`.

## Matching And Error Handling

The handler should validate before mutating the world:

- reject missing or blank `from_location`;
- reject missing `match`;
- reject negative `target_x` / `target_y`;
- reject target tiles outside the lab map bounds;
- reject calls before the world is loaded;
- reject calls before `combat_lab.reset` created the lab.

Match behavior:

- No matching monsters: return `GameStateInvalid` with source location and filter details.
- More than one matching monster: return `GameStateInvalid` and require a tighter selector.
- Exactly one match: relocate that monster.

The handler should use projected monster summaries for matching where possible. That keeps match semantics aligned with what tests can observe through `state.location`.

## SVE Proof Scenario

Add this new SVE scenario after scenario 27:

- `tests/sdv/28-sve-combat-lab-relocate-mod-monster.test.json`

Preferred flow:

1. Load the SVE core profile and standard fixture.
2. Set time/weather and cross a real day boundary so FTM spawns the Crimson Badlands corrupt mummy.
3. Warp to `Custom_CrimsonBadlands` near the known guard.
4. Wait for the corrupt mummy at tile `(20,144)` with `sprite_texture: "Characters/Monsters/CorruptMummy"` and expected health/max health.
5. Give the player a reliable weapon.
6. Reset `Frobby_CombatLab` and warp the player there.
7. Relocate the matching corrupt mummy to a lab tile with label `corrupt-mummy`.
8. Wait for exactly one lab monster with that label and sprite texture.
9. Attack by lab label or `monster_id`.
10. Wait for zero lab monsters with that label.
11. Capture a final frozen screenshot.

This proves Frobby can isolate a mod-configured runtime monster without knowing SVE or FTM construction rules.

## Alternatives Considered

### Direct Mod Monster Construction

Constructing mod monsters directly would be more convenient, but it requires knowing how each mod or spawn framework maps content data to runtime monster instances. For SVE/FTM, that includes health, damage, dodge, sprite overrides, loot, and area rules. That is too much for this slice and risks baking SVE assumptions into Frobby.

### Keep Fighting In The Source Location

Existing SVE scenarios already do this. It validates live-map behavior, but it does not solve exact-instance isolation when multiple monsters, movement, debris, and map hazards are involved.

### Clone Instead Of Move

Cloning sounds safer for the source location, but monster objects are not guaranteed to have stable clone semantics across vanilla and modded types. Moving the real object preserves all runtime mod state.

## Testing Strategy

Use TDD.

Protocol tests:

- serialize/deserialize `CombatLabRelocateMonsterRequest`, match filters, and result fields with snake-case JSON;
- preserve existing Combat Lab request/result compatibility.

Harness tests:

- reject relocation before world ready;
- reject relocation before lab reset;
- reject out-of-bounds target tiles;
- reject zero matches and multiple matches;
- relocate exactly one fake monster from source to lab;
- assign `monster_id` / `label` with `spawned_by_frobby: false`;
- preserve projected name/type/sprite/health fields after relocation.

Runner tests:

- ensure scenario action `combat_lab.relocate_monster` passes through with useful report labels;
- reuse existing `wait.location_content` and `combat.attack` identity filters for the relocated monster.

Live verification:

- run SVE scenario 28 headlessly;
- rerun SVE scenario 27 to ensure vanilla lab spawning still works;
- rerun SVE scenario 12 or 13 to ensure existing live-map combat still works.

## Documentation

Update:

- `docs/rpc-schema.md` with `combat_lab.relocate_monster`;
- `docs/dsl-quickstart.md` with the relocation flow;
- `docs/wiki/examples.md` with the SVE scenario 28 path;
- SVE `docs/FROBBY.md` with the new scenario summary;
- `SVE_FROBBY_CAPABILITY_TODO.md` with Slice 20 status and verification notes.

The docs should emphasize that relocation is for runtime isolation, not direct monster construction.
