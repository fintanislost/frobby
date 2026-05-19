# SVE Slice 19 Combat Lab Design

## Goal

Add a neutral Frobby combat dev room, called the Combat Lab, so tests can isolate monster identity, movement, death/removal, and drops without depending on crowded live maps. Start with vanilla monsters first, then leave a clear follow-up path for modded monster support.

## Context

Slice 8 proved player-like melee attacks can damage a deterministic SVE monster. Slice 9 added monster lifecycle, debris, and player-health observation. Those tools work, but the remaining hardening gap is identity: in a real combat location, a JSON scenario can often prove that some matching monster changed or disappeared, but it cannot always prove that a specific moving monster instance was the one removed.

A dev room solves that by giving tests a clean arena:

- no pre-existing monsters or debris;
- deterministic player and monster start tiles;
- one or more deliberately spawned monsters;
- stable run-local identities for projected monsters;
- clean before/after assertions for death and drops.

The lab must be Frobby-owned and mod-agnostic. SVE is the pressure test, not the implementation target.

## Recommended Approach

Create a test-only runtime location named `Frobby_CombatLab`. The harness creates or resets it only during test runs. It should not be written into source fixtures and should be removed or harmless after a scenario ends.

The first implementation should support a small vanilla monster set, such as:

- `GreenSlime`
- `Bat`
- one additional simple vanilla monster only if construction is reliable during implementation

Vanilla-first keeps the slice focused on Frobby mechanics. Once the lab can spawn, identify, attack, remove, and observe drops for vanilla monsters, later slices can add mod monster support through either runtime type construction, mod-provided spawn actions, or moving an already-spawned mod monster into the lab.

## Combat Lab Actions

### `combat_lab.reset`

Reset or create the lab location, clear monsters and debris, place the farmer at a known tile, and optionally warp the player there.

Example:

```json
{
  "action": "combat_lab.reset",
  "args": {
    "player_x": 8,
    "player_y": 8,
    "width": 20,
    "height": 14,
    "warp_player": true
  }
}
```

The first version can use fixed dimensions if dynamic sizing adds risk. The important contract is a clean, passable arena with stable player positioning.

### `combat_lab.spawn_monster`

Spawn a supported vanilla monster at a tile and assign a stable lab label.

Example:

```json
{
  "action": "combat_lab.spawn_monster",
  "args": {
    "kind": "GreenSlime",
    "label": "target",
    "x": 12,
    "y": 8,
    "health": 24
  }
}
```

The response should include:

- `monster_id`: Frobby's stable run-local identity;
- `label`: optional caller label;
- `kind`;
- `location`;
- `tile`;
- `health` and `max_health` when available.

The handler should reject unsupported monster kinds, occupied invalid tiles, and calls before the world is ready.

## Monster Identity

Extend `state.location.monsters` with additive identity fields:

- `monster_id`: stable for the lifetime of the monster object during the current run;
- `label`: optional Frobby-assigned label, mainly for lab-spawned monsters;
- `spawned_by_frobby`: true for Combat Lab spawns;
- existing fields such as `name`, `type`, `health`, `max_health`, `damage`, `tile`, and `sprite_texture`.

For lab-spawned monsters, the identity can be stored in `modData` when available and also tracked in a harness-side registry keyed by object reference. For non-lab monsters, Frobby can assign a best-effort run-local identity through the same registry later, but the Slice 19 proof only needs lab spawns.

No identity value should be treated as save-stable. It is a test-run handle, not game data.

## Combat Targeting

Extend scenario-level `combat.attack` targeting so a caller can attack by `monster_id` or lab `label`.

Example:

```json
{
  "action": "combat.attack",
  "args": {
    "target": {
      "monster_id": "frobby-monster-1"
    },
    "repeat": 8,
    "delay_ticks": 8
  }
}
```

The runner should resolve the current monster tile from `state.location.monsters` before each repeated attack, then call the existing harness attack primitive against that tile. This keeps the harness action player-like and avoids direct damage or kill shortcuts.

`wait.location_content` should accept `monster_id` and `label` filters for `collection: "monsters"`, including zero-count waits to prove removal:

```json
{
  "action": "wait.location_content",
  "args": {
    "location": "Frobby_CombatLab",
    "collection": "monsters",
    "label": "target",
    "max_count": 0,
    "timeout_ms": 5000
  }
}
```

## Drops And Debris

The lab should clear debris on reset, so any later debris is attributable to the lab run without a complex baseline. Initial drop assertions should be cautious because vanilla monster drops can be random and loaded mods may change drop behavior.

Slice 19 should support:

- waiting for zero matching monsters after player-like attacks;
- projecting any new debris already available through `state.location.debris`;
- asserting deterministic drop output only when the chosen monster, seed, and loaded mod set make it stable.

The first live proof can focus on identity and removal. A follow-up can add stronger drop-table assertions once we confirm which vanilla monster/drop path is stable under the target mod set.

## SVE Proof Scenario

Add a new SVE scenario after scenario 26.

Preferred flow:

1. Load the SVE core profile and standard fixture.
2. Reset `Frobby_CombatLab` and warp the player into it.
3. Give the player a reliable weapon if the fixture does not already have one.
4. Spawn a vanilla `GreenSlime` with label `target`.
5. Assert `state.location.monsters` includes one monster with `label: "target"`.
6. Attack by label or returned `monster_id` until the monster is removed.
7. Wait for `collection: "monsters", label: "target", max_count: 0`.
8. Optionally snapshot or assert debris if stable in the loaded SVE mod set.
9. Capture the final screenshot under freeze conditions.

This scenario proves the lab works in a real complex mod stack while still using vanilla monsters for the first slice.

## Mod Monster Follow-Up

Do not require custom monster support in Slice 19. Track it as a follow-up with three possible approaches:

1. Spawn by runtime type or content id when a mod exposes a constructible monster type.
2. Move an existing mod-spawned monster into the lab and bind its identity.
3. Execute a mod-provided spawn action in the lab, then bind the resulting monster.

The follow-up should be driven by real SVE research so Frobby does not encode SVE monster assumptions.

## Alternatives Considered

### Keep Testing In Live Maps

This avoids a new lab concept, but it leaves the same hardening problem in place. Live maps have pathing, pre-existing debris, multiple monster instances, and mod spawn rules that make identity assertions brittle.

### Direct Monster Kill Or Loot Spawn Commands

Direct commands would be useful for some fixture setup tasks, but they would not prove player-facing combat behavior. Slice 19 should still use `combat.attack` for the proof path.

### Mod Monsters First

Starting with custom SVE monsters is tempting, but it mixes three unknowns at once: Frobby identity, monster construction, and mod-specific drop/combat behavior. Vanilla-first gives us a stable foundation before adding mod monster complexity.

## Testing Strategy

Use TDD.

Protocol and schema tests:

- `combat_lab.reset` and `combat_lab.spawn_monster` request/response serialization;
- additive monster identity fields do not break existing `state.location` consumers.

Harness tests:

- lab reset creates a clean location and clears monsters/debris;
- vanilla monster spawn validates supported kinds and assigns a stable identity;
- `state.location.monsters` projects `monster_id`, `label`, and `spawned_by_frobby`;
- reset removes prior lab monsters and debris.

Runner tests:

- `combat.attack` target resolution supports `monster_id` and `label`;
- repeated attacks re-resolve the monster's current tile before each swing;
- `wait.location_content` supports monster `monster_id` and `label` filters;
- timeout diagnostics include the requested identity/label and observed monster summaries.

Live verification:

- run the new SVE scenario headlessly;
- rerun the closest existing SVE combat scenario to confirm no combat regression;
- run focused Frobby Protocol, Harness, Runner, and DSL tests touched by the slice.

## Documentation

Update Frobby docs and wiki pages with:

- Combat Lab purpose and non-production nature;
- `combat_lab.reset` and `combat_lab.spawn_monster` examples;
- monster identity fields in `state.location.monsters`;
- combat attack by label or `monster_id`;
- guidance to start with vanilla monsters, then layer mod monster support only after the lab proof is stable.

## Non-Goals

- Do not add direct kill, direct damage, or direct loot-spawn commands as the proof path.
- Do not implement custom SVE monster construction in Slice 19.
- Do not encode SVE locations, monster names, or drop rules in Frobby production code.
- Do not mutate tracked save fixtures in place.
- Do not require deterministic drop assertions if vanilla drops are random under the loaded mod set.

## Risks And Mitigations

Risk: Creating a runtime location may require map data that is awkward to construct in-process.

Mitigation: use the smallest reliable implementation during planning, such as a generated passable map or a cloned vanilla map with lab contents cleared. Keep the external API independent of that internal choice.

Risk: Monster identities could leak across scenario resets.

Mitigation: clear the lab location and identity registry on `combat_lab.reset` and scenario end.

Risk: Repeated attacks by identity could still miss moving monsters.

Mitigation: resolve the monster's current tile before every attack and keep timeout diagnostics explicit. If movement remains too flaky, the live proof can use a low-health monster, short arena, or temporary lab movement controls in a later hardening pass.

Risk: Drops are random or changed by mods.

Mitigation: make identity/removal the first completion proof. Add deterministic drop assertions only when the selected monster and mod set make them reliable.

## Completion Criteria

- Frobby exposes a neutral Combat Lab reset action.
- Frobby can spawn at least one vanilla monster in the lab with stable run-local identity.
- `state.location.monsters` includes identity fields without breaking existing consumers.
- Runner targeting and waits can use `monster_id` or lab `label`.
- SVE has a passing headless scenario proving vanilla monster identity and removal inside the lab.
- Mod monster support is documented as the next follow-up, not hidden inside the first slice.
