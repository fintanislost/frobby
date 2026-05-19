# SVE Slice 18 Hoe/Dig Tool-Use Design

## Goal

Add neutral Frobby support for player-like hoe/dig tool use, then prove it against Stardew Valley Expanded's Secret Note #18 buried reward patch.

## Context

SVE has already pushed Frobby through custom locations, events, runtime content assets, NPC state, object interactions, containers, alternate farm profiles, and progression gates. Slice 13 originally called out buried reward interactions, but the implemented proof focused on placed object interaction through SVE's Golden Piggy Bank. Slice 14 then added neutral container item projection for the Spirit's Eve chest.

The remaining gap is tool-use interaction. SVE relocates the vanilla Secret Note #18 buried reward by patching `Desert.checkForBuriedItem(int xLocation, int yLocation, bool explosion, bool detectOnly, Farmer who)`. Its patch checks whether the farmer has seen secret note 18, whether the `SecretNote18_done` mail flag is absent, and whether the checked tile is `(9,43)`. If all conditions match, it adds the `SecretNote18_done` mail flag and creates object debris for item `127`.

This is a useful proof case because it requires Frobby to drive the game through a neutral tool path that mod Harmony patches can observe. Frobby should not add an SVE-specific "give buried reward" shortcut.

## Recommended Approach

Add a generic `world.use_tool` action with Hoe as the first supported tool.

Request shape:

```json
{
  "action": "world.use_tool",
  "args": {
    "tool": "Hoe",
    "location": "Desert",
    "x": 9,
    "y": 43,
    "facing": "down",
    "power": 0
  }
}
```

The harness should resolve the current loaded location, validate the optional `location` guard, find the requested tool in the player inventory, orient the player if `facing` is supplied, and invoke Stardew's native tool-use path for the target tile. The first implementation only needs to accept `Hoe` because the SVE proof is digging a buried reward, but the API should leave room for Axe, Pickaxe, Watering Can, Scythe, and Fishing Rod support in later slices.

The action response should include enough diagnostics to debug a failed scenario:

- requested tool name;
- resolved location name;
- target tile;
- whether a matching tool was found;
- selected tool index when available;
- whether the native use path was invoked;
- a short error code/message for validation failures.

The action should fail clearly when the game world is not ready, the requested location does not match the current location, the tile is invalid, or the player does not have the requested tool. It should not warp the player implicitly; scenarios should use `player.warp` first so movement/setup stays explicit.

## Supporting State

The SVE proof requires the save to know Secret Note #18 has been seen. Frobby should add this as neutral player state support:

- `player.add_secret_note_seen` to add a secret note id to `Game1.player.secretNotesSeen`;
- `state.player.secret_notes_seen` to project the current seen-note ids.

The proof can already use existing debris and mail observability:

- `state.player.mail_received` can assert `SecretNote18_done`;
- `state.location.debris` and `wait.location_content` can assert item debris id `127` after the dig.

If debris appears on the next update tick, the scenario should use an existing wait instead of asserting immediately after `world.use_tool`.

## Alternatives Considered

### Narrow `world.dig`

This would expose a smaller action that only digs one tile with a hoe. It is fast to implement, but it bakes the first tool use into the API and gives future mods no natural path for other tools. It also risks encouraging bypass logic instead of player-like tool use.

### Full Tool Framework Now

This would add all common tools and richer cursor/charge behavior in one slice. It is attractive long term, but too broad for the current SVE proof. Hoe-first `world.use_tool` gives us a tested API shape without committing to unverified behavior for every tool.

### Direct Reward/Tile Patch Invocation

Frobby could call a known buried-item method or mutate mail/debris directly. That would make this one SVE scenario pass while failing the larger purpose: testing whether a mod's tool interaction path works in game. This approach is explicitly out of scope.

## SVE Proof Scenario

Add one SVE scenario, numbered after the current SVE suite:

`tests/sdv/26-sve-secret-note-dig.test.json`

Flow:

1. Load the normal SVE core profile and fixture.
2. Add secret note 18 to the player's seen-note list.
3. Warp to the Desert near the relocated Secret Note #18 tile.
4. Wait until the player is in `Desert`.
5. Use `world.use_tool` with Hoe at tile `(9,43)`.
6. Wait until `state.player.mail_received` contains `SecretNote18_done`.
7. Wait until `state.location.debris` contains object/item debris for id `127`.
8. Capture the final screenshot under freeze conditions for the HTML report.

The scenario should keep assertions state-first. Screenshots are for report readability, not the primary proof.

## Testing Strategy

Use TDD for implementation.

Protocol and schema tests:

- scenario schema accepts `world.use_tool`;
- `player.add_secret_note_seen` accepts positive note ids and rejects invalid ids;
- `state.player` can include `secret_notes_seen` without breaking existing consumers.

Harness tests:

- `world.use_tool` validates readiness, current location, tile bounds, and missing tools;
- Hoe lookup finds the player's existing hoe by type or stable game identifier;
- the handler reports useful diagnostics on success and failure;
- secret-note projection and mutation work through player state;
- the native tool-use seam is isolated enough to unit test validation without requiring a live game.

Runner tests:

- JSON scenario loading and result rendering preserve the new action labels;
- existing state assertions can check `secret_notes_seen`, `mail_received`, and debris filters.

Live verification:

- run the new SVE scenario headlessly;
- run a small SVE smoke subset that includes the latest object/container scenarios;
- run the focused Frobby test suites touched by protocol, harness, and runner changes.

## Documentation

Update the neutral Frobby docs, not just the SVE scenario:

- RPC/action reference for `world.use_tool` and `player.add_secret_note_seen`;
- scenario DSL examples for player-like tool use;
- SVE capability TODO status;
- docs/wiki example or capability entry that explains when to prefer tool use over direct state mutation.

The docs should describe the feature as "tool use" rather than "SVE Secret Note support".

## Non-Goals

- Do not implement all Stardew tools in this slice.
- Do not add SVE-specific coordinates, item ids, or mail ids to Frobby production code.
- Do not pathfind the player to the tile; the scenario setup remains explicit.
- Do not attempt to automate the full fishing minigame or combat tool behavior through this action.
- Do not mutate tracked fixture saves in place.

## Risks And Mitigations

Risk: Stardew's native tool-use path may depend on cursor position, player facing, or animation state more than a direct method call exposes.

Mitigation: keep the first implementation Hoe-only, validate against a live SVE run, and preserve enough diagnostics to see whether the target tile and location were correct.

Risk: The SVE patch may observe a lower-level location method rather than the exact tool method Frobby initially calls.

Mitigation: choose the implementation path by reading Stardew's Hoe and `GameLocation.performToolAction` behavior during implementation, then verify with the live SVE scenario before claiming completion.

Risk: Debris creation may be delayed or placed at a pixel position instead of a tile coordinate.

Mitigation: assert mail first, then use an existing `wait.location_content` debris filter with tolerant location matching rather than a single immediate equality check.

## Completion Criteria

- Frobby exposes a neutral Hoe-first `world.use_tool` action.
- Frobby can set and inspect seen secret notes through neutral player state.
- SVE has a passing headless scenario proving Secret Note #18's relocated buried reward through player-like hoe use.
- Existing Starberg/Frobby scenario behavior remains compatible.
- The feature is documented as a generic mod-testing capability.
