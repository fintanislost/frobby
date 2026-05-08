# Frobby-Local Mod Dependency Cache Design

## Goal

Decouple Frobby repository test runs from the user's playable Stardew Valley
`Mods` folder by adding a Frobby-local, gitignored dependency cache:

```text
sdv-test-framework/.cache/deps/
```

The cache stores test-only copies of dependency mods such as Content Patcher,
Farm Type Manager, and SpaceCore. `sdv-test repo run` then stages those cached
dependencies into Frobby's existing isolated test mods directory before launching
Stardew Valley.

This keeps test runs reproducible and avoids forcing users to arrange their live
game install around test framework needs.

## Non-Goals

- Do not download mods from the internet in this first version.
- Do not automatically update dependencies during normal test runs.
- Do not make the user's live Stardew `Mods` folder the dependency source of
  truth.
- Do not replace `extraMods`; repo-built mods and content packs still use
  `extraMods`.
- Do not introduce a global user cache as the default yet. A future version can
  migrate to `~/.cache/sdv-test-framework/deps` once the workflow is mature.

## Current Behavior

`sdv-test repo run` reads `sdv-test.config.json`, selects a `modSet`, resolves
`extraMods`, then passes those paths to `sdv-test run` as repeated
`--extra-mod` flags.

`sdv-test run` copies each `--extra-mod` folder into its isolated mods path using
`ExtraModDeployer`. This isolation is good, but SVE's current `core` mod set
uses paths like:

```json
"${SDV_GAME_MODS}/ContentPatcher"
"${SDV_GAME_MODS}/FarmTypeManager"
```

That means the test dependency source is still the user's live game install.

## Config Model

Add an optional `deps` array to each repo mod set:

```json
{
  "name": "core",
  "deps": [
    { "id": "Pathoschild.ContentPatcher", "version": "2.7.0" },
    { "id": "Esca.FarmTypeManager", "version": "1.23.0" }
  ],
  "extraMods": [
    "Stardew Valley Expanded/StardewValleyExpanded/bin/Release/net6.0",
    "Stardew Valley Expanded/[CP] Stardew Valley Expanded",
    "Stardew Valley Expanded/[FTM] Stardew Valley Expanded"
  ]
}
```

`id` is the dependency mod's `manifest.json` `UniqueID`. `version` is optional
for the first implementation. If present, `doctor` reports a mismatch when the
cached manifest version differs. Normal `repo run` must also fail on a version
mismatch so test runs do not silently drift.

`extraMods` remains required for repo-owned outputs in this design. Repos that
only validate third-party dependencies and have no repo-owned mod output are out
of scope for this pass.

## Cache Layout

Use one folder per `UniqueID`:

```text
.cache/deps/
  Pathoschild.ContentPatcher/
    manifest.json
    ...
  Esca.FarmTypeManager/
    manifest.json
    ...
```

The folder name is the sanitized `UniqueID`, using the same naming behavior as
`ExtraModDeployer`. The manifest inside the folder is authoritative; import and
doctor both validate that the folder's manifest `UniqueID` matches the expected
ID.

Add `.cache/` to Frobby's `.gitignore`.

## Commands

### `sdv-test repo deps import --from <path>`

Copies a dependency mod folder into `.cache/deps/<UniqueID>/`.

Behavior:
- Requires `<path>/manifest.json`.
- Reads `UniqueID` and `Version` from the manifest.
- Deletes and replaces any existing cached folder for that `UniqueID`.
- Prints the imported ID, version, source, and destination.

This command is intentionally local-only. Users can download or update a mod
however they normally would, then import that local folder into Frobby's test
cache.

### `sdv-test repo deps doctor [--mod-set <name>]`

Checks the selected mod set's `deps` entries against the Frobby-local cache.

Checks:
- required dependency folder exists under `.cache/deps`;
- cached folder has a valid `manifest.json`;
- manifest `UniqueID` matches the configured `id`;
- manifest `Version` matches configured `version`, when a version is provided;
- dependency path is stageable by `ExtraModDeployer`;
- legacy `${SDV_GAME_MODS}` dependency paths in `extraMods` are reported as a
  warning with a migration suggestion.

Exit codes:
- `0`: all configured dependencies are usable.
- `1`: dependencies are missing or version-mismatched.
- `2`: config or manifest is malformed.

### `sdv-test repo run`

When the selected mod set has `deps`, `RepoRunPlanner` resolves them from
`.cache/deps` and prepends them to the generated `--extra-mod` list before
repo-owned `extraMods`.

If a dependency is missing or version-mismatched, `repo run` fails before
launching Stardew and prints the matching `repo deps import --from <path>`
guidance.

## Path Resolution

Default dependency cache root:

```text
<sdv-test-framework-root>/.cache/deps
```

Add an override:

```text
SDV_TEST_MOD_CACHE=/path/to/deps
```

The override is useful for advanced users or future CI, but the default remains
inside the Frobby source tree for easier Windows and nontechnical-user
onboarding during this stage.

The framework root is the directory containing `sdv-test-framework.slnx`. Source
tree wrappers already `cd` there before running `repo run`, and direct source
tree usage can discover it by walking upward from the current directory or runner
assembly directory. Installed-tool behavior can require `SDV_TEST_MOD_CACHE`
until the project is mature enough to choose a global default.

## Data Flow

1. User imports dependencies:
   - `sdv-test repo deps import --from "C:\Games\Stardew Valley\Mods\ContentPatcher"`
2. Frobby copies the folder into `.cache/deps/Pathoschild.ContentPatcher`.
3. Repo config references the dependency by `UniqueID` in `modSets[].deps`.
4. `repo run` resolves each dependency to `.cache/deps/<UniqueID>`.
5. `sdv-test run` copies dependencies and repo-owned mods into its isolated
   runtime mods directory.
6. Stardew launches with only the test-selected mod set and Frobby harness.

The user's playable `Mods` folder is never read during normal `repo run` unless
the repo explicitly keeps old `${SDV_GAME_MODS}` paths in `extraMods`.

## Error Handling

Missing dependency:

```text
[repo deps] missing Pathoschild.ContentPatcher in .cache/deps.
Import it with: sdv-test repo deps import --from <path-to-ContentPatcher>
```

Version mismatch:

```text
[repo deps] Pathoschild.ContentPatcher version mismatch:
expected 2.7.0, found 2.6.0.
Import the expected version or update sdv-test.config.json intentionally.
```

Bad manifest:

```text
[repo deps] .cache/deps/Esca.FarmTypeManager/manifest.json is missing UniqueID.
```

## Testing

Unit coverage:
- `RepoTestConfigTests`: parses `deps` entries and validates required `id`.
- `RepoDependencyCacheTests`: resolves cache paths, reads manifest metadata,
  detects missing dependencies and version mismatch.
- `RepoCommandTests`: `repo deps import` copies a fixture mod into `.cache/deps`.
- `RepoCommandTests`: `repo deps doctor` reports pass/fail cases.
- `RepoRunPlannerTests`: selected `deps` become `--extra-mod` arguments before
  repo-owned `extraMods`.

Live smoke:
- Convert SVE's `core` mod set to cached `ContentPatcher` and
  `FarmTypeManager`.
- Keep SVE code and SVE content packs as repo-relative `extraMods`.
- Run `./scripts/sdv-test --headless --mod-set core --no-build tests/sdv/01-sve-core-loads.test.json`
  after importing dependencies.

## Documentation

Update Frobby README with:
- where `.cache/deps` lives;
- how to import a dependency;
- how to run doctor;
- how `deps` differs from `extraMods`;
- why normal test runs do not touch the user's live Mods folder.

Update the neutral repo scaffold docs so new mod repos can adopt `deps` without
copying SVE-specific paths.

## Migration Notes

SVE will move external dependencies from `extraMods` to `deps`:

```json
"deps": [
  { "id": "Pathoschild.ContentPatcher" },
  { "id": "Esca.FarmTypeManager", "version": "1.19" }
]
```

SVE-owned outputs will become repo-relative `extraMods`:

```json
"extraMods": [
  "Stardew Valley Expanded/StardewValleyExpanded/bin/Release/net6.0",
  "Stardew Valley Expanded/[CP] Stardew Valley Expanded",
  "Stardew Valley Expanded/[FTM] Stardew Valley Expanded"
]
```

This migration can happen after the Frobby cache support lands. Do not merge SVE
config changes into SVE `master` unless the user explicitly asks for that merge.
