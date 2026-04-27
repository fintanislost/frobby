---
description: Author a new test scenario for a mod
argument-hint: <scenario-name> <mod-name>
---

# /scenario — Author a test scenario

You're going to author a new test scenario: **$ARGUMENTS**

## Steps

1. Ask what behavior the scenario should verify. Get specific:
   - What's the setup state?
   - What action triggers the behavior?
   - What's the observable outcome?
2. Choose or create a fixture save appropriate to the setup:
   - List existing fixtures in `tests/fixtures/`
   - If none fit, suggest creating one via `[tool] fixture create`
3. Draft the scenario JSON per `schemas/scenario.schema.json`.
4. Decide assertion type:
   - **State assertion** — the default; check `Game1.*` via state inspector
   - **Draw-call assertion** — for visual checks (sprite selection, position, layer)
   - **Bitmap assertion** — only if draw-call inspection can't express it (shader effects, procedural rendering)
5. Write the scenario file to `tests/scenarios/<mod-name>/<scenario-name>.test.json`.
6. Run it: `[tool] run tests/scenarios/<mod-name>/<scenario-name>.test.json`.
7. If it fails on first run, decide: is the test wrong, or did we find a real bug?

## Scenario hygiene

- Name scenarios after the behavior they verify, not the implementation.
  - ✓ `shop_shows_custom_item_when_unlocked`
  - ✗ `test_patch_17`
- One assertion surface per scenario when possible. Mixed state + draw assertions are fine, but keep the scope tight.
- Avoid time-advancing more than necessary. Each `time.advance` is another source of potential nondeterminism to control.
