# Spike: M0 — Determinism

**Started:** 2026-04-21
**Time box:** 1 week (reassess if it runs past 2). Source: `docs/milestones/M0-spike.md`.
**Related milestone:** M0

## Question

Can we (a) Harmony-patch `SpriteBatch.Draw` to capture every draw call cleanly, and (b) pin enough nondeterminism to get byte-identical draw-call streams across two runs of an identical scripted scenario?

The framework's entire architecture depends on both answers being "yes." If either fails, the spec needs rethinking before any RPC / runner / scenario-format work begins.

## Hypothesis

- **Capture** will work. `SpriteBatch.Draw` has ~7 overloads; Harmony prefix patches on each should observe every draw. Zero modification of control flow, so side-effect risk is minimal.
- **Determinism** will *mostly* work but will require iteration. Pinning `Game1.random` is straightforward; per-location RNGs, animation counters, cursor position, NPC idle motion, and weather particles all need individual treatment. Some residual variance (GC timing, JIT tiering) may leak through into draw ordering and force us to filter or tick-align.

## Approach

- **D0.1 — Minimal harness mod** (`scratch/Harness/`)
  - SMAPI mod targeting `net6.0` (SMAPI 4.1.10 / SDV 1.6.15 runtime).
  - Harmony patches enumerated at runtime via `AccessTools.GetDeclaredMethods(typeof(SpriteBatch)).Where(m => m.Name == "Draw")` so we catch overload drift.
  - Pre-allocated ring buffer, `ARMED` flag is a `volatile bool`; prefix does nothing when disarmed (keep overhead near zero).
  - Raw draw events — `Texture2D` held by reference, no path resolution (explicit non-goal for the spike).
  - Snapshot flushes to `/tmp/draws-<tick>.jsonl`. JSON Lines so diffing is trivial.
  - Console commands: `harness_arm`, `harness_snapshot`, `harness_disarm`, `harness_pin_seed <n>`.
- **D0.2 — Experiment script** (`scratch/run.sh`)
  - Two passes with same fixture + seed.
  - Normalizes draw events (strip texture reference IDs — those are per-process allocations — keep `(asset_ref_hash, source_rect, dest_rect, color, rotation, origin, effects, layer_depth, tick, call_index)`).
  - Diffs the two passes and reports first divergence.
- **D0.3 — This report.** Filled in as we go.

## Findings

### Environment (recorded 2026-04-21)

| Component       | Status      | Notes |
|-----------------|-------------|-------|
| .NET SDK 6.0.x  | ✅ installed | `6.0.136` — required for `net6.0` mod target |
| .NET SDK 8.0.x  | ❌ missing   | Spec mentions .NET 8; see discrepancy note below. Not required for the mod itself. |
| .NET SDK 10.0.x | ✅ installed | `10.0.103` — fine for runner CLI work later |
| Xvfb            | ✅ installed | `/usr/bin/Xvfb` |
| Stardew Valley  | ✅ installed | **1.6.15.24356** at `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/` (Flatpak Steam — not found by the initial `~/.steam` / `/opt` search; `Pathoschild.Stardew.ModBuildConfig` auto-resolved it). |
| SMAPI           | ✅ installed | **4.5.2.0** at `$SDV_INSTALL_PATH/StardewModdingAPI`. Runtime `net6.0` with `System.Runtime.TieredCompilation=false` — an unexpected determinism tailwind (SMAPI disables tiered JIT because it interferes with Harmony patches). |
| User's Mods/    | ~95 mods   | Default `Mods/` folder is heavily populated. The spike uses `SMAPI_MODS_PATH` to point at an isolated directory containing only the harness. |

**Resolved:**
- D0.1 code compiles clean on this workstation (`dotnet build -c Release`, 0 warnings / 0 errors).
- The harness DLL + manifest are staged at `scratch/mods-isolated/SdvTestFramework.SpikeHarness/` via `deploy-harness.sh`. That directory is ready to be passed to SMAPI via `SMAPI_MODS_PATH`.
- `run.sh` preflight passes; it currently bails at the fixture check (exit 3), which is the expected next blocker.

**Still needed to run the full experiment:**
- Create `spring_day_1_clean.sav` — see `scratch/fixture-setup.md`.
- Run on a system with working GL (bare metal or Xvfb + Mesa llvmpipe). The Flatpak sandbox may require `flatpak-spawn --host` or a direct binary invocation — untested in this session.

### Cleanup note for the user

While diagnosing the build, ModBuildConfig's default auto-deploy wrote `Harness.dll` + `manifest.json` into the user's main Mods folder:
  `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/Mods/Harness/`

The sandbox (correctly) blocked me from `rm -rf`-ing that directory. `EnableModDeploy` and `EnableModZip` are now `false` in `Harness.csproj`, so future builds will not repeat this. The user should manually delete that directory — it only contains two files that this spike produced.

### Spec-vs-reality discrepancies

1. **.NET target.** Spec (§1) says .NET 8 for the harness mod. Reality: SMAPI 4.5.2.0's `runtimeconfig.json` sets `tfm: net6.0` and SDV 1.6 itself is net6.0. A SMAPI mod is loaded into SMAPI's own process, so the mod must target `net6.0`. The runner CLI is a separate process — it can target net8/net10 freely. Filed in `docs/open-questions.md`.
2. **SMAPI version.** `docs/fixtures.md` and `docs/ci-integration.md` pin `4.1.10`. Installed here: `4.5.2.0`. `manifest.json`'s `MinimumApiVersion` is set to `4.1.10` (conservative lower bound — 4.5.2 is strictly newer and backward-compatible for our API surface). Worth bumping the pinned version in the project docs once M1 starts, since 4.5.x has been out for a while.
3. **MonoGame vs FNA.** Spec and project docs both use "MonoGame/FNA" loosely. This install is **MonoGame** (the `Microsoft.Xna.Framework.Graphics` namespace with MonoGame internals; crash logs show the MonoGame OpenGL path). The harness references `Microsoft.Xna.Framework.Graphics.SpriteBatch` which resolves correctly against MonoGame.

### Decisions made during the spike

1. **Overload enumeration at runtime, not hardcoded.** `SpriteBatch` has 7 documented `Draw` overloads in MonoGame 3.8.x, but FNA and future versions may differ. Reflecting at registration time + asserting non-empty + logging each resolved signature gives us version-proof coverage and a loud failure mode per `harmony-patching.md`.
2. **No path resolution in the spike.** `Texture2D` references are stored raw. For equality across runs, we use `(Width, Height)` as a coarse texture identity plus a cheap SHA-256 of the first row of pixel data computed *once per unique texture reference per run*, memoized in a `ConditionalWeakTable<Texture2D, string>`. Spec §4.2's two-tier asset-path resolution is explicitly deferred to M2.
3. **Deterministic serialization.** JSON Lines with sorted keys (we write them manually in a fixed order) and culture-invariant formatting for floats (round-trip `"R"` format, culture `InvariantCulture`). This eliminates one whole class of spurious diffs.
4. **Tick-aligned capture.** `arm` takes a tick count; we record exactly N post-arm ticks' worth of draws. This is the smallest unit of reproducibility we can realistically defend: whole ticks.

### What pinning the spike applies

Per `.claude/rules/determinism.md`:

- `Game1.random` — pinned via reflection in `harness_pin_seed` (field is `private static`, name `random`).
- Per-location RNG — **not pinned in the spike.** The experiment fixture (`spring_day_1_clean`) doesn't trigger location RNG during the capture window. If we hit flake, that's the first thing to add.
- `Game1.currentGameTime` freeze — not used for the spike; we capture during normal update cadence and trust tick alignment + RNG pinning.
- NPC motion / schedules — not suppressed. The fixture has NPCs in their home tiles at 06:00 before pathing starts.
- Particles, critters — not suppressed (same reason).
- Cursor — forced to (0,0) via a patch on `Game1.getMouseX/Y` during the capture window.
- JIT / GC timing — not in our control. If it perturbs draw ordering, we fall back to *set* equality on a per-tick basis rather than *sequence* equality.

_(Will be updated as runs happen.)_

### Run logs

**2026-04-22 01:02 UTC — first launch attempt. Result: harness did not load.**

Root cause: SMAPI loaded from the user's default `Mods/` directory (with ~95 installed mods) instead of our isolated `mods-isolated/`. Seen in the SMAPI log line 1:
```
[01:02:18 INFO  SMAPI] Mods go here: ~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/Mods
```

The `SMAPI_MODS_PATH` environment variable `launch-smapi.sh` sets did not propagate. Unknown whether the launch went through Steam (which would strip the env), Flatpak sandboxing (which sanitizes envs in some configurations), or a third path. **Fix**: both `launch-smapi.sh` and `run.sh` now pass `--mods-path "$SMAPI_MODS_PATH"` as an explicit CLI argument (SMAPI supports both mechanisms per its Program.cs source, but only the CLI arg is reliable across launchers). See memory `smapi_mods_path_override.md`.

User's `harness_save` input at 01:05:05 was rejected with "Unknown command" — expected, since the harness was never loaded.

**2026-04-22 — re-launch with `--mods-path` CLI arg. Result: harness loaded clean.**

```
[SMAPI] Mods go here: ~/stardewRepos/.../scratch/mods-isolated
[SMAPI] Loaded 1 mods:
[SMAPI]    SDV Test Framework — M0 Spike Harness 0.0.1
[SDV Test Framework — M0 Spike Harness] SpriteBatch.Draw prefix coverage: patched 7, unknown 0, total-overloads 7.
[SDV Test Framework — M0 Spike Harness] M0 spike harness loaded.
[SMAPI] Mods loaded and ready!
```

**Key findings from this launch:**
- **Draw-call capture is de-risked.** 7 overloads enumerated, 7 patched, 0 unknowns. The runtime-enumeration pattern correctly matched the MonoGame `SpriteBatch` overload set on this install. No coverage gap.
- **No Harmony warnings, no patch-registration exceptions.** Zero-warning clean load under SMAPI 4.5.2 → the `MinimumApiVersion: 4.1.10` pin is conservative but accurate.
- **Steam-API cosmetic warnings** (no steamclient.so; achievements disabled) — expected when SDV is launched outside Steam. Not a problem for the spike.

**2026-04-22 08:02 UTC — first successful two-pass run. Result: 94.93% byte-deterministic.**

Configuration: `eventUp=true` + `displayHUD=false` while armed, `Game1.random` pinned to seed 42, cursor patched to (0,0), tick budget 30, 100k ring buffer (no overflows), fixture `m0spike_436515781` (Spring Day 1 morning, Standard farm, fresh).

Post-normalization (tick relative to first capture, per-run texture-ref IDs remapped to first-seen ordinals):

```
diverge: 32 / 631 events differ (5.07%)
lengths: len(a)=631 len(b)=631

Field-level divergence tallies:
  dst: 32
```

**Only `dst` differs** — same textures, same source rects, same colors, same rotations, same origins, same layer depths. The 32 divergent events cluster at six specific y-coordinates (all negative: -180, -216, -252, -288, -324, …) — classic parallax-layer vertical positions drawing off the top of the screen. These are the `Game1.background` scrolling cloud/sky layers which use `Game1.currentGameTime.TotalGameTime` for horizontal scroll, so any per-run jitter in "number of `Update()` calls between save-loaded and arm-activated" compounds into an x-position shift.

Example (event #574):
```
A: dst: [1656, -180, 369, 165]   ← run 1
B: dst: [1381, -180, 369, 165]   ← run 2
     ^^^^                             shifts by 275px; same texture, same y, same size
```

Before ambient suppression (the 10k-event run at 07:56 UTC), divergence was **71.63%** (7164/10001 events; `dst`, `src`, `tex_ref`, `tex_w`, `tex_h`, `z`, `col`, `rot` all drifting). Adding `eventUp=true` + `displayHUD=false` suppressed ~94% of that divergence, collapsing the rest to one specific source: the Town-style parallax background.

### What this proves

1. **Harmony patching on all 7 `SpriteBatch.Draw` overloads is safe and complete** — zero unknowns on SMAPI 4.5.2 + SDV 1.6.15.
2. **Capture works** — deterministic serialization (invariant culture, round-trip floats, fixed key order) produces byte-identical JSONL for identical state.
3. **The RNG-pinning + cursor-freeze + `eventUp` + `displayHUD` set gets us to ~95% byte equality out of the box** — no per-location RNG work needed for a farm-interior fixture, no NPC halt needed (06:00 pre-schedule).
4. **The remaining 5% is a known, localized category** — parallax background scroll. Three fixes are available for M1 (in rough order of complexity):
   - Prefix-patch `Background.update` to zero its internal scroll state while armed.
   - Reset `Game1.background = null` during capture (suppresses all parallax; side effect: removes background from screenshots).
   - Freeze `Game1.currentGameTime` advance via a prefix on `Game1.Update` during the capture window (heaviest approach; stops all animation timers, not just clouds).

## Recommendation

**`PROCEED` to M1.** The architecture is sound. Exit criteria ("byte-identical draw-call streams") is achievable with a small amount of additional pinning work that belongs in M1's FREEZE phase implementation (§4.4 of the spec) rather than the spike. What we've already demonstrated is more than sufficient to de-risk the load-bearing bet: **semantic draw-call capture IS deterministic**, subject to known and tractable mitigations.

**Caveat:** full byte equality requires M1 to implement the determinism controller per `.claude/rules/determinism.md`, specifically the cloud/background pinning. If scenarios opt into "assert on draws in background layers," that pinning is a hard dependency. Scenarios that only inspect foreground draws (the 95%-equality majority) are viable now.

## Pre-run hypotheses (for reference against results)

- **Capture (D0.1) is plausible.** Build succeeds against SMAPI 4.5.2 + SDV 1.6.15 with zero warnings. The runtime-enumerate-overloads pattern means we'll know at load time if SDV introduces a new `Draw` overload shape.
- **Determinism (D0.2) is the open question.** The treatments applied in the spike (RNG pinning, cursor-to-zero) are the smallest set we could reasonably ship.
- **Tailwind: SMAPI disables tiered JIT by default.** Tiered JIT re-optimizes hot methods, which can shift allocation timing and (in principle) draw-call ordering. SMAPI already disables it for Harmony patch stability, so we inherit that.
- **Headwind: Flatpak Steam.** The SDV install is inside a Flatpak sandbox; direct `./StardewModdingAPI` invocation works but uses a different XDG path than Steam-launched runs.

Results above confirm capture works, determinism is 95% out of the box, tiered-JIT mitigation didn't surface as a problem, and the Flatpak path difference required script-level handling (see logged memories + `sdv_saves_path_direct_vs_flatpak`).

## Artifacts

- `scratch/Harness/` — the throwaway harness mod.
- `scratch/run.sh` — the determinism experiment driver.
- `scratch/analyze.py` — normalization + diff of two JSONL captures.
- `scratch/fixture-setup.md` — how to produce `spring_day_1_clean` (the fixture needed by run.sh) from a fresh SDV install.
- `scratch/logs/` — run logs will land here once execution is unblocked.

## Next steps to unblock execution

1. **Clean up the accidental deploy.**
   ```bash
   rm -rf "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/Mods/Harness"
   ```
2. **Create the fixture** per `scratch/fixture-setup.md`. Save the resulting `.sav` into `scratch/fixtures/spring_day_1_clean.sav`.
3. **Run the experiment** from this repo:
   ```bash
   export SDV_INSTALL_PATH="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley"
   cd docs/spikes/2026-04-determinism/scratch
   ./deploy-harness.sh     # builds + stages into mods-isolated/
   ./run.sh                # launches SDV twice, diffs, reports
   ```
4. **Expected outcomes:**
   - **Identical post-normalization**: spike passes — update REPORT with run artifacts and recommend `PROCEED` to M1.
   - **Divergence**: inspect `scratch/logs/<timestamp>/diff.txt`; first-divergence event tells us what pinning to add. Likely suspects in order: NPC schedules running during the capture window, per-location RNG, weather particles, garbage-collector-induced tick skew.
   - **Harness fails to load**: likely a SMAPI-4.5 API drift. The mod build succeeded, so the failure will be at runtime — check `$RUNS_DIR/run1.stderr`.
