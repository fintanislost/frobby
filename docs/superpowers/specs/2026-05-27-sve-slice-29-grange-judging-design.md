# SVE Slice 29 Grange Judging Progression Design

## Goal

Use Stardew Valley Expanded's custom Stardew Fair grange judging behavior to
harden Frobby's neutral support for festival progression that is more complex
than loading a festival map, talking to an actor, or opening a festival shop. A
coding agent should be able to start the Fair, trigger grange judging through a
player-like path when possible, wait for the live judging phase to complete, and
assert SVE's judging dialogue side effects.

## Context

Earlier SVE slices proved custom festival maps, active festival actors, festival
chests, festival shops, alternate shop currencies, visible shop row purchases,
and movie theater NPC tile clicks. The remaining Fair-specific gap is grange
judging: SVE patches the live festival event itself, not just static content.

SVE's `HarmonyPatch_CustomGrangeJudging`:

- prefixes `Event.initiateGrangeJudging()`;
- calls Stardew's private `judgeGrange()` method;
- replaces the vanilla Lewis judging route with SVE-controlled advanced moves;
- moves Marnie out of Lewis's judging path;
- sets `Fair_Judging` dialogue on actors such as Sophia, Andy, and Susan while
  judging is active;
- postfixes `Event.interpretGrangeResults()` to apply after-judging dialogue to
  Sophia, Andy, and Susan.

Frobby already has:

- `festival.start` for starting the current date's festival through Stardew's
  festival data;
- `wait.event_active` and `state.event` for festival actor/state snapshots;
- `world.interact_npc` and `input.click_tile` for actor/NPC interaction;
- `event.advance`, `wait.menu`, and menu-choice click helpers;
- `content.asset` and state assertions for SVE data and dialogue validation;
- screenshot capture and frozen final screenshots.

The likely missing surface is event progression observability/control for a
festival command that can run for several seconds and mutate actor dialogue.

## Options Considered

### Option A: Player-Like Lewis Flow

Enter the Fair, position the player near Lewis, interact with Lewis through the
same visible interaction path a player would use, select the judging prompt if
one appears, wait for the judging sequence to finish, and assert SVE-specific
dialogue side effects.

This is the preferred path because it validates the player-facing festival flow
and minimizes direct event internals.

### Option B: Neutral Festival Phase Trigger

If the player-like Lewis route is not stable enough under headless automation,
add a neutral Frobby helper that invokes a named live event command/phase such as
grange judging on the active event. The helper must be generic and must not
refer to SVE, Lewis, or the Fair by name.

This is acceptable as a fallback because it still tests the live Stardew event
and SVE's Harmony patch side effects, but it is less player-like.

### Option C: State-Only Patch Proof

Assert that SVE patched Fair festival data, judging strings, and actor
placement. This is useful supporting coverage, but it does not prove the live
judging progression or the Harmony patch behavior.

Decision: implement Option A first, with Option B as the planned fallback if the
live player-like flow cannot be driven reliably.

## Frobby Design

The first implementation pass should avoid new RPCs until a failing SVE scenario
identifies a real neutral gap.

The scenario should initially try existing primitives:

1. `time.set` to Fall 16, year 1, at the Fair start time.
2. `festival.start` with `location: "Town"`.
3. `wait.event_active` for a live festival with actors `Lewis`, `Sophia`,
   `Andy`, and `Susan`.
4. `player.warp` or equivalent positioning near Lewis on the active festival
   map.
5. `input.click_tile` to initiate Lewis's player-facing Fair interaction.
6. `wait.menu` and `event.advance` / menu-choice helpers if Stardew presents a
   prompt before judging.
7. `wait.event_active` or a new runner wait, only if needed, for actor movement
   or dialogue state changes.
8. `state.event`, `state.menu`, or `world.interact_npc` assertions to prove
   SVE's judging and after-judging dialogue became observable.

If Option A exposes a gap, any Frobby additions must remain mod-neutral. Valid
additions include:

- richer `state.event` projection for active festival command/progression state;
- event actor dialogue summaries when Stardew exposes actor dialogue stacks;
- runner waits for event actor movement completion or actor dialogue changes;
- a generic `event.invoke_command` or `festival.invoke_phase` fallback that
  invokes a named command on the active event and reports the command, tick, and
  resulting event state.
- direct `world.interact_npc` may be used only as a diagnostic comparison while
  designing the failing test; the final SVE scenario should use `input.click_tile`
  for the primary path unless Option B is required.

Invalid additions:

- hard-coded SVE, Fair, Lewis, Sophia, Andy, Susan, or grange identifiers in
  Frobby production code;
- direct calls to SVE classes;
- scenario-only sleeps as the only synchronization strategy when observable
  state can be exposed neutrally.

## SVE Scenario Design

Add scenario `tests/sdv/37-sve-fair-grange-judging-progression.test.json`.

Preferred flow:

1. Set player money and date/time to Fall 16, year 1, during Fair hours.
2. Start the Fair with `festival.start`.
3. Wait for `state.event.is_festival == true`.
4. Wait for SVE-added Fair actors:
   - `Sophia`;
   - `Andy`;
   - `Susan`.
5. Assert SVE Fair content patches:
   - `Data/Festivals/fall16` contains SVE additional characters;
   - `Strings/StringsFromCSFiles` contains SVE after-judging strings;
   - `Characters/Dialogue/Sophia`, `Characters/Dialogue/Andy`, or
     `Characters/Dialogue/Susan` exposes
     `Fair_Judging` or equivalent judging dialogue.
6. Capture the Fair before judging.
7. Trigger judging through Lewis using the player-like interaction path.
8. If prompted, choose the judging/start option with existing menu choice tools.
9. Wait for the judging sequence to reach an observable post-judging state.
10. Assert at least one SVE actor has the after-judging dialogue active or
    interactable.
11. Capture the final post-judging state.

If the player-like path cannot reliably trigger judging, use the fallback helper
from Option B and keep the SVE scenario assertions unchanged.

## Testing

Frobby unit coverage should be added only for changed framework behavior:

- Protocol tests for any new RPC request/result models.
- Harness tests for event projection fields or command invocation behavior.
- Runner tests for any new wait/action syntax.
- Regression tests for existing `wait.event_active` behavior if event projection
  changes.

SVE live coverage:

- Run scenario 37 headless under the `core` mod set.
- Re-run scenario 34 as the existing Stardew Fair star-token shop regression.
- Re-run scenario 32 as the existing active festival actor interaction
  regression.
- If `input.click_tile` is touched, re-run scenario 36 as the movie theater NPC
  click regression.

## Non-Goals

- Perfect grange score calculation validation.
- Building a full grange display item-placement helper.
- Testing every SVE Fair actor or every year-two Fair layout.
- Festival minigames unrelated to grange judging.
- SVE-specific production code inside Frobby.

## Acceptance Criteria

- The SVE scenario proves the Fair is active and SVE Fair actors are present.
- The scenario attempts grange judging through Lewis's player-facing route first.
- If a fallback trigger is required, the fallback is generic and documented.
- The scenario proves SVE's judging or after-judging dialogue side effects.
- Any Frobby changes are neutral and covered by red/green tests.
- Existing Fair shop and festival actor scenarios still pass headlessly.
