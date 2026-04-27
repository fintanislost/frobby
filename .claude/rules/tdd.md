# TDD Discipline

This project uses test-driven development. Superpowers enforces red-green-refactor; follow it.

## The rule

**No implementation code without a failing test first.** This applies to:

- Runner CLI logic (xUnit tests in `tests/Runner.Tests/`)
- JSON-RPC protocol handlers (contract tests)
- State inspector/manipulator API surfaces (integration tests against a fixture save)
- Scenario executor (scenario-level tests with known expected outputs)

## Exceptions

- **Harmony patches themselves** — these are tested via integration tests against a running SDV instance, not unit tests. The test is: "patched method produces expected draw-call stream for known scenario." Write the integration test first.
- **Exploratory spikes** — code in `docs/spikes/*/` is exempt. Once promoted to `src/`, tests are required.
- **Content Patcher JSON schemas** — validated by schema tools, not xUnit tests.

## Test naming

`MethodUnderTest_Scenario_ExpectedOutcome` — e.g., `WarpPlayer_ToUnknownLocation_ReturnsErrorResult`.

Integration tests prefixed `Integration_`. Slow tests (>1s) tagged `[Trait("Category", "Slow")]`.

## Red-green-refactor

Red phase: write the test, run it, see it fail for the expected reason (not a compile error, not a typo). If it passes immediately, the test is wrong — make it fail first.

Green phase: minimum code to pass. Resist adding adjacent features.

Refactor phase: with green tests as safety net, clean up. Extract helpers, rename, dedupe.

## Coverage expectations

Not a percentage target. Instead: every public API of the runner and harness RPC surface has at least one success-path and one failure-path test. Error messages are tested — they're user-facing.
