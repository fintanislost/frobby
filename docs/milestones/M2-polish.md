# Milestone 2 — Production Polish

**Prerequisite:** M1 passes with green sample suite.

**Goal:** Framework is usable by modders other than the author. CI integration works. Visual regression fallback exists.

**Duration:** 2-3 weeks.

**Exit criteria:** A modder unfamiliar with the framework can, following docs alone, author a scenario for their own mod and get it running in CI in under an hour.

## Deliverables

### D2.1 — Bitmap fallback

- `bitmap.capture(region?)` RPC method per spec §4.5
- SSIM diff (use `System.Drawing` + a managed SSIM implementation; no native deps)
- `bitmap.diff(baseline, tolerance, method)` — methods: `ssim`, `pixel`, `dhash`
- Baselines stored in `tests/baselines/<scenario>/<name>.png`, diffs to `tests/diffs/`

### D2.2 — Record mode

- `[tool] record <scenario-name>` launches SDV in attached mode
- User plays manually; every manipulator-equivalent action is captured
- On exit, emits a scenario JSON stub the user can edit
- Stretch goal: record assertions by pressing a hotkey mid-play ("assert this frame")

### D2.3 — Watch mode

- `[tool] run --watch` re-runs matching scenarios on file change
- File watcher covers scenarios, fixtures, and the mod's source (if in a known location)
- Debounced, clears console between runs

### D2.4 — Additional reporters

- TAP 14 output for CI composition
- JUnit XML for GitHub Actions / GitLab visualization
- Optional: HTML reporter with screenshots on bitmap-fallback failures

### D2.5 — Fixture builder

- `[tool] fixture create <n>` launches SDV in capture mode
- On exit, copies save + metadata to `tests/fixtures/<n>.sav` + `<n>.meta.json`
- Metadata records: SDV version, SMAPI version, mod list, date, description

### D2.6 — GitHub Actions template

- `.github/workflows/test.yml` template in repo root
- Uses `[tool]/setup-sdv-test-env@v1` composite action (ship in same repo or separate)
- Sets up Xvfb, software rendering, required SMAPI version
- Caches SDV install between runs

### D2.7 — Proxmox self-hosted runner notes

- `docs/ci-self-hosted.md` covering GPU-passthrough VM setup (Finn-specific but reusable)
- Dockerfile for the runner environment
- Notes on determinism differences between Mesa llvmpipe and real GPU

### D2.8 — Documentation site

- Initial version — can be markdown-rendered GitHub Pages, doesn't need SvelteKit yet
- Covers: install, first scenario, assertion reference, CI setup, troubleshooting
- Example scenarios linked from docs

## Non-goals

- MCP server (M3)
- C# fluent DSL (M3)
- Multi-mod scenarios (post-M3)
- Multiplayer support (out of scope)

## Risks

- **SSIM implementation choice.** Managed SSIM libraries on NuGet vary in quality. Evaluate 2-3 before committing; the one we pick must handle 16-bit color and configurable window sizes.
- **Record mode UX.** Capturing "intent" from free-form play is hard. If it doesn't work cleanly, ship a simpler "checkpoint" mode where the user manually marks significant moments.
- **CI environment drift.** SMAPI and SDV versions bundled into CI actions get stale. Version-pin explicitly; add a "CI versions" note to doctor output.
