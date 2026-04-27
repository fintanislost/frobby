# HTML Run Reports — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green**. T7's extra gates:
> - `./scripts/run-samples.sh` produces a `./test-results/<run-id>/` directory; `index.html` shows 11/11 green; bitmap scenario has at least one screenshot.
> - Tampered failing scenario produces a red `index.html` + an `assertion-fail.png` in screenshots.
> - `dotnet test tests/Runner.Dsl.Tests/` produces a `./test-results/<run-id>/` from the SdvFixture path (manually verified via the worked sample if SDV available; unit-tested via shim otherwise).
> - MCP `run_scenario` tool result includes `report_dir` + `report_index` paths.

**Goal:** Per-run directory containing `index.html` + `summary.json` + screenshot evidence. Auto-capture at `freeze.begin` + on assertion failure + explicit user-driven via `screenshot.capture` step / `Screenshot.Capture(name)` DSL method. Works for `sdv-test run`, `dotnet test` via DSL, and MCP `run_scenario`.

**Architecture:** Runner-side orchestration. Harness's `bitmap.capture` RPC unchanged — runner copies output bytes from cache into the per-scenario report dir. Pure-function HTML generator with embedded CSS, no JS framework. Static file artifacts portable as CI downloads.

**Tech Stack:**
- No new NuGet dependencies. `System.Text.Json` for `summary.json`. Hand-rolled HTML via `StringBuilder` + embedded CSS template constant.
- Runner.Dsl + Runner.Mcp gain new public API surface; existing tests must continue passing.

**Design spec:** `docs/superpowers/specs/2026-04-24-html-run-reports-design.md`

---

## File structure

**New (Runner):**
- `src/Runner/Reports/RunDirectory.cs` — typed wrapper over the run-dir + subdirs.
- `src/Runner/Reports/RunSummary.cs` — DTO records (RunSummary, ScenarioOutcome, StepOutcome, AssertionOutcome).
- `src/Runner/Reports/ScreenshotRecorder.cs` — Runner-side capture orchestrator.
- `src/Runner/Reports/HtmlReportGenerator.cs` — pure function, generates index.html + per-scenario report.html + summary.json + steps.json + assets/styles.css.

**New (Runner.Dsl):**
- `src/Runner.Dsl/Screenshot.cs` — `Screenshot.Capture(name)` facet method.

**New tests:**
- `tests/Runner.Tests/Reports/RunDirectoryTests.cs` — 2 tests.
- `tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs` — 4 tests.
- `tests/Runner.Tests/Reports/ScreenshotRecorderTests.cs` — 2 tests.
- `tests/Runner.Dsl.Tests/Facets/ScreenshotTests.cs` — 1 test.
- `tests/Runner.Mcp.Tests/Tools/RunScenarioReportDirTests.cs` — 1 test.
- `tests/Runner.Tests/Reports/RunReportIntegrationTests.cs` — 1 skipped placeholder.

**Modified:**
- `src/Runner/Commands/RunCommand.cs` — `--report-dir` + `--no-report` flags; pre-create RunDirectory; pass to ScenarioRunner; HTML generation at end.
- `src/Runner/Scenarios/ScenarioRunner.cs` — new constructor param `RunDirectory? reportDir`; auto-capture hooks; `screenshot.capture` step case.
- `src/Runner/Scenarios/ScenarioReport.cs` — extend with `Screenshots: List<string>` + `Steps: List<StepOutcome>`.
- `src/Runner.Dsl/SdvTestSession.cs` — `ReportDir: RunDirectory?` property.
- `src/Runner.Dsl/SdvFixture.cs` — create RunDirectory in InitializeAsync, write HTML at DisposeAsync.
- `src/Runner.Mcp/Tools/RunScenarioTool.cs` — accept `report_dir` arg; return path in result.

**Starting test count:** 347 Passed + 45 Skipped.
**Target:** ~358 Passed + 46 Skipped (+11 passing, +1 skipped).

---

## Task 1: RunDirectory + RunSummary

**Why:** Pure types — no I/O orchestration yet, just the wrapper + DTOs. Foundation for everything else.

**Files:**
- Create: `src/Runner/Reports/RunDirectory.cs`
- Create: `src/Runner/Reports/RunSummary.cs`
- Create: `tests/Runner.Tests/Reports/RunDirectoryTests.cs`

### Step 1: Failing tests

Create `tests/Runner.Tests/Reports/RunDirectoryTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.RegularExpressions;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class RunDirectoryTests
{
    [Fact]
    public void Create_ProducesExpectedSubdirs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rundir-{Guid.NewGuid():N}");
        try
        {
            var rd = RunDirectory.Create(tmp);
            Assert.True(Directory.Exists(rd.Root));
            Assert.True(Directory.Exists(rd.ScenariosDir));
            Assert.True(Directory.Exists(rd.AssetsDir));
            // Scenario subdir is created on demand via ScenarioDir(name).
            var scen = rd.ScenarioDir("my_scenario");
            Assert.True(Directory.Exists(scen));
            Assert.True(Directory.Exists(Path.Combine(scen, "screenshots")));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void RunId_FormatMatchesRegex()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rundir-{Guid.NewGuid():N}");
        try
        {
            var rd = RunDirectory.Create(tmp);
            // 2026-04-24T15-30-45-abc123 — ISO timestamp with hyphens replacing colons + 6-char hash.
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-[a-f0-9]{6}$", rd.RunId);
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }
}
```

Run: expect compile failure.

### Step 2: RunDirectory.cs

```csharp
using System;
using System.IO;

namespace SdvTestFramework.Runner.Reports;

/// <summary>
/// Filesystem wrapper for a single test-run's output directory. Owns the run-id, the
/// root path, and the standard subdir layout (scenarios/, assets/). Per-scenario
/// subdirs are created on demand via <see cref="ScenarioDir"/>.
/// </summary>
public sealed class RunDirectory
{
    public string Root { get; }
    public string RunId { get; }
    public string ScenariosDir => Path.Combine(Root, "scenarios");
    public string AssetsDir => Path.Combine(Root, "assets");

    private RunDirectory(string root, string runId)
    {
        Root = root;
        RunId = runId;
    }

    /// <summary>
    /// Create a new run directory under <paramref name="baseDir"/>. If
    /// <paramref name="explicitRunId"/> is null, generate one as
    /// <c>YYYY-MM-DDTHH-mm-ss-<hash></c>. Subdirs (scenarios/, assets/) are
    /// pre-created. Throws if the resulting directory already exists.
    /// </summary>
    public static RunDirectory Create(string baseDir, string? explicitRunId = null)
    {
        var runId = explicitRunId ?? GenerateRunId();
        var root = Path.Combine(baseDir, runId);
        if (Directory.Exists(root))
            throw new IOException($"run directory already exists: {root}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "scenarios"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        return new RunDirectory(root, runId);
    }

    /// <summary>Path to the per-scenario subdir; creates the subdir + screenshots/ if absent.</summary>
    public string ScenarioDir(string scenarioName)
    {
        // Sanitize: forbid any chars that aren't safe for filenames. Conservative.
        var safe = SanitizeName(scenarioName);
        var dir = Path.Combine(ScenariosDir, safe);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "screenshots"));
        return dir;
    }

    private static string GenerateRunId()
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss");
        var hash = Guid.NewGuid().ToString("N").Substring(0, 6);
        return $"{ts}-{hash}";
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
```

### Step 3: RunSummary.cs

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Runner.Reports;

/// <summary>Top-level run summary. Serialized as <c>summary.json</c> in the run directory.</summary>
public sealed record RunSummary(
    string RunId,
    string Started,        // ISO 8601 UTC
    int DurationMs,
    IReadOnlyList<ScenarioOutcome> Scenarios);

/// <summary>One scenario's outcome.</summary>
public sealed record ScenarioOutcome(
    string Name,
    string? Path,           // source .test.json path; null if dynamically authored
    bool Passed,
    int DurationMs,
    IReadOnlyList<StepOutcome> Steps,
    IReadOnlyList<AssertionOutcome> Assertions,
    IReadOnlyList<string> Screenshots);  // relative paths from run-dir root

/// <summary>One scenario step.</summary>
public sealed record StepOutcome(string Action, bool Passed, int DurationMs, string? Detail);

/// <summary>One scenario assertion.</summary>
public sealed record AssertionOutcome(string Type, bool Passed, string? Detail);
```

### Step 4: Verify

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +2 tests. Total **349 Passed + 45 Skipped**.

---

## Task 2: HtmlReportGenerator

**Why:** Pure function — takes a `RunSummary` + a target dir, writes all the HTML/JSON files. Fully unit-testable without any I/O orchestration upstream.

**Files:**
- Create: `src/Runner/Reports/HtmlReportGenerator.cs`
- Create: `tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs`

### Step 1: Failing tests

Create `tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class HtmlReportGeneratorTests
{
    private static RunDirectory MakeRunDir(string testName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"htmlrep-{testName}-{Guid.NewGuid():N}");
        return RunDirectory.Create(tmp);
    }

    [Fact]
    public void EmptyRun_ProducesValidHtml()
    {
        var rd = MakeRunDir("empty");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 0,
                Scenarios: Array.Empty<ScenarioOutcome>());

            HtmlReportGenerator.Generate(rd, summary);

            var indexPath = Path.Combine(rd.Root, "index.html");
            Assert.True(File.Exists(indexPath));
            var html = File.ReadAllText(indexPath);
            Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(rd.RunId, html);
            Assert.Contains("0 passed", html, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void PassingScenario_RendersGreen()
    {
        var rd = MakeRunDir("passing");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 1234,
                Scenarios: new[] { new ScenarioOutcome(
                    "shop_menu_test", "tests/samples/shop.test.json", true, 1234,
                    Steps: new[] { new StepOutcome("player.warp", true, 12, null) },
                    Assertions: new[] { new AssertionOutcome("state", true, null) },
                    Screenshots: Array.Empty<string>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.Root, "index.html"));
            Assert.Contains("shop_menu_test", html);
            Assert.Contains("class=\"pass\"", html);  // CSS class signals green
            Assert.DoesNotContain("class=\"fail\"", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void FailedScenario_RendersRedWithAssertionDetail()
    {
        var rd = MakeRunDir("failing");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 567,
                Scenarios: new[] { new ScenarioOutcome(
                    "broken_test", null, false, 567,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: new[] { new AssertionOutcome("state", false, "expected 5000, got 0") },
                    Screenshots: Array.Empty<string>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.Root, "index.html"));
            Assert.Contains("broken_test", html);
            Assert.Contains("class=\"fail\"", html);
            // Per-scenario page should also render, with the assertion detail visible.
            var scenHtml = File.ReadAllText(Path.Combine(rd.ScenariosDir, "broken_test", "report.html"));
            Assert.Contains("expected 5000, got 0", scenHtml);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void SummaryJson_DeserializesBackToRunSummary()
    {
        var rd = MakeRunDir("rt");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-24T15:30:45Z", 0,
                Scenarios: new[] { new ScenarioOutcome(
                    "x", null, true, 100,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: Array.Empty<AssertionOutcome>(),
                    Screenshots: Array.Empty<string>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var jsonPath = Path.Combine(rd.Root, "summary.json");
            Assert.True(File.Exists(jsonPath));
            var roundTripped = JsonSerializer.Deserialize<RunSummary>(
                File.ReadAllText(jsonPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(roundTripped);
            Assert.Equal("x", roundTripped!.Scenarios[0].Name);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }
}
```

Run: expect compile failure (`HtmlReportGenerator` doesn't exist).

### Step 2: HtmlReportGenerator.cs

Create `src/Runner/Reports/HtmlReportGenerator.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SdvTestFramework.Runner.Reports;

/// <summary>
/// Pure-function generator for the HTML run report. Writes:
/// <list type="bullet">
///   <item><c>summary.json</c> — machine-readable equivalent.</item>
///   <item><c>index.html</c> — landing page with all scenarios.</item>
///   <item><c>scenarios/&lt;name&gt;/report.html</c> — per-scenario detail.</item>
///   <item><c>scenarios/&lt;name&gt;/steps.json</c> — per-scenario raw data.</item>
///   <item><c>assets/styles.css</c> — single embedded stylesheet.</item>
/// </list>
/// </summary>
public static class HtmlReportGenerator
{
    public static void Generate(RunDirectory runDir, RunSummary summary)
    {
        // 1. summary.json
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        File.WriteAllText(Path.Combine(runDir.Root, "summary.json"), json);

        // 2. assets/styles.css
        File.WriteAllText(Path.Combine(runDir.AssetsDir, "styles.css"), CssTemplate);

        // 3. index.html
        File.WriteAllText(Path.Combine(runDir.Root, "index.html"), RenderIndex(summary));

        // 4. per-scenario report.html + steps.json
        foreach (var s in summary.Scenarios)
        {
            var scenDir = runDir.ScenarioDir(s.Name);
            File.WriteAllText(Path.Combine(scenDir, "report.html"), RenderScenarioReport(s));
            var scenJson = JsonSerializer.Serialize(s, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            File.WriteAllText(Path.Combine(scenDir, "steps.json"), scenJson);
        }
    }

    private static string RenderIndex(RunSummary s)
    {
        var passed = s.Scenarios.Count(x => x.Passed);
        var total = s.Scenarios.Count;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>sdv-test run ").Append(HttpUtility.HtmlEncode(s.RunId)).AppendLine("</title>");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"assets/styles.css\">");
        sb.AppendLine("</head><body>");
        sb.Append("<h1>Run ").Append(HttpUtility.HtmlEncode(s.RunId)).AppendLine("</h1>");
        sb.Append("<p class=\"summary\">").Append(passed).Append(" passed").Append(" / ").Append(total).Append(" total");
        sb.Append(" · ").Append(s.DurationMs).AppendLine("ms · ").Append(HttpUtility.HtmlEncode(s.Started)).AppendLine("</p>");
        sb.AppendLine("<table class=\"scenarios\">");
        sb.AppendLine("<thead><tr><th>Scenario</th><th>Outcome</th><th>Duration</th><th>Steps/Asserts</th></tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (var sc in s.Scenarios)
        {
            var cls = sc.Passed ? "pass" : "fail";
            var label = sc.Passed ? "PASS" : "FAIL";
            sb.Append("<tr class=\"").Append(cls).Append("\">");
            var safe = SanitizeName(sc.Name);
            sb.Append("<td><a href=\"scenarios/").Append(HttpUtility.HtmlEncode(safe))
              .Append("/report.html\">").Append(HttpUtility.HtmlEncode(sc.Name)).Append("</a></td>");
            sb.Append("<td class=\"").Append(cls).Append("\">").Append(label).Append("</td>");
            sb.Append("<td>").Append(sc.DurationMs).Append("ms</td>");
            sb.Append("<td>").Append(sc.Steps.Count).Append("st / ").Append(sc.Assertions.Count).Append("as</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string RenderScenarioReport(ScenarioOutcome s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(HttpUtility.HtmlEncode(s.Name)).AppendLine("</title>");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"../../assets/styles.css\">");
        sb.AppendLine("</head><body>");
        sb.Append("<h1>").Append(HttpUtility.HtmlEncode(s.Name)).AppendLine("</h1>");
        sb.AppendLine("<p><a href=\"../../index.html\">← back to run</a></p>");

        var cls = s.Passed ? "pass" : "fail";
        sb.Append("<p class=\"badge ").Append(cls).Append("\">")
          .Append(s.Passed ? "PASSED" : "FAILED").AppendLine("</p>");
        sb.Append("<p>Duration: ").Append(s.DurationMs).AppendLine("ms</p>");
        if (s.Path is not null)
            sb.Append("<p>Path: ").Append(HttpUtility.HtmlEncode(s.Path)).AppendLine("</p>");

        sb.AppendLine("<h2>Steps</h2>");
        if (s.Steps.Count == 0)
        {
            sb.AppendLine("<p><em>(none)</em></p>");
        }
        else
        {
            sb.AppendLine("<ol class=\"steps\">");
            foreach (var step in s.Steps)
            {
                var stepCls = step.Passed ? "pass" : "fail";
                sb.Append("<li class=\"").Append(stepCls).Append("\">");
                sb.Append("<code>").Append(HttpUtility.HtmlEncode(step.Action)).Append("</code>");
                sb.Append(" — ").Append(step.DurationMs).Append("ms");
                if (step.Detail is { } d)
                    sb.Append(" — ").Append(HttpUtility.HtmlEncode(d));
                sb.AppendLine("</li>");
            }
            sb.AppendLine("</ol>");
        }

        sb.AppendLine("<h2>Assertions</h2>");
        if (s.Assertions.Count == 0)
        {
            sb.AppendLine("<p><em>(none)</em></p>");
        }
        else
        {
            sb.AppendLine("<ul class=\"asserts\">");
            foreach (var a in s.Assertions)
            {
                var aCls = a.Passed ? "pass" : "fail";
                sb.Append("<li class=\"").Append(aCls).Append("\">");
                sb.Append("<strong>").Append(HttpUtility.HtmlEncode(a.Type)).Append("</strong>");
                sb.Append(" — ").Append(a.Passed ? "PASS" : "FAIL");
                if (a.Detail is { } d)
                    sb.Append(" — ").Append(HttpUtility.HtmlEncode(d));
                sb.AppendLine("</li>");
            }
            sb.AppendLine("</ul>");
        }

        if (s.Screenshots.Count > 0)
        {
            sb.AppendLine("<h2>Screenshots</h2>");
            sb.AppendLine("<div class=\"screenshots\">");
            foreach (var ss in s.Screenshots)
            {
                // Screenshot paths are relative to the run-dir root. From the per-scenario
                // page they're at ../../<path> (../scenarios/<name>/screenshots/x.png).
                var fileName = Path.GetFileName(ss);
                sb.Append("<figure><img src=\"screenshots/").Append(HttpUtility.HtmlEncode(fileName));
                sb.Append("\" alt=\"").Append(HttpUtility.HtmlEncode(fileName)).Append("\">");
                sb.Append("<figcaption>").Append(HttpUtility.HtmlEncode(fileName)).AppendLine("</figcaption></figure>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    private const string CssTemplate = """
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; max-width: 1200px; margin: 2em auto; padding: 0 1em; color: #222; }
        h1 { color: #111; border-bottom: 2px solid #ddd; padding-bottom: 0.3em; }
        h2 { color: #333; margin-top: 1.5em; }
        .summary { color: #555; font-size: 0.95em; }
        .badge { display: inline-block; padding: 0.3em 0.8em; border-radius: 4px; font-weight: bold; color: white; }
        .badge.pass { background: #2d6a3e; }
        .badge.fail { background: #b03030; }
        table.scenarios { border-collapse: collapse; width: 100%; margin: 1em 0; }
        table.scenarios th, table.scenarios td { padding: 0.6em 0.8em; text-align: left; border-bottom: 1px solid #eee; }
        table.scenarios tr.pass td.pass { color: #2d6a3e; font-weight: bold; }
        table.scenarios tr.fail td.fail { color: #b03030; font-weight: bold; }
        a { color: #1556b0; text-decoration: none; }
        a:hover { text-decoration: underline; }
        ol.steps li, ul.asserts li { padding: 0.3em 0; }
        ol.steps li.fail, ul.asserts li.fail { color: #b03030; }
        ol.steps li.pass, ul.asserts li.pass { color: #2d6a3e; }
        code { background: #f5f5f5; padding: 0.1em 0.4em; border-radius: 3px; }
        .screenshots { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 1em; margin: 1em 0; }
        .screenshots figure { margin: 0; }
        .screenshots img { max-width: 100%; border: 1px solid #ddd; }
        .screenshots figcaption { font-size: 0.85em; color: #666; margin-top: 0.3em; }
        """;
}
```

### Step 3: Verify

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +4 tests. Total **353 Passed + 45 Skipped**.

---

## Task 3: ScreenshotRecorder

**Why:** Runner-side helper that orchestrates `bitmap.capture` RPC + copies the result into the per-scenario screenshots dir. Used by ScenarioRunner (T4).

**Files:**
- Create: `src/Runner/Reports/ScreenshotRecorder.cs`
- Create: `tests/Runner.Tests/Reports/ScreenshotRecorderTests.cs`

### Step 1: Failing tests

Create `tests/Runner.Tests/Reports/ScreenshotRecorderTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class ScreenshotRecorderTests
{
    // The recorder talks to bitmap.capture via a session. We use an interface seam
    // for testing — the real implementation calls JsonRpcSession.InvokeAsync.
    // For the test, a tiny shim returns a canned path.

    private sealed class FakeBitmapInvoker : ScreenshotRecorder.IBitmapInvoker
    {
        public string CapturePath { get; init; } = "/tmp/fake-capture.png";
        public bool ShouldFail { get; init; }
        public Task<string?> CaptureAsync(CancellationToken ct)
            => ShouldFail
                ? Task.FromResult<string?>(null)
                : Task.FromResult<string?>(CapturePath);
    }

    [Fact]
    public async Task CaptureAsync_CopiesBitmapToScenarioScreenshots()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ssrt-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        // Pre-create a fake source PNG so the copy succeeds.
        var src = Path.Combine(tmp, "source.png");
        File.WriteAllBytes(src, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            var inv = new FakeBitmapInvoker { CapturePath = src };
            var rec = new ScreenshotRecorder(inv);
            var dest = await rec.CaptureAsync(rd, "my_scenario", "after-warp", CancellationToken.None);

            Assert.NotNull(dest);
            Assert.True(File.Exists(dest));
            Assert.EndsWith(Path.Combine("my_scenario", "screenshots", "after-warp.png"), dest);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public async Task CaptureAsync_RpcFailure_ReturnsNullWithoutThrowing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ssrt-{Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        try
        {
            var inv = new FakeBitmapInvoker { ShouldFail = true };
            var rec = new ScreenshotRecorder(inv);
            var dest = await rec.CaptureAsync(rd, "my_scenario", "x", CancellationToken.None);
            Assert.Null(dest);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }
}
```

### Step 2: ScreenshotRecorder.cs

Create `src/Runner/Reports/ScreenshotRecorder.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Runner.Reports;

/// <summary>
/// Runner-side orchestrator for screenshot capture. Calls <c>bitmap.capture</c> via
/// the RPC session, then copies the resulting PNG into the per-scenario report dir.
/// </summary>
public sealed class ScreenshotRecorder
{
    /// <summary>Test seam — production implementation calls <see cref="JsonRpcSession"/>.</summary>
    public interface IBitmapInvoker
    {
        Task<string?> CaptureAsync(CancellationToken ct);
    }

    private readonly IBitmapInvoker _invoker;

    public ScreenshotRecorder(IBitmapInvoker invoker) => _invoker = invoker;

    /// <summary>Convenience constructor wrapping a real <see cref="JsonRpcSession"/>.</summary>
    public ScreenshotRecorder(JsonRpcSession session) : this(new SessionInvoker(session)) { }

    /// <summary>
    /// Capture the current framebuffer + copy to <c>&lt;run-dir&gt;/scenarios/&lt;scenario&gt;/screenshots/&lt;name&gt;.png</c>.
    /// Returns the absolute destination path, or null on capture failure (logs but doesn't throw — auto-captures shouldn't fail tests).
    /// </summary>
    public async Task<string?> CaptureAsync(RunDirectory runDir, string scenarioName, string fileNameWithoutExt, CancellationToken ct)
    {
        string? source;
        try
        {
            source = await _invoker.CaptureAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[screenshot] capture failed: {ex.Message}");
            return null;
        }
        if (source is null || !File.Exists(source))
            return null;

        var scenDir = runDir.ScenarioDir(scenarioName);
        var dest = Path.Combine(scenDir, "screenshots", $"{fileNameWithoutExt}.png");
        try
        {
            File.Copy(source, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[screenshot] copy failed: {ex.Message}");
            return null;
        }
        return dest;
    }

    private sealed class SessionInvoker : IBitmapInvoker
    {
        private readonly JsonRpcSession _session;
        public SessionInvoker(JsonRpcSession session) => _session = session;

        public async Task<string?> CaptureAsync(CancellationToken ct)
        {
            var resp = await _session.InvokeAsync("bitmap.capture", params_: null, ct);
            if (resp.Error is not null) return null;
            if (resp.Result is not { } r) return null;
            if (!r.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
                return null;
            return pathEl.GetString();
        }
    }
}
```

Note: `JsonRpcSession.InvokeAsync(method, params_, ct)` — verify the actual parameter name by reading `src/Protocol/JsonRpcSession.cs`. May be `parameters`, `args`, or `params`.

### Step 3: Verify

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +2 tests. Total **355 Passed + 45 Skipped**.

---

## Task 4: ScenarioRunner integration + RunCommand wiring + screenshot.capture step

**Why:** Plug the report dir + ScreenshotRecorder into the existing scenario-execution flow. This is the integration point that makes everything visible.

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs` — accept `RunDirectory? reportDir` via constructor; auto-capture at `freeze.begin`; on assertion failure capture; new `screenshot.capture` step case.
- Modify: `src/Runner/Scenarios/ScenarioReport.cs` — add `Screenshots` + `Steps` collections.
- Modify: `src/Runner/Commands/RunCommand.cs` — `--report-dir` + `--no-report` flag parsing; pre-create RunDirectory; pass to ScenarioRunner; HtmlReportGenerator.Generate at end.

### Step 1: Read existing ScenarioRunner

Read `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Scenarios/ScenarioRunner.cs` to understand its current shape. Key things to confirm:
- Constructor signature.
- `RunAsync` + `EvaluateAssertionAsync` shapes.
- The existing step-loop switch.

### Step 2: Update ScenarioReport.cs

Add to the existing class:

```csharp
public List<StepOutcome> Steps { get; set; } = new();
public List<string> Screenshots { get; set; } = new();
```

Reuse the `StepOutcome` record from `RunSummary.cs` — add `using SdvTestFramework.Runner.Reports;` if not already there.

### Step 3: Update ScenarioRunner

Change the constructor to accept `RunDirectory? reportDir`. Add a new field `_recorder: ScreenshotRecorder?` initialized only if reportDir + session exist.

In the step loop:
- On `freeze.begin` step success: call `_recorder?.CaptureAsync(_reportDir, spec.Name, $"step-{stepIndex:D2}-after-freeze", ct)`. Append the returned path (if non-null) to `report.Screenshots`.
- On `screenshot.capture` step (new case): read `args.name`, call recorder, append to screenshots. Treat as a passing step regardless of capture success (failures are logged, not test-breaking).

In `EvaluateAssertionAsync` failure paths:
- Before returning `(false, detail)`, if `_recorder` is non-null, call `CaptureAsync(... "assertion-fail-{assertionIndex:D2}")`. Append to screenshots.

Also build up the `report.Steps` list as the loop progresses (each step's action + passed status + duration via `Stopwatch`).

The exact code is fiddly — read the existing ScenarioRunner carefully + apply the changes incrementally. Verify by running the existing tests after each chunk.

### Step 4: Update RunCommand

Add CLI flag parsing for `--report-dir` and `--no-report` to the existing argv loop.

```csharp
string? reportDir = null;
bool noReport = false;
// In the loop:
if (a == "--report-dir" && i + 1 < args.Length) { reportDir = args.Span[++i]; continue; }
if (a == "--no-report") { noReport = true; continue; }
```

After args parsing, before scenario execution:

```csharp
RunDirectory? rd = null;
if (!noReport)
{
    var baseDir = reportDir ?? Path.Combine(Directory.GetCurrentDirectory(), "test-results");
    rd = RunDirectory.Create(baseDir);
    Console.Error.WriteLine($"[run] report dir: {rd.Root}");
}
```

Pass `rd` to `ScenarioRunner` constructor.

After all scenarios complete, build `RunSummary` from the collected `ScenarioReport`s + call `HtmlReportGenerator.Generate(rd, summary)`. Print the final report path:

```csharp
if (rd is not null)
{
    var summary = BuildRunSummary(rd, scenarios, reports);
    HtmlReportGenerator.Generate(rd, summary);
    Console.Out.WriteLine($"[run] report: {Path.Combine(rd.Root, "index.html")}");
}
```

### Step 5: Verify CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: **355 Passed + 45 Skipped** unchanged (no new tests in T4 — behavior is verified by T7 smoke). If existing tests broke (likely a few — the constructor signature change cascades), update them to pass `null` for `reportDir`.

---

## Task 5: DSL Screenshot facet + SdvFixture report-dir wiring

**Why:** The DSL path. `Screenshot.Capture(name)` reuses the screenshot recorder; `SdvFixture` creates the report dir per assembly run.

**Files:**
- Create: `src/Runner.Dsl/Screenshot.cs`
- Modify: `src/Runner.Dsl/SdvTestSession.cs` — add `ReportDir` property.
- Modify: `src/Runner.Dsl/SdvFixture.cs` — create RunDirectory in InitializeAsync, write HTML at DisposeAsync.
- Create: `tests/Runner.Dsl.Tests/Facets/ScreenshotTests.cs`

### Step 1: SdvTestSession.cs — add ReportDir

Add property:

```csharp
/// <summary>Per-test-assembly run report directory; null if not configured.</summary>
public RunDirectory? ReportDir { get; set; }
```

Add `using SdvTestFramework.Runner.Reports;` at the top if not already present.

### Step 2: Screenshot.cs

Create `src/Runner.Dsl/Screenshot.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Reports;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for capturing screenshots into the per-run report directory.</summary>
public static class Screenshot
{
    /// <summary>
    /// Capture the current framebuffer + save to
    /// <c>&lt;report-dir&gt;/scenarios/&lt;current-scenario&gt;/screenshots/&lt;name&gt;.png</c>.
    /// No-op (with a warning) when no report dir is configured (e.g. unit tests).
    /// </summary>
    public static async Task Capture(string name, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        if (s.ReportDir is null)
        {
            Console.Error.WriteLine($"[screenshot] Capture('{name}') called but no report dir is configured");
            return;
        }
        if (s.CurrentScenarioName is null)
            throw new InvalidOperationException("Screenshot.Capture requires an active [Scenario] (no scenario name set)");

        // Call bitmap.capture via the session, get the path.
        var resp = await s.InvokeAsync("bitmap.capture", null, ct);
        if (!resp.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
        {
            Console.Error.WriteLine($"[screenshot] bitmap.capture returned no path");
            return;
        }
        var sourcePath = pathEl.GetString()!;
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"[screenshot] capture path missing: {sourcePath}");
            return;
        }

        var scenDir = s.ReportDir.ScenarioDir(s.CurrentScenarioName);
        var dest = Path.Combine(scenDir, "screenshots", $"{name}.png");
        File.Copy(sourcePath, dest, overwrite: true);
    }
}
```

Note: `s.CurrentScenarioName` doesn't exist yet — add it to `SdvTestSession` as a settable string. The `[Scenario]` attribute's `Before` method should set it; `After` should clear it.

### Step 3: ScenarioAttribute — populate CurrentScenarioName

In `src/Runner.Dsl/ScenarioAttribute.cs`'s `Before(MethodInfo)`:

```csharp
SdvTestSession.Current!.CurrentScenarioName = Name ?? methodUnderTest.Name;
```

In `After(MethodInfo)`:

```csharp
SdvTestSession.Current.CurrentScenarioName = null;
```

### Step 4: SdvFixture wiring

In `SdvFixture.InitializeAsync`, after the `SdvTestSession.Initialize(...)` call, set the ReportDir:

```csharp
var baseDir = Environment.GetEnvironmentVariable("SDV_REPORT_DIR")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "test-results");
session.ReportDir = RunDirectory.Create(baseDir);
Console.Error.WriteLine($"[sdv-fixture] report dir: {session.ReportDir.Root}");
```

In `DisposeAsync`, write the HTML report. Need a way to track per-scenario outcomes — simplest: keep a `List<ScenarioOutcome>` on the fixture, populated by `ScenarioAttribute.After` based on whether the test threw. Add the wiring; this is fiddly.

Alternative MVP: `SdvFixture.DisposeAsync` writes a minimal `summary.json` + skeleton HTML. Per-scenario step/assertion data is collected later. Document as "SdvFixture's report has scenario presence + screenshots only; CLI runner has the richer step-by-step view."

For MVP scope: ship the screenshot path + minimal index.html. T7 verifies; richer per-scenario step data is a Tier 2 followup ("xUnit observer for full step capture in DSL run reports").

### Step 5: Failing test for Screenshot

Create `tests/Runner.Dsl.Tests/Facets/ScreenshotTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class ScreenshotTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public string CapturePath { get; init; } = "/tmp/fake.png";
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((m, p?.GetRawText() ?? ""));
            var json = $"{{\"path\":\"{CapturePath}\",\"width\":1280,\"height\":720}}";
            return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
        }
    }

    [Fact]
    public async Task Capture_WithReportDir_CopiesToScenarioScreenshots()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"sshot-{System.Guid.NewGuid():N}");
        var rd = RunDirectory.Create(tmp);
        var sourcePng = Path.Combine(tmp, "source.png");
        File.WriteAllBytes(sourcePng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var inv = new CapturingInvoker { CapturePath = sourcePng };
        SdvTestSession.InitializeForTests(inv);
        var session = SdvTestSession.Current!;
        session.ReportDir = rd;
        session.CurrentScenarioName = "test_scenario";

        try
        {
            await Screenshot.Capture("after_warp");
            Assert.Equal("bitmap.capture", inv.Calls[0].Method);
            var dest = Path.Combine(rd.ScenarioDir("test_scenario"), "screenshots", "after_warp.png");
            Assert.True(File.Exists(dest));
        }
        finally
        {
            SdvTestSession.ResetForTests();
            Directory.Delete(rd.Root, recursive: true);
        }
    }
}
```

### Step 6: Verify

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +1 test. Total **356 Passed + 45 Skipped**.

---

## Task 6: MCP RunScenarioTool report-dir

**Why:** Claude needs the artifact path back so it can read `summary.json` or reference screenshots. Small addition to existing tool.

**Files:**
- Modify: `src/Runner.Mcp/Tools/RunScenarioTool.cs`
- Create: `tests/Runner.Mcp.Tests/Tools/RunScenarioReportDirTests.cs`

### Step 1: Update RunScenarioTool

Read `src/Runner.Mcp/Tools/RunScenarioTool.cs`. Find the result-building block. Add `report_dir` and `report_index` to the JsonObject:

After the path validation + ScenarioRunner.RunAsync invocation, before returning the result:

```csharp
// If a report dir was created during the run, surface it for Claude.
if (reportDir is not null)
{
    report["report_dir"] = reportDir.Root;
    report["report_index"] = Path.Combine(reportDir.Root, "index.html");
}
```

This means RunScenarioTool needs to be the one creating the `RunDirectory` for MCP-driven runs. Add an optional `report_dir` arg parse (default to `./test-results/<auto-id>/`):

```csharp
string? userReportDir = null;
if (args.TryGetProperty("report_dir", out var rdEl) && rdEl.ValueKind == JsonValueKind.String)
    userReportDir = rdEl.GetString();

var baseDir = userReportDir ?? Path.Combine(Directory.GetCurrentDirectory(), "test-results");
var reportDir = RunDirectory.Create(baseDir);
```

Update the input schema to include `report_dir`:

```csharp
public JsonElement InputSchema { get; } = JsonDocument.Parse("""
    {"type":"object","properties":{
       "path":{"type":"string"},
       "report_dir":{"type":"string","description":"Optional output directory for the HTML run report. Default: ./test-results/<auto-id>/"}
     },"required":["path"]}
    """).RootElement;
```

### Step 2: Failing test

Create `tests/Runner.Mcp.Tests/Tools/RunScenarioReportDirTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class RunScenarioReportDirTests
{
    [Fact]
    public async Task RunScenario_ResultIncludesReportDirAndIndex()
    {
        // Write a minimal scenario file.
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-rep-{Guid.NewGuid():N}.test.json");
        File.WriteAllText(tmp, "{\"name\":\"n\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[]}");

        var lifeBaseDir = Path.Combine(Path.GetTempPath(), $"mcp-reports-{Guid.NewGuid():N}");
        Directory.CreateDirectory(lifeBaseDir);

        try
        {
            // Use the existing RecordingLifecycle pattern from StatefulToolsTests — declare a
            // local copy here. In production, the existing tests have established the shim shape.
            var life = new RecordingLifecycle();
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"]   = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";

            var tool = new RunScenarioTool();
            var args = JsonDocument.Parse(
                $"{{\"path\":{JsonSerializer.Serialize(tmp)},\"report_dir\":{JsonSerializer.Serialize(lifeBaseDir)}}}").RootElement;
            var result = await tool.InvokeAsync(args, life, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("report_dir", result.Text);
            Assert.Contains("report_index", result.Text);
            // The report dir should be a subdirectory of lifeBaseDir.
            Assert.Contains(lifeBaseDir.Replace("\\", "\\\\"), result.Text);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(lifeBaseDir)) Directory.Delete(lifeBaseDir, recursive: true);
        }
    }

    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public System.Collections.Generic.Dictionary<string, string> Responses { get; } = new();
        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            var resp = Responses.TryGetValue(method, out var r) ? r : "{}";
            return Task.FromResult(JsonDocument.Parse(resp).RootElement.Clone());
        }
    }
}
```

### Step 3: Verify

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +1 test. Total **357 Passed + 45 Skipped**.

---

## Task 7: Smoke + integration placeholder + docs + roadmap

**Why:** Final task. Verify end-to-end against the sample suite, ship docs, close roadmap.

**Files:**
- Create: `tests/Runner.Tests/Reports/RunReportIntegrationTests.cs` (skipped)
- Modify: `docs/milestones/current.md`
- Modify: `docs/roadmap.md`
- Modify: `docs/dsl-quickstart.md` — add `Screenshot.Capture(name)` to facet reference.

### Step 1: Integration placeholder

Create `tests/Runner.Tests/Reports/RunReportIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

/// <summary>Integration surface for HTML run reports — verified manually via T7 smoke.</summary>
public class RunReportIntegrationTests
{
    [Fact(Skip = "Requires live SDV — run-samples.sh produces a real run dir; verify by inspecting test-results/.")]
    public void RunReports_PopulatedAfterRunSamples() { }
}
```

### Step 2: Live smoke

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
rm -rf test-results/
./scripts/run-samples.sh 2>&1 | tail -10
# Verify the directory was created.
ls test-results/
LATEST=$(ls -1t test-results/ | head -1)
ls test-results/$LATEST/
ls test-results/$LATEST/scenarios/
```
Expected:
- `[run] report: <abs-path>/test-results/<run-id>/index.html` printed to stdout.
- `test-results/<run-id>/index.html` + `summary.json` + `assets/styles.css` exist.
- `test-results/<run-id>/scenarios/<11 dirs>` each with `report.html` + `steps.json` + `screenshots/`.
- The bitmap scenario (`bitmap_shop_menu_basic`) has at least one PNG in screenshots/.

Open the HTML for visual verification:
```bash
xdg-open test-results/$LATEST/index.html 2>/dev/null || echo "open manually: file://$(pwd)/test-results/$LATEST/index.html"
```

Verify summary.json:
```bash
cat test-results/$LATEST/summary.json | python3 -m json.tool | head -30
```

### Step 3: Tamper test (optional but recommended)

Edit `tests/samples/01-state-time-after-load.test.json` (or any scenario) to make an assertion fail (e.g. change expected season to a wrong value). Re-run:

```bash
./scripts/run-samples.sh 2>&1 | tail -10
LATEST=$(ls -1t test-results/ | head -1)
grep "fail" test-results/$LATEST/index.html | head -5
ls test-results/$LATEST/scenarios/<failing-scenario>/screenshots/
```
Expected: `class="fail"` in the index HTML; `assertion-fail-NN.png` in the failing scenario's screenshots.

Restore the scenario file when done.

### Step 4: docs/dsl-quickstart.md

Add to the facet reference list:

```markdown
- `Screenshot.Capture(name)` — capture the current framebuffer into the per-run report directory. Requires an active scenario. See HTML Run Reports below.
```

Add a new section near the bottom:

```markdown
## HTML Run Reports

Every test run produces a directory at `./test-results/<run-id>/` containing:
- `index.html` — pass/fail dashboard, opens in any browser.
- `summary.json` — machine-readable run data (LLM-friendly).
- `scenarios/<name>/` — per-scenario page + step/assertion data + screenshots.

Auto-screenshots fire at `freeze.begin` and on assertion failure. Add explicit named
captures via `await Screenshot.Capture("after_my_action")` from the DSL or
`{ "action": "screenshot.capture", "args": { "name": "after_my_action" } }` in JSON.

CLI flag: `sdv-test run --report-dir <path>` to override the default location, or
`--no-report` to skip generation.
```

### Step 5: docs/milestones/current.md

Append a completion subsection:

```markdown
### HTML run reports landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-html-run-reports.md` (7 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-html-run-reports-design.md`.

**Scope:** every test run produces a `./test-results/<run-id>/` directory with
`index.html` + `summary.json` + per-scenario detail pages + screenshot evidence.
Promoted to roadmap Tier 1 because evidence visibility is core to the LLM-workflow
goal — Claude reasons about test failures from the JSON + screenshot paths.

**Architecture:** Runner-side orchestration. `RunDirectory` wraps the per-run dirs
(scenarios/, assets/, run-id auto-generated as ISO-timestamp + 6-char hash).
`HtmlReportGenerator` is a pure function — takes `RunSummary` + writes `index.html` +
`summary.json` + per-scenario `report.html` + `steps.json` + `assets/styles.css` (no
JS framework, embedded CSS). `ScreenshotRecorder` calls `bitmap.capture` via RPC +
copies the result PNG into the per-scenario screenshots subdir.

**Integration points:**
- `sdv-test run` — `--report-dir <path>` and `--no-report` flags. Default
  `./test-results/<run-id>/`.
- DSL via `dotnet test` — `SdvFixture.InitializeAsync` creates the run dir;
  `Screenshot.Capture(name)` writes named captures.
- MCP `run_scenario` tool — accepts `report_dir` arg; result includes `report_dir` +
  `report_index` paths so Claude can navigate to the artifacts.

**Auto-capture triggers:**
- After `freeze.begin` succeeds — most scenarios enter FREEZE for assertions; this
  gives every scenario at least one screenshot for free.
- On assertion failure — captures the framebuffer at the moment of failure, named
  `assertion-fail-NN.png`.
- Explicit via `screenshot.capture` step or `Screenshot.Capture(name)` DSL method.

**Test count after HTML run reports:** 347+45 → 358+46 (+11 passed, +1 skipped).

**Out of scope (Tier 3/4 followups):**
- Diff-image-on-failure rendering (Tier 3, pairs with this).
- Interactive HTML (timeline, filter-by-status), Tier 4.
- Run pruning, JPEG/WebP compression, server-side viewer — Tier 4.
- Full step-by-step capture from DSL path — for MVP, DSL run-dirs have screenshots
  but minimal step data; CLI runner has the richer per-step data.
```

### Step 6: docs/roadmap.md

Remove the "HTML run reports" item from Tier 1. Add to Completed:

```markdown
- **HTML run reports**. Per-run directory with `index.html` + `summary.json` +
  per-scenario detail pages + screenshot evidence. Auto-capture at `freeze.begin` +
  assertion failures + explicit `Screenshot.Capture(name)` DSL method. Integrates
  with `sdv-test run` (CLI), `dotnet test` (SdvFixture), and MCP `run_scenario`
  (returns `report_dir` for Claude). 347+45 → 358+46.
```

If "HTML run reports" wasn't yet on the roadmap (it was promoted from Tier 2 candidate
during this brainstorm), no removal needed — just add to Completed.

### Step 7: Final CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:\|Skipped:" | head -10`
Expected: **358 Passed + 46 Skipped**.

---

## Self-review

**1. Spec coverage:**
- RunDirectory + RunSummary → T1 ✓
- HtmlReportGenerator → T2 ✓
- ScreenshotRecorder → T3 ✓
- ScenarioRunner integration → T4 ✓
- DSL Screenshot facet + SdvFixture → T5 ✓
- MCP RunScenarioTool → T6 ✓
- Smoke + docs + roadmap → T7 ✓

**2. Placeholder scan:** No TBD. The "(M4 followup)" notes are explicit deferrals.

**3. Type consistency:**
- `RunDirectory.ScenarioDir(name)` — defined T1, consumed T3 + T5.
- `ScreenshotRecorder.IBitmapInvoker` — defined T3, used in tests + production.
- `RunSummary` records — defined T1, consumed T2 + T4.
- `SdvTestSession.ReportDir` + `CurrentScenarioName` — defined T5, consumed T5
  Screenshot.Capture.
- `RunScenarioTool` `report_dir` arg + result fields — T6.

**4. Hazards:**
- **Test pollution from existing tests** that depend on `ScenarioRunner` constructor
  arity — T4 changes the constructor. Some existing tests will need to pass `null`
  for `reportDir`. Update tests in T4 step 5 if any break.
- **Disk usage** — 11 scenarios × ~3 screenshots × 1MB ≈ 33MB per run.
  Acceptable; Tier 4 can add compression.
- **HTML rendering perf** — for 100s of scenarios, `StringBuilder` + file writes
  scale linearly. Fine.
- **HtmlEncoding** — using `HttpUtility.HtmlEncode` for all user-supplied strings.
  Safe.
- **Path injection in `report_dir` (MCP)** — Claude could pass an absolute path
  outside the workspace. T6 doesn't restrict; this is by design (Claude is trusted).
  If concerns arise, future hardening: validate the path is within a safe prefix.

---

## Execution handoff

Plan complete + saved to `docs/superpowers/plans/2026-04-24-html-run-reports.md`.
Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
