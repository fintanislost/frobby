# Save Fixture Management

Fixtures are deliberately-constructed SDV save files used as known starting states for scenarios. They're version-controlled and their correctness matters — a bad fixture invalidates every scenario that uses it.

## Location and layout

```
tests/fixtures/
  <n>.sav              # the actual save file (Stardew format)
  <n>.meta.json        # metadata
  <n>.README.md        # human-readable description
```

## Metadata schema

```json
{
  "name": "spring_day_5_clean",
  "description": "Spring Day 5, fresh farm, 1000g, all NPCs at default friendship",
  "sdv_version": "1.6.15",
  "smapi_version": "4.1.10",
  "mods_installed": [],
  "created_at": "2026-05-14T12:00:00Z",
  "created_by": "fixture-builder",
  "seed": 42,
  "farmer": {
    "name": "Tester",
    "gender": "female",
    "farm_type": "standard"
  },
  "regenerate_with": "tools/fixture-scripts/spring_day_5_clean.sh"
}
```

## Creation paths

### Path A: `[tool] fixture create`

Interactive. Launches SDV, user plays to desired state, exits. Framework copies save + generates metadata. Good for ad-hoc fixtures.

### Path B: scripted regeneration

For fixtures that need to be reproducible from scratch (e.g., "day 5 with Pierre's shop already visited once"), ship a script in `tools/fixture-scripts/` that uses the harness itself to get from day 1 → desired state. Metadata references the script.

**Preference:** Path B for anything that'll outlive a point release. Saves can go stale when SDV updates its save format; scripts survive because they re-run against the current version.

## Staleness checks

`[tool] doctor` enumerates all fixtures and reports any with:
- `sdv_version` not matching installed version (warn, may still work)
- `sdv_version` in a known-incompatible range (error, regenerate required)
- Missing metadata (error)
- Corrupt save (detected by attempting load; error)

## Do-not-do

- Don't hand-edit save XML. SDV will mostly-refuse to load saves it thinks are corrupted, and debugging "why does this fixture crash on load" eats days.
- Don't check in saves with mods baked in unless the scenario specifically tests that mod combination. Fixtures should be minimal.
- Don't reference fixtures by absolute path. Scenarios reference by name; the loader resolves.

## Git LFS

Saves are binary and can be large (hundreds of KB). Use Git LFS for the `.sav` files once the project has >5 fixtures. Metadata and READMEs stay in regular git.
