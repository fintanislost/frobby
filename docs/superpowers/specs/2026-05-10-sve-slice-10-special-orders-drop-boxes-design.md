# SVE Slice 10: Special Orders, Quest State, And Drop Boxes Design

## Context

Slice 9 gave Frobby enough neutral combat lifecycle coverage to observe monster
state, player health, and dropped debris. The next high-value Stardew Valley
Expanded pressure point is special orders: SVE adds custom orders, event-gated
orders, untimed order patches, custom NPC requesters, and custom drop boxes. These
are long-running mod systems where a developer needs to prove both data registration
and runtime progress, not just that a map or NPC loaded.

Core SVE is enough for this slice. Frontier Farm and Grandpa's Farm also add special
orders, but they should remain later config-pack/farm-variant pressure tests. Slice
10 should establish the neutral Frobby surface using core SVE only.

## Goals

- Add a neutral runtime state RPC for Stardew special orders.
- Expose enough order, objective, donation, reward, and completion metadata to let
  scenario assertions describe the current order lifecycle.
- Add runner polling for special-order state so tests can wait for event-gated or
  day-start order changes without fixed sleeps.
- Add a neutral drop-box deposit action that uses Stardew special-order runtime state
  rather than SVE-specific code paths.
- Add one core SVE proof scenario that seeds/unlocks a custom order, waits for it,
  deposits required items, and asserts objective/donation progress.

## Non-Goals

- Do not hardcode SVE order keys, event IDs, NPCs, locations, or drop boxes in
  Frobby production code.
- Do not parse SVE Content Patcher files inside Frobby as the source of truth.
- Do not include Frontier Farm, Grandpa's Farm, or alternate farm packs in this
  slice.
- Do not implement a full special-orders board UI driver before runtime state is
  reliable.
- Do not complete every possible Stardew objective type. This slice needs robust
  projection and a deposit path for donation-style objectives; later slices can add
  richer action helpers for fishing, shipping, combat, or mine-floor objectives.

## Approaches Considered

### Runtime State Plus Native Deposit

Add `state.special_orders`, `wait.special_order`, and `drop_box.deposit`. The state
RPC projects live Stardew order data; the runner wait polls that state; the deposit
action uses Stardew's special-order donation model where possible. This is the
recommended approach because it matches the pattern that has worked for SVE slices:
observe neutral runtime state first, then drive a focused gameplay action, then
assert the resulting state.

### Read-Only Special Order Coverage

Only add `state.special_orders` and `wait.special_order`, then prove an SVE order
appears after seeded prerequisites. This is lower risk, but it stops short of the
player-facing drop-box loop that makes special orders interesting to test.

### UI-First Special Order Coverage

Drive the special-orders board and drop-box menu entirely through text capture and
clicks. This would be highly player-like, but it is likely to be brittle before
Frobby can assert the underlying order state. It should be a later layer once the
state and deposit primitives exist.

## Recommended Design

### Special Order State

Add a new harness RPC, `state.special_orders`, with an additive protocol model. The
RPC should require a loaded world and project the current team/player order state.

Initial response shape:

- `active`: active team special orders.
- `available`: available special orders, when Stardew exposes them.
- `completed`: completed special-order keys.
- `accepted_types`: accepted order types, when available.
- `returned_donations`: item summaries for returned donated items.

Each order summary should include:

- `key`: `questKey`.
- `name`, `description`, and `requester`.
- `order_type`, `special_rule`, `duration`, `due_date`, and `state`.
- `ready_for_removal`, `is_timed`, and `runtime_type` where available.
- `objectives`: ordered objective summaries.
- `rewards`: ordered reward summaries.
- `donated_items`: item summaries for the order's donated item list.
- `selected_random_elements` and `preselected_items` as string dictionaries where
  available, because many orders bind random choices into objectives.

Objective summaries should be best-effort and reflection-tolerant:

- `type` and `runtime_type`.
- `description`, `current_count`, `max_count`, and `complete`.
- `drop_box`, `drop_box_location`, and `drop_box_tile` for donation objectives.
- `target_name` for delivery objectives.
- `accepted_context_tags` as a list or normalized string collection.
- `confirmed`, `minimum_capacity`, and other simple scalar metadata when exposed.

Reward summaries should include runtime type and simple scalar fields such as amount,
mail ids, friendship target, or gem count when Stardew exposes them. Unknown reward
details should not break the snapshot.

Item summaries should reuse the same identity fields used by player inventory and
location debris: raw id, item id, qualified id, display/name, stack, quality,
category, and runtime type.

### Runner Wait

Add `wait.special_order` as a runner-only action that polls `state.special_orders`.

The wait should support:

- `collection`: `active`, `available`, or `completed`, defaulting to `active`.
- order filters: `key`, `name`, `requester`, `order_type`, `special_rule`, `state`,
  `is_timed`, and `ready_for_removal`.
- objective filters: `objective_type`, `objective_runtime_type`, `drop_box`,
  `drop_box_location`, `target_name`, `accepted_context_tag`, `current_count`,
  `current_count_gte`, `max_count`, and `complete`.
- count filters: `min_count` and optional `max_count`.
- `timeout_ms` and `poll_ms`.

Timeout diagnostics should report the last observed active, available, and completed
keys plus the last match count. This keeps failure reports useful when a seeded event
does not produce an order or a deposit does not change objective progress.

### Drop Box Deposit Action

Add a harness RPC, `drop_box.deposit`, for tests that need to simulate the player
depositing items into a special-order drop box without parsing mod content.

Request shape:

- `order_key`: required active special order key.
- `drop_box`: optional box id when a single order has multiple donation objectives.
- `item_id` or `qualified_id`: required item selector.
- `count`: positive amount to deposit.

The action should validate that:

- a world is loaded;
- the active order exists;
- a matching donation objective exists;
- the drop box selector matches when provided;
- the player inventory contains enough matching items;
- the selected items satisfy the objective's accepted tags or Stardew's objective
  validation API, when that API is available.

Implementation should first attempt to use Stardew's native special-order donation
logic or objective methods. If the public surface is too menu-bound, the fallback is
a neutral harness helper that updates the active `SpecialOrder` and `DonateObjective`
runtime fields the same way Stardew does: move matching items from player inventory
to the order's donated-item list and update objective progress. The fallback must be
generic to Stardew special orders and must not contain SVE-specific branches.

The response should include the order key, drop box, deposited item summary, deposited
count, and the affected objective's before/after counts.

### SVE Proof Scenario

Add one core SVE scenario after the Frobby primitives are green.

Preferred proof flow:

1. Load the deterministic fixture.
2. Seed the event/mail state required for a core SVE donation order that is managed
   by SVE's runtime special-order code.
3. Advance to the day or tick boundary that causes SVE to add the order.
4. `wait.special_order` for the expected active order using generic filters.
5. Assert objective/drop-box metadata from `state.special_orders`.
6. Give the player the required vanilla item(s).
7. Call `drop_box.deposit` for the active order/drop box.
8. `wait.special_order` for updated objective progress.
9. Capture final report evidence.

Candidate core SVE orders from current source inspection include custom event-gated
orders managed by `AddSpecialOrdersAfterEvents`, such as `MarlonFay2`, `Lance`,
`Krobus`, or other core SVE keys. During implementation, choose the candidate that
is deterministic, uses obtainable vanilla item ids/tags, and can be activated with
the fewest unrelated prerequisites. If no core SVE donation order can be made stable
quickly, use a vanilla special-order donation as the Frobby integration proof and add
a core SVE read-only order scenario in the same slice; keep the TODO entry open for a
follow-up SVE drop-box proof.

## Testing Strategy

Use TDD for every Frobby behavior.

Frobby protocol tests:

- `SpecialOrdersState` serializes active, available, completed, objectives, rewards,
  donated items, and returned donations in snake_case.
- `DropBoxDepositRequest` and result models serialize the expected fields.

Frobby harness tests:

- `state.special_orders` projects active orders, available orders, completed keys,
  objective metadata, donation boxes, reward summaries, and donated items from fake
  abstractions.
- Projection tolerates missing optional fields and unknown objective/reward types.
- `drop_box.deposit` validates missing order, missing objective, invalid item selector,
  insufficient item count, and successful donation progress.

Frobby runner tests:

- `wait.special_order` filters active, available, and completed collections.
- Objective filters match drop box, accepted tags, target names, and progress counts.
- Timeout diagnostics include last observed keys and match counts.

SVE verification:

- Run the new Slice 10 scenario headlessly with the shared Frobby dependency cache.
- Re-run nearby SVE scenarios that exercise event seeding, item giving, tile actions,
  and previous special-order-adjacent state if applicable.
- Keep generated reports under the existing `/tmp/stardew-valley-expanded-frobby-results-0.1.0/`
  grouping.

## Risks And Mitigations

- Stardew special-order internals may expose useful data through fields rather than
  public properties. Mitigation: build projection behind small abstractions and use
  reflection only at the edge.
- The native donation path may be menu-coupled. Mitigation: try native APIs first,
  then use a generic Stardew special-order fallback that updates runtime order state
  without SVE branches.
- Event-gated SVE orders may require several prerequisite events. Mitigation: choose
  the smallest stable core SVE candidate during implementation and document any
  remaining prerequisites in the scenario.
- Some orders use delivery objectives rather than drop boxes. Mitigation: Slice 10
  can project delivery metadata but should only add a deposit action for donation
  objectives.
- Donation objectives with multiple accepted tags can be easy to overfit. Mitigation:
  validate against Stardew context tags or objective methods where possible instead
  of string-matching only the displayed text.

## Completion Criteria

- Frobby exposes `state.special_orders` with active, available, completed,
  objective, reward, and donation metadata.
- Frobby runner supports `wait.special_order` with useful filters and timeout
  diagnostics.
- Frobby supports a neutral `drop_box.deposit` action for active donation objectives.
- At least one Slice 10 SVE scenario proves a special-order lifecycle beyond read-only
  loading, or the implementation documents a specific blocker and includes the best
  stable read-only SVE proof plus a vanilla drop-box proof.
- README, RPC schema, DSL quickstart, and SVE capability TODO are updated after
  implementation.
- Frobby targeted tests and relevant SVE headless scenarios pass before the work is
  marked complete.
