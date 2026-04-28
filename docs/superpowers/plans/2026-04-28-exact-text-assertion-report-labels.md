# Exact Text Assertion Report Labels

> **For future compacted sessions:** This was added while building Starberg sell-lifecycle UI tests.

**Goal:** Keep Frobby HTML reports meaningful when scenarios assert exact rendered text with `filter.text_equals`.

**Why:** Starberg cash-cell assertions need exact matching for values such as `0.00 SBD`. Substring matching would treat `1,000.00 SBD` as a match for `0.00 SBD`, so the scenarios use `text_equals`. Before this change, Frobby rendered those assertions as `draw.text_contains "<text>"`, which made failure reports less useful.

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerTests.cs`

---

### Completed Work

- [x] Added a runner test covering a `draw.text_contains` assertion with `filter.text_equals`.
- [x] Updated assertion description logic to prefer `text_contains`, then `text_equals`, then `<text>`.
- [x] Verified the targeted runner test passes.

### Verification

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --no-restore --filter FullyQualifiedName~DrawTextAssertions_CallTextAssertRpcs
```

Full targeted test-project verification also passed:

```bash
for project in tests/Protocol.Tests/Protocol.Tests.csproj tests/Harness.Tests/Harness.Tests.csproj tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj tests/Runner.Tests/Runner.Tests.csproj; do dotnet test "$project" --configuration Debug --no-restore || exit $?; done
```
