# Scenario Step Fragments

## Goal

Add loader-level scenario step composition so repeated mod UI setup can live in small JSON fragments instead of being copied across every `.test.json`.

## Implemented Flow

1. Scenario files may place `{ "include": "relative/path.steps.json" }` inside `steps`.
2. The include path resolves relative to the file that contains it.
3. Included files contain a JSON array of normal step objects.
4. Nested includes are supported.
5. `ScenarioLoader` expands includes before returning `ScenarioSpec`, so the runner only executes concrete `action` steps.
6. Include cycles are rejected with an `include cycle` load error.

## Validation

- Focused loader tests cover direct includes, nested relative includes, and cycle detection.
- Starberg scenarios 31 and 32 are the first real consumers through `tests/sdv/fragments/`.
