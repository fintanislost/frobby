# Tier 2 Texture-Hash Fallback — Design

**Milestone:** Roadmap Tier 1 (post-M3-subproject-2)
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session, auto-mode)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Close the 9.2% unresolved-textures gap from D1.5. Tier 1 resolves 90.8% via SMAPI's
`IContentEvents.AssetReady` event, but 9.2% of textures (engine pre-mod-load loads,
`new Texture2D(...)` allocations, dialogue portraits loaded before the hook fires)
produce draw events with `texture_asset = null`. This breaks the LLM-workflow goal:
when Claude writes a test asserting `texture_asset == "Characters/Abigail"` against a
modded portrait that missed Tier 1, the assertion silently fails with no diagnostic path.

Ship a **build-on-user-machine** texture hash manifest + runtime hash-on-cache-miss
lookup, plus a Tier 3 anonymous-fallback shape (`content_hash` + `texture_size`) for
textures that don't resolve at all. DrawFilter gains matching fields so assertions can
still work against Tier 3.

## Architecture

**Three-phase cascade at draw.snapshot time**, matching spec §4.2:

1. **Tier 1** (already shipped in D1.5): `ConditionalWeakTable<Texture2D, string>`
   populated from SMAPI's `IContentEvents.AssetReady` event. Resolves ~90.8% of textures.

2. **Tier 2** (this subproject): on cache miss, hash the texture bytes (SHA-256 over
   `Color[]` data from `GetData`) + look up in a pre-built manifest. If hit, populate
   the Tier 1 weak map for future lookups + emit the resolved asset path.

3. **Tier 3** (this subproject): if Tier 2 misses (manifest absent, or hash not in
   manifest), emit the draw event with `texture_asset = null` + new fields
   `content_hash` (16-hex-char prefix of SHA-256) + `texture_size = {w, h}`. Assertions
   match on these instead of `texture_asset`.

**Manifest generation** is a new command, not a shipped artifact:
- New harness RPC: `diagnostic.build_texture_manifest` — iterates SDV's content loader,
  hashes every loadable texture, returns the full `{hash → asset_path}` map as JSON.
- New Runner CLI: `sdv-test build-manifest` — drives the RPC, streams result to
  `~/.cache/sdv-test-framework/texture-manifests/<sdv-version>.json`. Progress printed
  to stderr. Takes 30-60 seconds against vanilla SDV content.
- Harness loads the manifest at startup if the file exists for the current `Game1.version`.
  If missing, Tier 2 is a no-op; first assertion hitting Tier 2 emits a warning with
  the fix instruction.

**Hash-on-query, not hash-at-record.** Per spec §4.2: "Only hash on cache miss, and
only when someone actually queries for the path." The hash happens inside
`DrawSnapshotHandler.ToDto` when the Tier 1 lookup returns null — not in the hot
`SpriteBatch.Draw` prefix. Zero overhead on non-asserted textures.

**Cache hashing results.** Once a texture hashes, store the result in the same
`ConditionalWeakTable<Texture2D, string>` — so the second query against the same
`Texture2D` reference skips re-hashing. Weak references ensure no GC-root leak.

## Components

**New files (Harness):**

- `src/Harness/Assets/TextureHashManifest.cs` — loads the JSON manifest at startup;
  `TryResolve(byte[] hash) → string?` lookup. Empty if file absent.
- `src/Harness/Assets/TextureHasher.cs` — `ComputeHash(Texture2D) → byte[]` — reads
  `Color[]` via `GetData`, SHA-256s the span. Runs on game thread (GetData requires it).
- `src/Harness/Handlers/DiagnosticBuildManifestHandler.cs` — `diagnostic.build_texture_manifest`
  RPC. Iterates `Game1.content.Load<Texture2D>` over a known path list (or uses
  reflection to enumerate loaded content), hashes each, returns the complete map.

**New files (Runner):**

- `src/Runner/Commands/BuildManifestCommand.cs` — `sdv-test build-manifest [--output <path>]`.
  Launches SDV, invokes `diagnostic.build_texture_manifest`, writes the JSON to
  `~/.cache/sdv-test-framework/texture-manifests/<version>.json` (or `--output`).
  Prints progress to stderr.

**Modified (Harness):**

- `src/Harness/Assets/TextureAssetRegistry.cs` — gains a `TryResolveWithFallback(Texture2D)`
  method that chains Tier 1 → Tier 2 → emits null. Existing `Register(Texture2D, string)`
  stays for Tier 1 population.
- `src/Harness/Handlers/DrawSnapshotHandler.cs` — `ToDto(DrawEvent)` uses
  `TryResolveWithFallback`; populates new DTO fields `ContentHash` + `TextureSize` when
  Tier 1 misses.
- `src/Harness/Handlers/DrawFilterMatcher.cs` — evaluates new filter fields
  `content_hash` + `texture_size` when present.
- `src/Harness/Handlers/DrawFilterValidator.cs` — validates the new fields.
- `src/Harness/ModEntry.cs` — registers `DiagnosticBuildManifestHandler`, loads the
  manifest at startup (quiet no-op if absent).

**Modified (Protocol):**

- `src/Protocol/Models/DrawEventDto.cs` — add `ContentHash: string?` (16-hex-char
  prefix; full SHA-256 is 64 hex but 16 is sufficient for collision avoidance in a
  single-SDV-install manifest) + `TextureSize: int[]` (`[width, height]`).
- `src/Protocol/Models/DrawFilter.cs` — add `ContentHash: string?` + `TextureSize: int[]?`.

**Modified (Runner):**

- `src/Runner/Program.cs` — dispatch `build-manifest` + help text.

**New tests (~10 passing + 1 skipped):**

- `tests/Harness.Tests/TextureHashManifestTests.cs` — 3 tests:
  - `Load_MissingFile_ReturnsEmptyManifest`
  - `Load_ValidJson_ResolvesHashToPath`
  - `TryResolve_UnknownHash_ReturnsNull`
- `tests/Harness.Tests/TextureHasherTests.cs` — 2 tests (with synthetic `Color[]`
  data — we bypass GetData in tests by taking the array directly):
  - `ComputeHash_SameData_ReturnsSameHash`
  - `ComputeHash_DifferentData_ReturnsDifferentHash`
- `tests/Harness.Tests/DrawFilterMatcherTests.cs` — extend with 2 tests:
  - `ContentHash_Matches_ReturnsTrue`
  - `TextureSize_ArrayMatch_ReturnsTrue`
- `tests/Harness.Tests/DrawFilterValidatorTests.cs` — extend with 1 test:
  - `ContentHash_NonHexChars_ThrowsInvalidParams`
- `tests/Runner.Tests/BuildManifestCommandTests.cs` — 2 tests (arg-parse):
  - `MissingExplicitOutput_DefaultsToCacheDir`
  - `UnknownFlag_ReturnsTwo`
- `tests/Harness.Tests/TextureHashIntegrationTests.cs` — 1 skipped integration placeholder
  (`Tier2HashResolution_RealTextures` — verified manually via T-final smoke).

**Target test count:** 298+37 → **~308 Passed + 38 Skipped** (+10 passed, +1 skipped).

## Wire shapes

### New harness RPC: `diagnostic.build_texture_manifest`

**Params:** none.

**Response:**
```json
{
  "sdv_version": "1.6.15",
  "texture_count": 4217,
  "manifest": {
    "<16-hex-prefix>": "Characters/Abigail",
    "<16-hex-prefix>": "LooseSprites/Cursors",
    ...
  }
}
```

Progress is not streamed — the harness batches the full map and returns once complete.
~60s typical; acceptable for a one-time operation. MCP streaming tool results (Tier 3
roadmap) would improve this but isn't needed for MVP.

### Extended `DrawEventDto`

```json
{
  "tick": 84231,
  "texture_asset": null,                               // Tier 1 + 2 both missed
  "content_hash": "a1b2c3d4e5f6a789",                  // new (Tier 2/3)
  "texture_size": [512, 1002],                         // new (Tier 2/3 only — Tier 1 already has this implicit via the resolved asset)
  "source_rect": { "x": 0, "y": 0, "w": 512, "h": 512 },
  "dest_rect": { ... },
  ...
}
```

When Tier 1 or Tier 2 resolves, `texture_asset` is populated AND `content_hash` +
`texture_size` are also populated — gives assertions the option to match on either.
(Previously: only `texture_asset`. Future scenarios could choose hash-based matching
for stability against content pipeline changes.)

### Extended `DrawFilter`

```json
{
  "content_hash": "a1b2c3d4e5f6a789",
  "texture_size": [512, 1002]
}
```

Filter logic: all fields `AND` together (existing behavior). `content_hash` is a prefix
match to allow users to specify just the first 8 or 16 chars.

### CLI: `sdv-test build-manifest [--output <path>] [--mods-path <path>]`

```bash
$ sdv-test build-manifest
[build-manifest] launching SDV...
[build-manifest] harness ready, iterating content...
[build-manifest] hashed 4217 textures in 47.3s
[build-manifest] wrote /home/fintan/.cache/sdv-test-framework/texture-manifests/1.6.15.json (312 KB)
```

Exit codes: 0 success, 2 arg error, 4 SDV launch / RPC fatal. Same conventions as
other Runner commands.

## Error handling

- **Manifest file missing on harness startup** — log a one-line info message
  (`"texture-manifest for SDV 1.6.15 not found — Tier 2 resolution disabled; run 'sdv-test build-manifest'"`)
  and initialize an empty manifest. No error.
- **Manifest file corrupt (bad JSON, wrong SDV version)** — log a warning + ignore it.
  Tier 2 stays disabled. Don't fail-fast; the framework should still work with Tier 1
  alone.
- **Tier 2 resolution miss** (hash not in manifest) — silent fall-through to Tier 3.
  No warning per query (would spam). Summary stats exposed via `draw.snapshot` meta if
  future investigation needs them.
- **`GetData` fails on a Texture2D** (GPU-backed render target, disposed, etc.) — catch,
  fall through to Tier 3. One-line Monitor warning per session (not per query).
- **`diagnostic.build_texture_manifest` during manifest-build** — textures that fail to
  load are skipped with a log line. Build continues.
- **`DrawFilter` with invalid `content_hash`** (non-hex chars) — `InvalidParams` error
  per existing validator pattern.

## Testing

**Unit tests (10 passing + 1 skipped):**

- `TextureHashManifest` load + resolve (3).
- `TextureHasher` determinism + differentiation (2).
- `DrawFilterMatcher` new field matching (2).
- `DrawFilterValidator` new field validation (1).
- `BuildManifestCommand` arg parsing (2).
- Skipped integration placeholder (1).

**Smoke verification (manual):**

Done during the T-final task. Full flow:
1. Run `sdv-test build-manifest` — verify manifest file lands in cache dir at ~300KB,
   contains ~4000 entries.
2. Run a modified sample scenario that asserts on a texture historically in the 9.2%
   gap (e.g. `LooseSprites/Cursors` — it's in the D1.5 90.8% already but a portrait
   like `Portraits/Abigail` may be in the 9.2%). Verify the assertion now resolves.
3. Tamper: delete the manifest. Re-run. Verify the assertion falls to Tier 3 — event
   now has `content_hash` + `texture_size` populated, `texture_asset = null`.
4. Rewrite the scenario to assert on `content_hash` + `texture_size`. Verify it passes.

## Acceptance criteria

1. `./scripts/ci.sh` green at ~308 Passed + 38 Skipped.
2. `sdv-test build-manifest` launches SDV, produces a manifest, writes to cache dir,
   reports count + duration.
3. On harness startup with a valid manifest, Tier 2 resolves textures that Tier 1 missed
   — verified via a manually-crafted scenario.
4. On missing/corrupt manifest, harness logs the diagnostic + continues; Tier 2 no-ops;
   Tier 3 fallback emits `content_hash` + `texture_size`.
5. `DrawFilter.ContentHash` + `DrawFilter.TextureSize` match correctly (unit-verified).
6. `DrawEventDto` carries `content_hash` + `texture_size` on all events (new fields;
   backfilled for Tier 1 resolutions too).
7. `./scripts/run-samples.sh` still 11/11 PASS (no regression — new fields are additive,
   don't change existing assertion semantics).
8. `docs/roadmap.md` updated (move to Completed).
9. `docs/milestones/current.md` gains a Tier 2 completion subsection.

## Out of scope (M4 follow-ups)

- **Tier 2 manifest auto-regeneration** — today the manifest is manual. Auto-rebuild
  on SDV version change would require an install-version check + background rebuild.
- **Shipped manifest** — committing a pre-built manifest to the repo (would need
  maintenance every SDV release).
- **Streaming manifest-build progress** — pairs with MCP Tier 3 streaming tool-results.
  For now the build command prints a single summary line at the end.
- **Modded-content manifest entries** — out of scope. Modded textures route through
  Tier 1 (SMAPI hooks them at load time). Only vanilla-content pre-hook loads need
  Tier 2.
- **Full 64-char SHA-256 hash** in the wire format — 16-char prefix is enough for a
  manifest of ~5K entries (collision probability ≈ 2^-64 × 5000² / 2 ≈ 10^-12).
  If collision rate becomes a real concern, widen later.
- **Hash algorithm agility** — SHA-256 is hard-coded. If ever needed, could add an
  algorithm tag to the manifest and the DrawEventDto.

## Links

- Spec: `docs/spec.md` §4.2 "Texture → asset path resolution" (Tier 1 + 2 + 3)
- D1.5 completion: `docs/milestones/current.md` §"D1.5 — Texture path resolution landed"
- Conventions: `.claude/rules/draw-call-recorder.md`
- Brainstorm: 2026-04-24 auto-mode session (this doc)
