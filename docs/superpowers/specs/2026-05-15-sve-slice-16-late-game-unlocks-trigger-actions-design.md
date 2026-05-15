# SVE Slice 16 Late-Game Unlocks And Trigger Actions Design

## Context

Slice 16 uses Stardew Valley Expanded as a pressure test for progression content
that changes over time. SVE has several systems that are hard to validate with
static load checks alone:

- `Data/TriggerActions` entries that react to `LocationChanged`, `DayEnding`,
  and `DayStarted`.
- Event and mail gates that unlock later maps, regions, and NPC behavior.
- Content Patcher map edits that apply after event/mail state changes and a
  location or day boundary re-evaluates conditions.

Frobby already has neutral setup primitives for `player.add_mail`,
`player.add_event_seen`, `player.warp`, `time.next_day`, `content.asset`,
`state.map_tile`, `state.tile_actions`, and `world.interact_tile_action`.
The missing part is convenient, deterministic observation of the player
progression state that trigger actions mutate.

## Goals

1. Keep Frobby mod-agnostic. SVE-specific event ids, mail ids, map names, and
   coordinates live only in SVE scenarios.
2. Exercise trigger actions through real game boundaries instead of adding a
   mod-specific "run this trigger action" shortcut.
3. Add player progression observability needed by any mod test suite:
   received mail, tomorrow mail, and seen events.
4. Verify SVE late-game unlocks by seeding prerequisites, crossing a real
   trigger boundary, and asserting resulting player/map/action state.
5. Preserve current Starberg and SVE scenario behavior.

## Non-Goals

- Do not implement Frontier Farm minecart or bridge unlock coverage in this
  slice. That needs farm-type fixture support and belongs after the alternate
  farm profile follow-ups.
- Do not add a generic trigger-action execution RPC. The tests should prove the
  game and Content Patcher integration path, not bypass it.
- Do not parse SVE content-pack source JSON as the test oracle. Tests should use
  live runtime assets and game state through Frobby.

## Alternatives Considered

### Recommended: Real Boundaries Plus Progression Waits

Add a small progression-state expansion to `state.player`, then exercise
`LocationChanged` and `DayEnding` trigger actions by warping or running
`time.next_day`. This is the best fit because it validates the same path a
player uses while keeping Frobby neutral.

### Direct Trigger-Action Runner

Add an RPC that finds and runs one `Data/TriggerActions` entry by id. This would
be convenient, but it risks hiding broken event wiring, trigger conditions, or
SMAPI/Content Patcher boundary behavior. It is not the right first slice.

### Frontier Farm Unlock First

Start with minecart, bridge, or desert shortcut unlocks. These are strong SVE
late-game examples, but they require farm-type fixture support beyond the
Grandpa's Farm profile proof from Slice 15. They should remain follow-ups.

## Frobby Design

### `state.player` Progression Fields

Extend the existing `state.player` response with:

- `mail_for_tomorrow`: normalized pending mail ids that the game has scheduled
  for a later delivery.

The existing fields remain unchanged:

- `mail_received`
- `events_seen`

The implementation should read the local/master farmer mail collections using
the same style as `mail_received` and `events_seen`. Empty or missing runtime
collections should project as empty lists, not errors, so tests remain portable
across Stardew versions.

### `wait.player` Progression Filters

Extend the runner-only `wait.player` action with list-membership filters:

- `mail_received`
- `mail_for_tomorrow`
- `event_seen`

Each filter succeeds when the requested string is present in the corresponding
`state.player` list. The filters compose with existing `wait.player` filters
such as location, tile, health, swimming, and buffs.

Timeout diagnostics should include short summaries of `mail_received`,
`mail_for_tomorrow`, and `events_seen` counts plus the requested missing value.
That gives mod authors a useful failure message without dumping huge save-state
lists into every report.

### Runtime Trigger-Action Inspection

Use the existing `content.asset` RPC against `Data/TriggerActions` to verify
that the SVE trigger action under test exists in the live asset graph. No new
Frobby RPC is needed for trigger-action listing in this slice.

## SVE Scenario Design

### Scenario 21: LocationChanged Trigger Action

Purpose: prove that a real SVE `LocationChanged` trigger action can mutate
player progression state.

Source pressure:

- `code/NPCs/Magnus.json` adds a trigger action that marks event `1000035` seen
  when the player is in year 2 or has seen event `418172`.

Flow:

1. Load the standard SVE fixture.
2. Assert the live `Data/TriggerActions` asset contains the Wizard basement
   event marker action.
3. Seed event `418172` with `player.add_event_seen`.
4. Warp the player to a safe location, then to `Custom_WizardBasement` so
   Content Patcher observes a location-change boundary.
5. Use `wait.player` with `event_seen: "1000035"`.
6. Capture a final frozen screenshot for report review.

Expected result: event `1000035` appears in `state.player.events_seen` without
Frobby calling SVE-specific code.

### Scenario 22: DayEnding Trigger Action

Purpose: prove that a real SVE `DayEnding` trigger action schedules mail for a
future day.

Source pressure:

- `code/Other/Mail.json` adds the Henchman tonic trigger action. When event
  `1337737` has been seen, it runs `AddMail Current HenchmanMarshTonics
  tomorrow`.

Flow:

1. Load the standard SVE fixture.
2. Assert the live `Data/TriggerActions` asset contains the Henchman tonic
   trigger action.
3. Seed event `1337737` with `player.add_event_seen`.
4. Run `time.next_day`, which raises Frobby's deterministic `DayEnding` then
   `DayStarted` boundary.
5. Use `wait.player` with `mail_for_tomorrow: "HenchmanMarshTonics"`.
6. Capture a final frozen screenshot for report review.

Expected result: `HenchmanMarshTonics` appears in `state.player.mail_for_tomorrow`.
This intentionally checks scheduled mail, not a mailbox UI, because Frobby's
deterministic day transition does not run the full overnight sleep/save flow.

### Scenario 23: Progression-Gated Map Or Action Mutation

Purpose: prove that late-game SVE progression state changes the active map or
map actions after Content Patcher re-evaluates conditions.

Preferred pressure target:

- Enchanted Grove unlock progression, because it is core SVE and does not need
  an alternate farm fixture.

Flow:

1. Seed the SVE progression event state required for one Enchanted Grove map
   mutation.
2. Warp into the affected location to force an `OnLocationChange` content update.
3. Assert the expected `state.map_tile` or `state.tile_actions` change at the
   selected map patch coordinate.
4. If the changed tile exposes an action, trigger it with
   `world.interact_tile_action` and assert the resulting location/state.
5. Capture a final frozen screenshot for report review.

The implementation plan will lock the exact coordinate and assertion from the
live runtime map during the red-test step, then keep that value in the SVE
scenario. Frobby stays generic; SVE owns the event ids and map coordinates.

## Testing

Frobby unit coverage:

- Protocol serialization for `mail_for_tomorrow`.
- `state.player` projection tests for received mail, tomorrow mail, and seen
  events.
- `wait.player` unit tests for success, timeout, composition with other filters,
  and diagnostic details.
- Scenario-loader/schema coverage for the new wait-player arguments.

SVE verification:

- Run the new scenarios headlessly through the repo-local runner.
- Run a small existing SVE smoke subset to confirm no regression in earlier
  state/action primitives.
- Run the Starberg smoke subset if Frobby code changes affect shared
  `state.player` or `wait.player` behavior.

## Acceptance Criteria

- `state.player` includes `mail_for_tomorrow` without breaking existing fields.
- `wait.player` can wait for `mail_received`, `mail_for_tomorrow`, and
  `event_seen`.
- SVE proves one `LocationChanged` trigger-action effect.
- SVE proves one `DayEnding` trigger-action mail-scheduling effect.
- SVE proves one late-game progression-gated map or action mutation.
- Frobby docs and SVE/Frobby TODO entries are updated with the completed slice
  status after implementation.
