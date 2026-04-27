# SDV Test Framework — Roadmap

**North-star goal:** Make sdv-test a first-class tool for **LLM-driven mod testing**.
Specifically:

- Claude Code (or similar) can author + run mod tests during development via the MCP server.
- The framework is installable as a NuGet package in a mod's repo (no source-tree vendoring).
- Example suites show Claude how to test typical mod patterns, so it has templates to follow
  when scaffolding new tests.
- Debugging output (draw-event diffs, SSIM scores, diff images) is machine-parseable so an
  LLM can reason about failures and iterate.

Non-goals for now: Windows-primary support, multi-user cloud MCP, FluentAssertions
integration. These sit in Tier 4.

## How to use this doc

**At session start** (see CLAUDE.md): skim this file to see what's in-flight and near-term.

**When an item ships:** move it from its tier to [Completed](#completed) with the date +
one-line summary. Be specific about what landed (commit, test count delta, key files).

**When a new TODO surfaces** (code-review fallout, smoke-test bug, spec gap, user report):
add it to the appropriate tier. Reference the source — spec section, plan doc, or
conversation — so future readers know where it came from.

**Tiers are priority, not order.** Items within a tier are roughly equivalent;
sequencing within a tier is a judgment call when picking the next item.

---

## Tier 1 — LLM-workflow enablers

Items that directly unblock "Claude drives test authoring on real mods." Do these first.

_(All Tier 1 items shipped 2026-04-24. Next: pick from Tier 2 ecosystem work — NuGet
packaging, docs site, example mod suites — or Tier 3 polish.)_


## Tier 2 — Distribution + ecosystem (spec §7.3 completion)

Makes the framework actually usable by other modders + Claude-via-MCP on arbitrary repos.

- [ ] **Publish 0.1.0 to nuget.org** (~2 hours once a real user has smoke-tested).
  Requires NuGet API key + decision about who maintains. The `scripts/pack.sh`
  flow is ready; just needs `dotnet nuget push *.nupkg --source nuget.org --api-key X`.
  Source: NuGet packaging out-of-scope.

- [ ] **GitHub Actions release workflow** (~half-day). Automate `dotnet pack` +
  `dotnet nuget push` on `git tag v0.1.x`. Source: NuGet packaging out-of-scope.

- [ ] **Documentation site** (~2-3 weeks) — SvelteKit. Landing page + quickstart +
  scenario cookbook + API reference + MCP usage guide + examples. Claude reads docs to
  generate correct tool calls, so doc quality compounds LLM ergonomics.
  Originally spec'd as M3 subproject 4.

- [ ] **Example suites for 1-2 real mods** (~1 week each) — pick small popular
  community mods, write real test suites against them, commit to the repo. Serve as
  templates Claude can copy from. Originally spec'd as M3 subproject 5 (3-5 mods
  targeted; start with 1-2 to validate the workflow). Candidates: a small UI mod
  (for draw-call assertions), a small dialogue-patcher mod (for state assertions).

## Tier 3 — Debugging polish

Makes test failures actionable — especially important when Claude is iterating on a
broken test.

- [ ] **Full DSL assertion eval in MCP `run_scenario`** (~1 day) — today handles
  steps + `draw.contains` assertions; delegates state-DSL to the CLI runner. Extend to
  evaluate `state` assertions (reuse `ScenarioRunner`'s logic or refactor into a shared
  evaluator). Source: M3-MCP out-of-scope list.

- [ ] **MCP streaming tool results** (~2 days) — incremental updates for long-running
  scenarios via MCP. Today `run_scenario` is synchronous; LLM sees nothing until
  completion. Streaming lets Claude watch step-by-step. Source: M3-MCP out-of-scope.

- [ ] **Consolidate ScenarioLoader's physical home** (~2 hours) — today `ScenarioLoader.cs`
  lives in `src/Runner.Mcp/` with namespace `SdvTestFramework.Protocol.Scenarios`. It was
  moved out of Protocol during Tier 2 smoke because `JsonSchema.Net 7.x` uses net8-only
  `System.Text.Json` APIs, and the net6 Harness transitively loaded Protocol → SMAPI
  rejected the mod. Cleaner fix: pin `JsonSchema.Net` to a net6-compat version (v6.x
  supports netstandard2.0 + net6.0), restore `ScenarioLoader.cs` to `src/Protocol/Scenarios/`.
  Source: Tier 2 T5 smoke side-fix.

## Tier 4 — Nice-to-haves / paper cuts

Not important for the LLM-workflow goal but worth logging so they don't get lost.

- [ ] Watch mode keyboard shortcuts (`r`/`q`/`a`) (~1 day). Source: M2-watch out-of-scope.
- [ ] FluentAssertions `.Should()` integration (~2 days). Source: M3-DSL out-of-scope.
  Low priority — Claude doesn't care about `.Should()` ergonomics.
- [ ] Generic menu registry `Wait.ForMenu<ShopMenu>` (~2 days). Source: M3-DSL out-of-scope.
- [ ] Combined `[ScenarioFact]` attribute (~1 day) — `[Fact]` + `[Scenario]` in one,
  via a custom xUnit discoverer. Source: M3-DSL out-of-scope.
- [ ] Parallel SDV-subprocess across multiple xUnit collections (~1 week).
  Source: M3-DSL out-of-scope.
- [ ] MCP HTTP transport (~2 days). Source: M3-MCP out-of-scope.
  Low priority for local Claude Code; higher if cloud-hosted MCP matters.
- [ ] MCP resources + prompts (~1 week). Source: M3-MCP out-of-scope.
- [ ] Windows build parity (~1 week).
- [ ] Git LFS setup for baselines + fixtures (few hours, once >5 items in either).
  Source: M2-fixture-builder + M2-bitmap out-of-scope.
- [ ] StageHarnessPayload generic NuGet-dep staging (~2 hours). Today hardcodes
  `SixLabors.ImageSharp.dll`; future runtime NuGet deps need a generic pattern.
  Source: M2-bitmap T7 build-fix commentary.
- [ ] Expose `ScenarioRunner` state-DSL evaluator as a reusable library (folds into
  "Full DSL assertion eval in MCP `run_scenario`" Tier 3 item).
- [ ] Suppress `Test Run Aborted` cosmetic noise in `ci.sh` (~30 min). `dotnet test
  sdv-test-framework.slnx` tries to run `Runner.Dsl` as a test host (because it
  references `xunit` for `BeforeAfterTestAttribute`), aborts harmlessly, then continues
  to the actual test projects. Fix options: add `<IsTestProject>false</IsTestProject>`
  to `Runner.Dsl.csproj`, or filter `ci.sh` to only Microsoft.NET.Test.Sdk-having projects.
  Source: surfaced by World.InteractNpc Phase A subagent.

---

## Completed

### 2026-04-26

- **Bitmap completion bundle**. Four Tier 3 items shipped together:
  - **Pixel-exact + dHash methods** (completes spec §4.5). `bitmap` assertion gains `method`
    field; per-method `tolerance` semantics polymorphic.
  - **Three-tier tolerance preset** (`generic`/`ci-ubuntu`/`self-hosted-nvidia` per
    `.claude/rules/ci-integration.md`). `sdv-test run --tier=<name>` selects per-method
    defaults via `TierTolerance.Resolve`.
  - **`sdv-test baselines` subcommand** (`list`/`update`/`show`/`delete`). Replaces the
    `--update-baselines` static-field hack via a `RunCommandOptions` record refactor +
    swappable `RunExecutor` delegate.
  - **Capture-cache cleanup**. Auto-sweeps `~/.cache/sdv-test-framework/captures/` at end of
    every `sdv-test run` (--no-cache-cleanup to opt out). Manual `sdv-test cache clean`
    with `--max-age` / `--keep-runs` / `--dry-run`.
  368+47 → 388+48.

### 2026-04-25

- **Diff-image-on-failure**. Bitmap assertion failures (when not `--update-baselines`) now
  write `baseline.png` + `capture.png` + `diff.png` (bilinear-smoothed heatmap) into
  `<run-dir>/scenarios/<scenario>/diffs/assertion-NN-bitmap/`. Optional `triptych.png`
  composite via `--diff-format=triptych` or per-assertion override. Surfaced in HTML
  report's "Failure forensics" section. Pairs with HTML run reports for one-glance LLM-
  driven debugging. 357+46 → 368+47.

### 2026-04-24 (even later)

- **HTML run reports**. Per-run directory with `index.html` + `summary.json` +
  per-scenario detail pages + screenshot evidence (CLI path). Auto-capture at
  `freeze.begin` + assertion failures + explicit `Screenshot.Capture(name)` DSL method.
  Integrates with `sdv-test run` (CLI), `dotnet test` (SdvFixture, summary.json only —
  HTML is CLI-only), and MCP `run_scenario` (returns `report_dir` for Claude). Pre-T5
  fixup moved pure report types to Protocol so Runner.Dsl + Runner.Mcp can use them
  without dragging Runner. 347+45 → 357+46.

- **NuGet packaging**. Three packages produced by `scripts/pack.sh`:
  `SdvTestFramework.Protocol` (transitive), `SdvTestFramework.Runner.Dsl` (library),
  `SdvTestFramework.Cli` (`dotnet tool` bundling CLI + MCP server + embedded harness
  payload). Free architecture win: dropped Runner.Dsl's stale Runner project ref.
  Local-install smoke verified end-to-end. 347+44 → 347+45.

- **Action-trace recording**. Third record-mode flow (after M2's state-snapshot + RPC-trace).
  `harness_record_actions <name>` + `harness_record_stop` SMAPI console commands capture
  warps, NPC interactions, and time-advance via SMAPI's high-level events. Pure-function
  `ActionTraceTranslator` applies multi-warp coalesce + NPC inference + time-debounce
  heuristics. Output is plain scenario JSON, replayable via `sdv-test run`. 337+43 → 347+44.

- **`world.interact_npc` + `time.set` RPCs**. Two new harness handlers + 2 DTOs + DSL
  facet methods (`World.InteractNpc(name)`, `Time.Set(time?, day?, season?, year?)`). MCP
  `shop_purchase` and `npc_interaction` scaffold templates upgraded to include the actual
  `world.interact_npc` step (no longer placeholder). `WorldInteractNpcHandler` finds an NPC
  in `Game1.currentLocation.characters` by name and calls `NPC.checkAction`; `TimeSetHandler`
  writes `Game1.timeOfDay/dayOfMonth/year/season` directly with upfront param validation
  (at-least-one, HHMM range, day 1-28, year ≥ 1, season enum). Both registered in `ModEntry`;
  startup log updated. Test count: 321+38 → **337 Passed + 43 Skipped** (+16 Passed, +5 Skipped
  — Phase A added 14 handler unit tests, Phase B added 2 DSL tests). Source: M3-DSL out-of-scope list.

### 2026-04-24 (later)

- **Richer `scaffold_scenario` templates** in the MCP server. Added 4 templates:
  `npc_interaction`, `shop_purchase`, `tool_use`, `inventory_check`. Refactored the
  template builder to return both steps + assertions (so `inventory_check` can ship
  its 2 state assertions). Test count: 317+38 → 321+38 (+4 template tests).

- **Tier 2 texture-hash fallback**. Closes the 9.2% D1.5 gap via build-on-user-machine
  SHA-256 manifest. New `sdv-test build-manifest` command drives a new
  `diagnostic.build_texture_manifest` harness RPC; 3-tier cascade (weak map →
  hash+manifest → anonymous). New `DrawEvent` + `DrawEventDto` + `DrawFilter` fields
  `ContentHash` + `TextureSize`. 298+37 → 317+38.
  Side fix: moved `ScenarioLoader` out of `Protocol` (SMAPI harness incompatibility with
  `JsonSchema.Net 7.x` net8.0-only APIs) into `Runner.Mcp` (duplicated copy pattern).
  Plan: `docs/superpowers/plans/2026-04-24-tier2-texture-hash.md`.

- **`.slnx` + `ci.sh` self-sufficiency**. Added the 4 missing projects (Runner.Dsl,
  Runner.Mcp, and both test projects) to `sdv-test-framework.slnx`; rewrote `ci.sh` to
  use the solution for build + test (single `dotnet build sln` / `dotnet test sln`
  instead of per-project globbing). Caught a real drift bug along the way:
  `SdvFixture.cs` still referenced `SdvTestFramework.Runner.{HarnessDeployer,SdvLauncher}`
  (the pre-MCP-T6 namespace) — fixed to `SdvTestFramework.Protocol.*`. CI still
  298 Passed + 37 Skipped; newly catches future drift.

### 2026-04-24

- **M3 subproject 2 — MCP server** (7 tasks, subagent-driven). `sdv-test mcp` stdio
  server with 6 curated tools + `rpc_call` passthrough. Side fixup in T6: resolved
  Runner ↔ Runner.Mcp cycle by moving `SdvLauncher` + `HarnessDeployer` + `ScenarioLoader`
  to Protocol. 286+36 → **298 Passed + 37 Skipped**.
  Plan: `docs/superpowers/plans/2026-04-24-m3-mcp-server.md`.
- **M3 subproject 1 — C# fluent DSL** (9 tasks, subagent-driven). New
  `src/Runner.Dsl/` with 9 ambient static facets + `[Scenario]` attribute +
  `SdvFixture` collection fixture. 266+34 → **286 Passed + 36 Skipped**.
  Plan: `docs/superpowers/plans/2026-04-24-m3-csharp-dsl.md`.
- **M3 subproject 0 — SIGTERM handler** (M2 followup). `PosixSignalRegistration` for
  SIGTERM + SIGINT in `src/Runner/Program.cs`. 264+34 → **266 Passed + 34 Skipped**.

### 2026-04-24 (M2 completion)

- **M2 subproject 5 — Bitmap fallback** (7 tasks). Hand-rolled SSIM + `bitmap.capture`
  RPC + `--update-baselines`. 253+33 → 264+34.
- **M2 subproject 4 — Record mode** (5 tasks). `harness_record` + `sdv-test record`.
  246+32 → 253+33.

### 2026-04-23

- **M2 subprojects 1-3** — Fixture builder (+28 passed), TAP/JUnit reporters (+11),
  Watch mode (+6).
- **M1 — Core framework** — D1.1 through D1.7. JSON-RPC + runner CLI + harness + RPC
  surface + determinism controller + 10-scenario sample suite. 201+26 at D1.7 ship.

### 2026-04-22

- **M0 spike — Determinism proof** — 94.93% byte-identical across runs.
  `PROCEED` recommendation to M1. Spike report: `docs/spikes/2026-04-determinism/REPORT.md`.

---

## Sources of new items

When adding to the roadmap, prefer attribution:

- **Code-review fallout** — a `DONE_WITH_CONCERNS` status, a "defer to M3" reviewer
  note, or a `// TODO:` comment → add to the appropriate tier with the commit/file ref.
- **Smoke-test bugs** — bugs surfaced during manual smoke (record mode Ctrl-C flush,
  watch mode SIGTERM) → usually Tier 1 if it blocks a workflow, Tier 3 if it's polish.
- **Spec gaps** — sections of `docs/spec.md` not yet implemented → reference the §.
- **User-visible feedback** — if someone (including the user themselves) files an issue
  → include the source note.

Avoid vague entries like "improve tests" or "refactor X" without justification.
Every item should be actionable + specific about its value.
