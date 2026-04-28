# Frobby Run-Suite Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a first-class `sdv-test run-suite` command that runs each discovered scenario in a fresh Stardew/SMAPI process while producing one shared HTML report hub.

**Architecture:** Keep `RunCommand` as the single-scenario executor. Add `RunSuiteCommand` as a thin orchestrator that discovers and validates `*.test.json` files, filters by scenario name, sorts paths deterministically, and calls `RunCommand.RunAsync` once per scenario with the same report base and pass-through mod/report flags. This preserves the proven per-scenario fresh-process behavior without duplicating SDV launch or report-generation code.

**Tech Stack:** .NET 10 runner CLI, existing `ScenarioLoader`, existing `RunCommand`, xUnit command tests, HTML report hub generation.

---

### Task 1: CLI Orchestrator

**Files:**
- Create: `src/Runner/Commands/RunSuiteCommand.cs`
- Modify: `src/Runner/Program.cs`
- Test: `tests/Runner.Tests/RunSuiteCommandTests.cs`

- [x] **Step 1: Write failing tests**

Cover deterministic discovery order, pass-through args, filter handling, and continuing after a child scenario failure.

- [x] **Step 2: Verify red**

Run: `dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --no-restore --filter FullyQualifiedName~RunSuiteCommandTests`

Expected: compile failure because `RunSuiteCommand` does not exist yet.

- [x] **Step 3: Implement command**

Add `RunSuiteCommand.RunAsync`, parse the narrow flag set needed by suite execution, and expose a `RunExecutor` test seam defaulting to `RunCommand.RunAsync`.

- [x] **Step 4: Verify green**

Run the focused `RunSuiteCommandTests` filter and then the full `Runner.Tests` project.

### Task 2: Docs And Starberg Flow

**Files:**
- Modify: `docs/spec.md`
- Modify: `/home/fintan/stardewRepos/stonks/docs/FROBBY.md`

- [x] **Step 1: Document command**

Add `run-suite` to CLI help/spec docs and replace Starberg's manual bash loop with the new command.

- [x] **Step 2: Verify live**

Run `sdv-test run-suite --fresh-process-per-scenario --extra-mod /home/fintan/stardewRepos/stonks/src/Starberg.Mod/bin/Release/net6.0 --report-dir /tmp/starberg-frobby-results-0.1.0 /home/fintan/stardewRepos/stonks/tests/sdv` and confirm all 25 scenarios pass.

### Task 3: Live Warp-Settle Followup

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `docs/rpc-schema.md`
- Test: `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [x] **Step 1: Capture failure**

Live `run-suite` exposed an intermittent first-scenario failure where `freeze.begin`
hit `Game1.isWarping` even after normal setup waits. The harness precondition is
correctly strict; the runner needs to treat that exact mid-warp response as transient
when `freeze.begin` is used as a scenario step.

- [x] **Step 2: Write failing test**

Added `FreezeBegin_RetriesTransientMidWarp`, which returns
`freeze.begin requires !Game1.isWarping (mid-warp)` once and then succeeds.

- [x] **Step 3: Implement retry**

Added `ScenarioRunner` retry behavior for only that exact `freeze.begin` mid-warp
error, with `args.settle_timeout_ms` and `args.poll_ms` overrides matching the
existing `time.next_day` pattern.
