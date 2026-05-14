# SVE Slice 15: Config Pack Profiles Design

## Context

Stardew Valley Expanded ships optional or adjacent content packs that change the tested runtime shape of the mod. Grandpa's Farm is the first useful proof pack because it changes the farm experience, depends on the same shared mod stack as SVE core, and should be runnable without contaminating a player's normal Stardew install.

This slice should make Frobby better for every large Stardew mod that needs alternate test environments. The implementation should not know about SVE, Grandpa's Farm, Immersive Farm 2 Remastered, Frontier Farm, or any specific Content Patcher pack. SVE only supplies the first real-world scenario.

## Goals

- Add a neutral Frobby profile concept for named mod/config test environments.
- Let scenarios declare the profile they require so tests are repeatable from the JSON file.
- Stage profile-specific mod sets into isolated repo-local cache folders instead of the user's game `Mods` directory.
- Support inheritance so profiles can share a core dependency stack and add only the variant pack paths they need.
- Record the selected profile and staged mod roots in reports.
- Prove the feature with an SVE Grandpa's Farm scenario.

## Non-Goals

- Do not merge or mutate SVE's master branch.
- Do not add SVE-specific logic to Frobby production code.
- Do not implement every alternate SVE farm pack in this slice.
- Do not add dependency downloading or version solving. Profiles point at existing local mod folders for now.
- Do not build a GUI profile selector in this slice.

## Approaches Considered

### Scenario-Declared Profiles

Add profile definitions to `sdv-test.config.json` and let scenarios declare a `profile` field. The runner resolves the profile, stages inherited and local mod paths, and records the profile in report metadata. This is the preferred approach because the test file documents its required environment and can be rerun without remembering extra command flags.

### Runner-Only Profile Flag

Add a CLI flag such as `--profile sve-grandpas-farm`. This is useful as a convenience, but it is weaker as the primary model because the scenario JSON would not describe its environment. Reports would also be less clear when someone runs a scenario with the wrong profile.

### Per-Scenario Extra Mod Paths

Let scenarios list extra mod folders directly. This is fast to implement, but it scales poorly once a repo has several optional packs that share most dependencies. It would duplicate paths across scenarios and make nontechnical setup harder to explain.

## Recommended Design

### Profile Configuration

Extend the repo-local Frobby config with a `profiles` object. Each profile has a stable id and may inherit from one parent profile.

Profile fields:

- `inherits`: optional parent profile id;
- `extra_mods`: additional local mod folders to stage for this profile;
- `config_overlays`: optional config files to copy into staged mod folders before launch;
- `cache_namespace`: optional stable cache folder name. When omitted, the profile id is used.

The existing top-level `extra_mods` behavior remains valid and becomes the implicit shared base. This keeps current Starberg and SVE core tests working while giving large mods a cleaner way to express variants.

Example intended shape:

```json
{
  "extra_mods": [
    "${SDV_GAME_MODS}/Content Patcher",
    "${SDV_GAME_MODS}/Farm Type Manager"
  ],
  "profiles": {
    "sve-core": {
      "extra_mods": [
        "../Stardew Valley Expanded/[CP] Stardew Valley Expanded"
      ]
    },
    "sve-grandpas-farm": {
      "inherits": "sve-core",
      "extra_mods": [
        "../Grandpa's Farm/[CP] Grandpa's Farm"
      ],
      "cache_namespace": "sve-grandpas-farm"
    }
  }
}
```

Exact paths should follow the SVE repo's generated scaffold conventions during implementation. The schema should remain path-based and neutral.

### Scenario Selection

Extend scenario specs with an optional `profile` field:

```json
{
  "id": "20-sve-grandpas-farm-profile",
  "name": "SVE Grandpa's Farm profile loads alternate farm pack",
  "profile": "sve-grandpas-farm"
}
```

When a scenario declares a profile, the runner resolves that profile before launching Stardew. A CLI profile override can be added as a convenience only if it does not obscure the scenario-declared profile in reports.

If a scenario references an unknown profile, the runner fails before launching the game with a clear config error.

### Profile Staging

Frobby should stage resolved profile mods into a profile-specific cache under the repo-local framework cache. The staged output should be independent of the player's normal game install.

The staging process should:

- resolve inherited profiles from parent to child;
- preserve existing managed-mod cleanup behavior;
- stage shared top-level `extra_mods` plus inherited profile `extra_mods`;
- deduplicate equivalent source paths while preserving deterministic order;
- fail early when a profile path does not exist;
- copy profile config overlays after source mods are staged;
- avoid deleting unmanaged files outside the active profile cache.

Config overlays are intentionally minimal. They copy a repo file into a staged mod folder, which is enough for config-gated Content Patcher or mod settings without inventing a larger config editor.

### Report Metadata

Reports should show the selected profile near the existing run metadata. Per-run metadata should include:

- selected profile id or `default`;
- resolved cache namespace;
- staged mod source paths;
- config overlays applied.

This helps a mod developer understand whether a failure came from the core profile or an alternate pack profile.

### SVE Proof Scenario

Add one SVE scenario after the current Spirit's Eve coverage:

- scenario id: `20-sve-grandpas-farm-profile`;
- profile: `sve-grandpas-farm`;
- assert the Grandpa's Farm Content Patcher pack is loaded by unique id;
- assert the player can enter or observe the active farm map under the profile;
- assert at least one neutral runtime map/content signal that differs from the SVE core profile, such as map asset identity, map dimensions, or a stable alternate-farm content marker;
- capture a screenshot for the report.

The exact assertion should be chosen from SVE's real Grandpa's Farm content during implementation. The important point is that the proof uses existing neutral Frobby map/content tools or small neutral extensions, not pack-specific code.

## Error Handling

- Unknown profile ids fail before launch.
- Cyclic profile inheritance fails during config load.
- Missing mod paths fail before launch and name the missing profile and path.
- Missing overlay source files fail before launch.
- Overlay targets that do not resolve to staged mod folders fail before launch.
- Runtime scenario failures should include profile metadata in the report.

## Test Strategy

Use TDD before implementation:

- config model tests for profile parsing, inherited profile resolution, duplicate path handling, and cycle detection;
- runner tests for scenario-declared profile selection and unknown-profile failures;
- protocol/deployer tests for profile-specific staging and managed cleanup boundaries;
- report tests for profile metadata rendering;
- SVE headless proof run for `20-sve-grandpas-farm-profile`;
- a Starberg smoke test to catch compatibility regressions in default-profile behavior.

## Risks

- Optional SVE pack folder names may vary across user checkouts. The generated SVE scaffold should keep local paths clear and editable.
- Config overlays could become a broad feature. This slice should keep them as file copies only.
- Some alternate farm differences may be hard to assert without a new neutral map asset query. If needed, add the smallest generic map/content assertion that can help other mods too.

## Completion Criteria

- Frobby supports named, inherited test profiles from repo-local config.
- Scenario JSON can declare a profile.
- Profile staging uses isolated repo-local cache folders and leaves the user's game `Mods` directory alone.
- Reports identify the selected profile and staged inputs.
- SVE has a Grandpa's Farm proof scenario using the profile.
- The SVE capability tracker marks Slice 15 complete after the proof passes.
- Existing default-profile tests still work, including a Starberg smoke run.
