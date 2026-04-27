# M2 TAP + JUnit Reporters — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green** (same convention as all other plans). T5's additional gates: `sdv-test run --reporter tap tests/samples/` prints valid TAP 13 to stdout AND `./scripts/run-samples.sh` still reports 10/10 PASS.

**Goal:** Add `sdv-test run --reporter <console|tap|junit> [--output PATH]` so the framework's scenario results can be consumed by real CI systems. The existing Playwright-style stdout summary moves into a `ConsoleReporter`; TAP 13 and Jenkins-compatible JUnit XML are the two new formats.

**Architecture:** Refactor the inline output in `RunCommand.cs` behind an `IReporter` interface with three implementations. `RunCommand` parses the new flags, collects the existing `List<ScenarioReport>` (enhanced with a `Path` field), and dispatches to the chosen reporter at the end. No scenario-execution logic changes — reporters are pure output adapters. Scenario = testcase granularity across all reporters; per-assertion nesting is rejected as UI-awkward in CI.

**Tech Stack:**
- .NET 10 (Runner) — unchanged
- `System.Xml` / `XmlWriter` — JUnit output (built-in, no new NuGet)
- xUnit — unit tests (string-based; no file I/O needed beyond `StringWriter`)

**Design spec:** `docs/superpowers/specs/2026-04-23-m2-reporters-design.md`

---

## File structure

**New files (`src/Runner/Reporters/`):**
- `IReporter.cs` — single-method interface: `void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)`.
- `ConsoleReporter.cs` — byte-for-byte preservation of the current RunCommand output.
- `TapReporter.cs` — TAP 13 with YAML diagnostics.
- `JunitReporter.cs` — Jenkins-compatible JUnit XML via `XmlWriter`.
- `ReporterFactory.cs` — `IReporter Create(string name)` mapping `console|tap|junit` (case-insensitive) → instance; throws `ArgumentException` on unknown.

**Modified files:**
- `src/Runner/Scenarios/ScenarioReport.cs` — add `public string Path { get; set; } = string.Empty;` so reporters know which file produced the report (JUnit's classname, console's trailing ` — <path>`).
- `src/Runner/Commands/RunCommand.cs` — parse `--reporter` + `--output`; replace the inline output loop with a `ReporterFactory.Create(...).Report(reports, writer)` call.
- `src/Runner/Program.cs` — update `PrintHelp()` to mention the new flags.
- `docs/milestones/current.md` — M2-reporters completion subsection.

**New tests (`tests/Runner.Tests/`):**
- `ConsoleReporterTests.cs` — 1 test: output shape matches pre-refactor byte-for-byte.
- `TapReporterTests.cs` — 3 tests: all-pass, failure-with-yaml, empty.
- `JunitReporterTests.cs` — 3 tests: all-pass XML structure, failure body + attrs, empty.
- `RunCommandReporterFlagTests.cs` — 3 tests: default is console, `--reporter tap` dispatches, unknown reporter → exit 2.

**Verification:** `./scripts/ci.sh` green after each task. Live smoke after T5.

**Starting test count:** 229 Passed + 31 Skipped.
**Target test count after reporters:** ~239 Passed + 31 Skipped (+10 passing, no new skipped).

---

## Task 1: IReporter + ReporterFactory + ConsoleReporter refactor

**Why:** Create the shared interface + move the existing console output behind it before adding new formats. The user-visible output must be unchanged by the end of this task.

**Files:**
- Create: `src/Runner/Reporters/IReporter.cs`
- Create: `src/Runner/Reporters/ReporterFactory.cs`
- Create: `src/Runner/Reporters/ConsoleReporter.cs`
- Modify: `src/Runner/Scenarios/ScenarioReport.cs` — add `Path` property
- Modify: `src/Runner/Commands/RunCommand.cs` — populate `Path` + dispatch through reporter
- Create: `tests/Runner.Tests/ConsoleReporterTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Add Path to ScenarioReport**

In `src/Runner/Scenarios/ScenarioReport.cs`, find the property block. After the existing `Name` property, add:

```csharp
    /// <summary>Absolute or repo-relative path to the scenario file that produced this report.
    /// Populated by <c>RunCommand</c> after <c>ScenarioRunner.RunAsync</c> returns. Consumed by
    /// reporters (JUnit uses it as <c>classname</c>; console appends it after the scenario name).</summary>
    public string Path { get; set; } = string.Empty;
```

- [ ] **Step 2: Create IReporter**

Create `src/Runner/Reporters/IReporter.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// Output adapter for scenario run results. One implementation per supported format
/// (console, TAP, JUnit). Called once per run with the full list of scenario reports.
/// </summary>
public interface IReporter
{
    /// <summary>Serialize the reports to <paramref name="output"/> in the reporter's format.</summary>
    void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output);
}
```

- [ ] **Step 3: Create ReporterFactory**

Create `src/Runner/Reporters/ReporterFactory.cs`:

```csharp
using System;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>Factory mapping reporter-name strings (CLI input) to <see cref="IReporter"/> instances.</summary>
public static class ReporterFactory
{
    /// <summary>
    /// Create a reporter for the given name. Names are case-insensitive: "console", "tap", "junit".
    /// Throws <see cref="ArgumentException"/> for unknown names so RunCommand can surface
    /// a usage error with exit code 2.
    /// </summary>
    public static IReporter Create(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "console" => new ConsoleReporter(),
            "tap" => new TapReporter(),
            "junit" => new JunitReporter(),
            _ => throw new ArgumentException(
                $"unknown reporter: {name} (known: console, tap, junit)", nameof(name)),
        };
    }
}
```

Note: this references `TapReporter` and `JunitReporter` which don't exist yet. Stub them inline as empty shells now so this task builds cleanly — T2 and T3 fill them in:

Create `src/Runner/Reporters/TapReporter.cs` (stub — fleshed out in T2):

```csharp
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>TAP 13 reporter — implemented in T2.</summary>
public sealed class TapReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        throw new System.NotImplementedException("TapReporter lands in T2");
    }
}
```

Create `src/Runner/Reporters/JunitReporter.cs` (stub — fleshed out in T3):

```csharp
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>JUnit XML reporter — implemented in T3.</summary>
public sealed class JunitReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        throw new System.NotImplementedException("JunitReporter lands in T3");
    }
}
```

- [ ] **Step 4: Write failing ConsoleReporter test**

Create `tests/Runner.Tests/ConsoleReporterTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Reporters;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ConsoleReporterTests
{
    [Fact]
    public void Report_MixedPassFail_MatchesExistingOutputShape()
    {
        // Exact byte-for-byte output that RunCommand currently writes. Preserving this
        // guarantees the refactor is user-invisible.
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "alpha", Path = "/abs/alpha.test.json", Passed = true, DurationMs = 443 },
            new ScenarioReport
            {
                Name = "beta",
                Path = "/abs/beta.test.json",
                Passed = false,
                DurationMs = 454,
                Failures = new List<string> { "draw.contains: thing not found" },
            },
        };
        var writer = new StringWriter();
        new ConsoleReporter().Report(reports, writer);

        var expected =
            "  PASS alpha (443ms) — /abs/alpha.test.json\n" +
            "  FAIL beta (454ms) — /abs/beta.test.json\n" +
            "        draw.contains: thing not found\n" +
            "\n" +
            "[run] 1/2 passed\n";
        // Normalize line endings — StringWriter on Windows would emit \r\n; we compare with \n.
        Assert.Equal(expected, writer.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void Report_AllPass_PrintsAllPassedLine()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "x", Path = "/x.test.json", Passed = true, DurationMs = 10 },
        };
        var writer = new StringWriter();
        new ConsoleReporter().Report(reports, writer);
        Assert.Contains("[run] 1/1 passed", writer.ToString());
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter ConsoleReporter`
Expected: FAIL — `ConsoleReporter` doesn't exist yet.

- [ ] **Step 5: Create ConsoleReporter**

Create `src/Runner/Reporters/ConsoleReporter.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// Default Playwright-style reporter — one line per scenario, indented failures, and a
/// trailing summary line. Byte-for-byte compatible with the pre-M2-reporters output of
/// <c>RunCommand</c>.
/// </summary>
public sealed class ConsoleReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        int failed = 0;
        foreach (var report in reports)
        {
            var status = report.Passed ? "PASS" : "FAIL";
            output.WriteLine($"  {status} {report.Name} ({report.DurationMs}ms) — {report.Path}");
            foreach (var f in report.Failures)
                output.WriteLine($"        {f}");
            if (!report.Passed) failed++;
        }
        output.WriteLine();
        output.WriteLine($"[run] {reports.Count - failed}/{reports.Count} passed");
    }
}
```

- [ ] **Step 6: Rewire RunCommand to use ConsoleReporter + populate Path**

In `src/Runner/Commands/RunCommand.cs`, find the run-scenarios block (currently around lines 139-154):

```csharp
            // ---- run scenarios ----
            var runner = new ScenarioRunner(session);
            int failed = 0;
            foreach (var (path, spec) in scenarios)
            {
                var report = await runner.RunAsync(spec, ct);
                var status = report.Passed ? "PASS" : "FAIL";
                Console.WriteLine($"  {status} {spec.Name} ({report.DurationMs}ms) — {path}");
                foreach (var f in report.Failures)
                    Console.WriteLine($"        {f}");
                if (!report.Passed) failed++;
            }

            Console.WriteLine();
            Console.WriteLine($"[run] {scenarios.Count - failed}/{scenarios.Count} passed");
            return failed == 0 ? 0 : 1;
```

Replace with:

```csharp
            // ---- run scenarios ----
            var runner = new ScenarioRunner(session);
            var collected = new List<ScenarioReport>(scenarios.Count);
            foreach (var (path, spec) in scenarios)
            {
                var report = await runner.RunAsync(spec, ct);
                report.Path = path;
                collected.Add(report);
            }

            // Output via the selected reporter. Console is the default.
            new SdvTestFramework.Runner.Reporters.ConsoleReporter().Report(collected, Console.Out);

            int failed = 0;
            foreach (var r in collected) if (!r.Passed) failed++;
            return failed == 0 ? 0 : 1;
```

Note: `--reporter` and `--output` plumbing lands in T4 — for now, this task hardcodes `ConsoleReporter`, which preserves today's behavior exactly.

Add `using SdvTestFramework.Runner.Reporters;` near the top if you want the unqualified name; either style is fine.

- [ ] **Step 7: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 229 → 231 (+2 new passing tests: both ConsoleReporter cases).

Verify the console output is unchanged by running a tiny schema-only check (no live SDV needed):

```bash
dotnet run --project src/Runner -c Release -- list tests/samples/
```

Expected: 10 scenarios listed — `list` is untouched by this task, sanity-checks nothing regressed.

---

## Task 2: TapReporter

**Why:** TAP 13 is the lingua franca for CI test-reporting. Small reporter, easy to unit-test against string snapshots.

**Files:**
- Modify: `src/Runner/Reporters/TapReporter.cs` — replace stub with real impl
- Create: `tests/Runner.Tests/TapReporterTests.cs`

**Dependencies:** Task 1.

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Tests/TapReporterTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Reporters;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class TapReporterTests
{
    private static string RenderTo(IReadOnlyList<ScenarioReport> reports)
    {
        var sw = new StringWriter();
        new TapReporter().Report(reports, sw);
        return sw.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void Report_AllPass_EmitsOkLinesWithPlan()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "alpha", Path = "/a.test.json", Passed = true, DurationMs = 10 },
            new ScenarioReport { Name = "beta", Path = "/b.test.json", Passed = true, DurationMs = 20 },
        };
        var expected =
            "TAP version 13\n" +
            "1..2\n" +
            "ok 1 - alpha\n" +
            "ok 2 - beta\n";
        Assert.Equal(expected, RenderTo(reports));
    }

    [Fact]
    public void Report_FailureWithYamlBlock_IncludesDiagnostics()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport
            {
                Name = "broken",
                Path = "/b.test.json",
                Passed = false,
                DurationMs = 125,
                Failures = new List<string> { "step 'player.warp' failed: no location named: Nowhere" },
            },
        };
        var output = RenderTo(reports);
        Assert.Contains("TAP version 13\n", output);
        Assert.Contains("1..1\n", output);
        Assert.Contains("not ok 1 - broken\n", output);
        Assert.Contains("  ---\n", output);
        Assert.Contains("  duration_ms: 125\n", output);
        Assert.Contains("  failures:\n", output);
        Assert.Contains("    - \"step 'player.warp' failed: no location named: Nowhere\"\n", output);
        Assert.Contains("  ...\n", output);
    }

    [Fact]
    public void Report_EmptyReports_EmitsZeroPlan()
    {
        var expected =
            "TAP version 13\n" +
            "1..0\n";
        Assert.Equal(expected, RenderTo(new List<ScenarioReport>()));
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter TapReporter`
Expected: FAIL — `TapReporter.Report` throws `NotImplementedException` (stub from T1).

- [ ] **Step 2: Flesh out TapReporter**

Replace `src/Runner/Reporters/TapReporter.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// TAP 13 reporter. Emits "TAP version 13", an "1..N" plan, and one
/// "ok|not ok" line per scenario. Failures get a YAML diagnostic block indented 2 spaces
/// with duration + failure list.
/// </summary>
/// <remarks>
/// TAP 13 spec: https://testanything.org/tap-version-13-specification.html. Near-universal
/// CI consumer support (GitHub Actions, GitLab, Jenkins). TAP 14's subtests aren't useful
/// here — each scenario is a flat test case, not a hierarchy.
/// </remarks>
public sealed class TapReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        output.WriteLine("TAP version 13");
        output.WriteLine($"1..{reports.Count}");

        for (int i = 0; i < reports.Count; i++)
        {
            var r = reports[i];
            var status = r.Passed ? "ok" : "not ok";
            output.WriteLine($"{status} {i + 1} - {r.Name}");

            if (!r.Passed)
            {
                // YAML diagnostic block per TAP 13. Two-space indent, "---" / "..." delimiters.
                output.WriteLine("  ---");
                output.WriteLine($"  duration_ms: {r.DurationMs}");
                if (r.Failures.Count > 0)
                {
                    output.WriteLine("  failures:");
                    foreach (var f in r.Failures)
                    {
                        // Quote the value to handle colons/special chars safely. TAP 13
                        // uses YAML 1.1, so escape embedded double-quotes and backslashes.
                        var escaped = f.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        output.WriteLine($"    - \"{escaped}\"");
                    }
                }
                output.WriteLine("  ...");
            }
        }
    }
}
```

- [ ] **Step 3: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 231 → 234 (+3 new passing tests).

---

## Task 3: JunitReporter

**Why:** JUnit XML is the other canonical CI format. Every major CI system parses it; `actions/upload-artifact` + JUnit viewers give per-PR test annotations.

**Files:**
- Modify: `src/Runner/Reporters/JunitReporter.cs` — replace stub with real impl
- Create: `tests/Runner.Tests/JunitReporterTests.cs`

**Dependencies:** Task 1.

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Tests/JunitReporterTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using SdvTestFramework.Runner.Reporters;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class JunitReporterTests
{
    private static XDocument RenderTo(IReadOnlyList<ScenarioReport> reports)
    {
        var sw = new StringWriter();
        new JunitReporter().Report(reports, sw);
        return XDocument.Parse(sw.ToString());
    }

    [Fact]
    public void Report_AllPass_ProducesWellFormedSuite()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport { Name = "alpha", Path = "tests/samples/alpha.test.json", Passed = true, DurationMs = 10 },
            new ScenarioReport { Name = "beta", Path = "tests/samples/beta.test.json", Passed = true, DurationMs = 20 },
        };
        var doc = RenderTo(reports);
        var root = doc.Root!;
        Assert.Equal("testsuites", root.Name.LocalName);
        Assert.Equal("2", root.Attribute("tests")!.Value);
        Assert.Equal("0", root.Attribute("failures")!.Value);
        Assert.Equal("0.030", root.Attribute("time")!.Value);

        var suite = root.Element("testsuite")!;
        Assert.Equal("sdv-test", suite.Attribute("name")!.Value);
        Assert.Equal("2", suite.Attribute("tests")!.Value);
        Assert.Equal("0", suite.Attribute("failures")!.Value);

        var cases = suite.Elements("testcase").ToList();
        Assert.Equal(2, cases.Count);
        Assert.Equal("alpha", cases[0].Attribute("name")!.Value);
        Assert.Equal("tests/samples/alpha.test.json", cases[0].Attribute("classname")!.Value);
        Assert.Equal("0.010", cases[0].Attribute("time")!.Value);
        Assert.Null(cases[0].Element("failure"));
    }

    [Fact]
    public void Report_WithFailure_EmitsFailureElement()
    {
        var reports = new List<ScenarioReport>
        {
            new ScenarioReport
            {
                Name = "broken",
                Path = "tests/samples/b.test.json",
                Passed = false,
                DurationMs = 123,
                Failures = new List<string>
                {
                    "step 'player.warp' failed: no location named: Nowhere",
                    "assertion: state.player.location == 'Farm'",
                },
            },
        };
        var doc = RenderTo(reports);
        var root = doc.Root!;
        Assert.Equal("1", root.Attribute("tests")!.Value);
        Assert.Equal("1", root.Attribute("failures")!.Value);

        var c = root.Element("testsuite")!.Element("testcase")!;
        Assert.Equal("0.123", c.Attribute("time")!.Value);
        var failure = c.Element("failure")!;
        Assert.Equal("assertion", failure.Attribute("type")!.Value);
        // message attr = first failure line (most CI UIs display only this).
        Assert.Equal("step 'player.warp' failed: no location named: Nowhere", failure.Attribute("message")!.Value);
        // Body = full list joined by \n.
        Assert.Contains("step 'player.warp' failed", failure.Value);
        Assert.Contains("assertion: state.player.location", failure.Value);
    }

    [Fact]
    public void Report_Empty_ProducesEmptySuite()
    {
        var doc = RenderTo(new List<ScenarioReport>());
        var root = doc.Root!;
        Assert.Equal("testsuites", root.Name.LocalName);
        Assert.Equal("0", root.Attribute("tests")!.Value);
    }
}
```

Add `using System.Linq;` if the test file doesn't pick it up.

Run: `dotnet test tests/Runner.Tests/ --filter JunitReporter`
Expected: FAIL — `JunitReporter.Report` throws `NotImplementedException`.

- [ ] **Step 2: Flesh out JunitReporter**

Replace `src/Runner/Reporters/JunitReporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// JUnit XML reporter producing Jenkins-compatible output (testsuites → testsuite →
/// testcase). Scenarios map to testcases; <see cref="ScenarioReport.Path"/> becomes the
/// <c>classname</c> attribute following the Jenkins convention of "classname = file path".
/// </summary>
/// <remarks>
/// Schema: https://llg.cubic.org/docs/junit/. Consumed by GitHub Actions, GitLab, Jenkins,
/// and most other CI test-result aggregators. Failure bodies carry all <see cref="ScenarioReport.Failures"/>
/// entries joined by newline; the <c>message</c> attribute carries just the first (most UIs
/// display only the message).
/// </remarks>
public sealed class JunitReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        int totalFailures = 0;
        int totalMs = 0;
        foreach (var r in reports)
        {
            if (!r.Passed) totalFailures++;
            totalMs += r.DurationMs;
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
        };

        using var w = XmlWriter.Create(output, settings);
        w.WriteStartDocument();

        w.WriteStartElement("testsuites");
        w.WriteAttributeString("tests", reports.Count.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("failures", totalFailures.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("errors", "0");
        w.WriteAttributeString("time", FormatSeconds(totalMs));

        w.WriteStartElement("testsuite");
        w.WriteAttributeString("name", "sdv-test");
        w.WriteAttributeString("tests", reports.Count.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("failures", totalFailures.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("errors", "0");
        w.WriteAttributeString("time", FormatSeconds(totalMs));
        w.WriteAttributeString("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        foreach (var r in reports)
        {
            w.WriteStartElement("testcase");
            w.WriteAttributeString("classname", r.Path);
            w.WriteAttributeString("name", r.Name);
            w.WriteAttributeString("time", FormatSeconds(r.DurationMs));

            if (!r.Passed)
            {
                w.WriteStartElement("failure");
                w.WriteAttributeString("type", "assertion");
                w.WriteAttributeString("message", r.Failures.Count > 0 ? r.Failures[0] : "assertion failed");
                w.WriteString(string.Join("\n", r.Failures));
                w.WriteEndElement();  // failure
            }

            w.WriteEndElement();  // testcase
        }

        w.WriteEndElement();  // testsuite
        w.WriteEndElement();  // testsuites
        w.WriteEndDocument();
    }

    /// <summary>Milliseconds → seconds, 3-decimal fixed, invariant culture. Matches Jenkins' parser.</summary>
    private static string FormatSeconds(int millis)
    {
        return (millis / 1000.0).ToString("F3", CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 3: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 234 → 237 (+3 new passing tests).

---

## Task 4: CLI flag wiring

**Why:** Plumb `--reporter` and `--output` through `RunCommand`. This is the task where the new reporters become user-reachable.

**Files:**
- Modify: `src/Runner/Commands/RunCommand.cs`
- Create: `tests/Runner.Tests/RunCommandReporterFlagTests.cs`

**Dependencies:** Tasks 1, 2, 3.

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Tests/RunCommandReporterFlagTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class RunCommandReporterFlagTests
{
    [Fact]
    public async Task Run_UnknownReporter_ReturnsTwo()
    {
        // Invalid reporter name → argument error → exit 2 before any scenario loading.
        var code = await RunCommand.RunAsync(
            new[] { "--reporter", "bogus", "/tmp/does-not-exist-dir" }.AsMemory(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Run_ReporterFlagAfterPathArgs_StillParsed()
    {
        // Flag can come anywhere in argv. Passing a nonexistent path forces exit 2 at
        // scenario-load time; the test just asserts no argument-parse crash.
        var code = await RunCommand.RunAsync(
            new[] { "/tmp/does-not-exist-dir", "--reporter", "tap" }.AsMemory(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Run_OutputPathUnwritable_ReturnsThree()
    {
        // /dev/full on Linux swallows writes then errors on close — but simpler to point
        // at a path whose parent directory doesn't exist: File.Create throws DirectoryNotFound.
        // We also need to dodge the scenario-loading check, so point at a valid empty dir.
        var emptyDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"empty-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(emptyDir);
        try
        {
            var code = await RunCommand.RunAsync(
                new[] { "--reporter", "tap", "--output", "/no/such/dir/out.tap", emptyDir }.AsMemory(),
                CancellationToken.None);
            // emptyDir has no *.test.json → scenarios.Count == 0 → "no scenarios matched" → exit 0
            // UNLESS we pre-validate the output path before the scenario check. If we do, we hit 3.
            // If we don't (write lazily at reporter-emit time and the reporter isn't even called
            // when scenarios is empty), we hit 0. Pick the stricter semantics: validate eagerly.
            Assert.Equal(3, code);
        }
        finally { System.IO.Directory.Delete(emptyDir, recursive: true); }
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter RunCommandReporterFlag`
Expected: FAIL — no `--reporter` / `--output` parsing yet; unknown reporter slides through and hits the no-scenarios-matched path.

- [ ] **Step 2: Parse the new flags**

In `src/Runner/Commands/RunCommand.cs`, find the arg-parsing block (currently around lines 21-31):

```csharp
        // ---- parse args ----
        var paths = new List<string>();
        string? filter = null;
        string? modsPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--filter" && i + 1 < args.Length) { filter = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            paths.Add(a);
        }
        if (paths.Count == 0) paths.Add(Directory.GetCurrentDirectory());
```

Replace with:

```csharp
        // ---- parse args ----
        var paths = new List<string>();
        string? filter = null;
        string? modsPath = null;
        string reporterName = "console";
        string? outputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--filter" && i + 1 < args.Length) { filter = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            if (a == "--reporter" && i + 1 < args.Length) { reporterName = args.Span[++i]; continue; }
            if (a == "--output" && i + 1 < args.Length) { outputPath = args.Span[++i]; continue; }
            paths.Add(a);
        }
        if (paths.Count == 0) paths.Add(Directory.GetCurrentDirectory());

        // ---- resolve reporter eagerly so bad flag values exit 2 before SDV launches ----
        SdvTestFramework.Runner.Reporters.IReporter reporter;
        try { reporter = SdvTestFramework.Runner.Reporters.ReporterFactory.Create(reporterName); }
        catch (ArgumentException ex) { Console.Error.WriteLine($"[reporter] {ex.Message}"); return 2; }

        // Resolve output sink. Stream writer is opened here (before SDV launches) so an
        // unwritable path fails fast with exit 3 — no waiting through a 60s SDV boot
        // only to discover the destination is bad.
        TextWriter? fileWriter = null;
        if (!string.IsNullOrEmpty(outputPath))
        {
            try { fileWriter = new StreamWriter(outputPath); }
            catch (Exception ex) { Console.Error.WriteLine($"[reporter] can't open --output '{outputPath}': {ex.Message}"); return 3; }
        }
```

- [ ] **Step 3: Replace the hardcoded ConsoleReporter call with the selected reporter**

In the same file, find the block from T1 Step 6:

```csharp
            // Output via the selected reporter. Console is the default.
            new SdvTestFramework.Runner.Reporters.ConsoleReporter().Report(collected, Console.Out);
```

Replace with:

```csharp
            // Output via the selected reporter. --reporter picks the format; --output
            // picks the sink (stdout if omitted).
            var writer = fileWriter ?? Console.Out;
            reporter.Report(collected, writer);
            writer.Flush();
```

Wrap the file writer's disposal. Just BEFORE the `catch (Exception ex)` block that ends the try, add:

```csharp
            fileWriter?.Dispose();
```

Actually cleanest approach: use `using var` on declaration. But `fileWriter` needs to exist before the try block for the catch-exit-3 path. Simpler: after everything completes (success or failure), ensure disposal happens in a `finally`:

Change the outer try block to ensure disposal. The existing `finally` at the end of RunAsync kills the SDV process; extend it to also dispose the writer:

```csharp
        finally
        {
            try { if (!sdv.HasExited) { sdv.Kill(); sdv.WaitForExit(5000); } } catch { }
            fileWriter?.Dispose();
        }
```

Note: `fileWriter` is declared outside the `try { ... using var sdv = ... }` block, so it's in scope for the `finally`.

- [ ] **Step 4: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 237 → 240 (+3 new passing tests: `Run_UnknownReporter_ReturnsTwo`, `Run_ReporterFlagAfterPathArgs_StillParsed`, `Run_OutputPathUnwritable_ReturnsThree`).

---

## Task 5: Smoke + docs

**Why:** Verify the reporters work live against real scenario output, update the CLI help text + milestone note.

**Files:**
- Modify: `src/Runner/Program.cs` — PrintHelp additions
- Modify: `docs/milestones/current.md` — M2-reporters subsection

**Dependencies:** Tasks 1-4.

- [ ] **Step 1: Update Program.cs PrintHelp**

Open `src/Runner/Program.cs`. Find the existing `run` command documentation block in `PrintHelp`:

```csharp
        w.WriteLine("  run [--filter <p>] [--mods-path <p>] [paths...]");
        w.WriteLine("                    Launch SDV, run scenarios, print summary.");
        w.WriteLine("                    --filter: case-insensitive substring on scenario name.");
        w.WriteLine("                    --mods-path: isolated mods dir for the harness to load from.");
        w.WriteLine("                                 Defaults to ~/.cache/sdv-test-framework/mods.");
```

Replace with:

```csharp
        w.WriteLine("  run [--filter <p>] [--mods-path <p>] [--reporter <c|tap|junit>] [--output <path>] [paths...]");
        w.WriteLine("                    Launch SDV, run scenarios, print summary.");
        w.WriteLine("                    --filter: case-insensitive substring on scenario name.");
        w.WriteLine("                    --mods-path: isolated mods dir for the harness to load from.");
        w.WriteLine("                                 Defaults to ~/.cache/sdv-test-framework/mods.");
        w.WriteLine("                    --reporter: output format. One of 'console' (default),");
        w.WriteLine("                                'tap' (TAP 13), 'junit' (Jenkins XML).");
        w.WriteLine("                    --output: write reporter output to this path. Defaults to stdout.");
```

- [ ] **Step 2: Live smoke — TAP**

```bash
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
rm -rf ~/.cache/sdv-test-framework-samples/mods
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
XVFB_PID=$!
trap "pkill -9 -f StardewModdingAPI 2>/dev/null; kill $XVFB_PID 2>/dev/null" EXIT

SAMPLES_MODS="$HOME/.cache/sdv-test-framework-samples/mods"
mkdir -p "$SAMPLES_MODS"
cp -r ~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness "$SAMPLES_MODS/" 2>/dev/null || \
    dotnet build -c Release >/dev/null && \
    cp -r ~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness "$SAMPLES_MODS/"
cp -r "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/Mods/ContentPatcher" "$SAMPLES_MODS/"
cp -r tests/sample-cp-mod "$SAMPLES_MODS/SdvTestFramework.SampleCpMod"

DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- \
    run tests/samples/ --mods-path "$SAMPLES_MODS" --reporter tap
```

Expected output: a TAP 13 block starting with `TAP version 13\n1..10\n` and 10 `ok` or `not ok` lines. All 10 should be `ok` (the sample suite passes). Any `not ok` → scenario regression; stop and diagnose.

- [ ] **Step 3: Live smoke — JUnit**

Run the same staging prelude if not already staged, then:

```bash
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- \
    run tests/samples/ --mods-path "$SAMPLES_MODS" --reporter junit --output /tmp/m2-reporters-smoke.xml

# Validate the XML
xmllint --noout /tmp/m2-reporters-smoke.xml && echo "XML well-formed"

# Spot-check shape
python3 -c "
import xml.etree.ElementTree as ET
t = ET.parse('/tmp/m2-reporters-smoke.xml').getroot()
assert t.tag == 'testsuites'
assert int(t.get('tests')) == 10
assert int(t.get('failures')) == 0
print('JUnit OK — tests={} failures={}'.format(t.get('tests'), t.get('failures')))"
```

Expected: `XML well-formed` + `JUnit OK — tests=10 failures=0`.

- [ ] **Step 4: Confirm run-samples.sh still 10/10**

```bash
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
./scripts/run-samples.sh
```

Expected: `[run] 10/10 passed` — console reporter still works end-to-end.

- [ ] **Step 5: Clean up smoke artifacts**

```bash
rm -f /tmp/m2-reporters-smoke.xml
```

- [ ] **Step 6: Update docs/milestones/current.md**

In `docs/milestones/current.md`, find the `## M2 — Production polish` section and the subproject list. Update subproject 3 from `TAP + JUnit reporters (§4.7) — deferred.` to:

```markdown
3. **TAP + JUnit reporters** (§4.7) — CI integration via `--reporter <console|tap|junit>`. ✓ **Landed 2026-04-23.**
```

After the existing `### M2 subproject 1 — Fixture builder landed` subsection, insert:

```markdown
### M2 subproject 2 — TAP + JUnit reporters landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-m2-reporters.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-m2-reporters-design.md`.

**Scope:** `sdv-test run` gained two new flags — `--reporter <console|tap|junit>` picks the output format, `--output <path>` picks the sink (stdout if omitted). Three new classes under `src/Runner/Reporters/` (`IReporter`, `ConsoleReporter`, `TapReporter`, `JunitReporter`, `ReporterFactory`). `ScenarioReport` gained a `Path` field so reporters know which scenario file produced the report (JUnit uses it as `classname`; Console appends it after the scenario name).

**Refactor:** the inline output loop in `RunCommand.cs` moved behind the `IReporter` interface. `ConsoleReporter` preserves the pre-M2 output byte-for-byte — the default user experience is unchanged.

**Formats:**
- **Console** (default) — Playwright-style summary, unchanged from M1.
- **TAP 13** — one-line-per-scenario with YAML diagnostics on failures. Widely accepted by CI aggregators.
- **JUnit XML** — Jenkins-compatible shape (`<testsuites><testsuite><testcase>`). Consumed by GitHub Actions, GitLab, Jenkins. `classname` = scenario file path, `name` = scenario name, `time` = seconds.

**Smoke result:** `sdv-test run --reporter tap tests/samples/` produced a valid TAP 13 block with 10 `ok` lines; `--reporter junit --output /tmp/x.xml` produced an `xmllint`-clean XML document with `tests="10" failures="0"`. `./scripts/run-samples.sh` still reports **10/10 passed**.

**Test count after M2-reporters:** ~240 Passed + 31 Skipped (was 229+31 before; +11 passed).

**TODOs for later work:**
- Coloured console output.
- Multiple reporters at once (`--reporter console --reporter junit`).
- Incremental/streaming output (emit per-scenario as they run, not at end).
- GitLab's native test-results XML schema (different from JUnit).
```

- [ ] **Step 7: Final CI**

Run: `./scripts/ci.sh`
Expected: PASS. Final test count ~240 Passed + 31 Skipped.

---

## Self-review

**1. Spec coverage:**
- Architecture — IReporter with 3 implementations → T1 (interface + ConsoleReporter refactor), T2 (Tap), T3 (Junit) ✓
- ScenarioReport.Path field → T1 step 1 ✓
- `--reporter` + `--output` CLI flags → T4 ✓
- Console output preserved byte-for-byte → T1 step 4 test explicitly asserts the existing shape ✓
- TAP 13 format (version header, plan, ok lines, YAML diag) → T2 ✓
- JUnit Jenkins-compatible XML (testsuites/testsuite/testcase, classname/name/time) → T3 ✓
- Error handling: unknown reporter exit 2, unwritable --output exit 3 → T4 step 2 ✓
- Unknown reporter message format `(known: console, tap, junit)` → T1 step 3 ReporterFactory throws ArgumentException with this exact phrasing ✓
- Empty scenario set handling (1..0 for TAP, tests="0" for JUnit) → T2 step 1 / T3 step 1 both test this ✓
- Smoke verification → T5 steps 2-4 ✓
- Docs — milestones/current.md → T5 step 6 ✓

**2. Placeholder scan:** no TBD / TODO / "implement later" in steps. The T1 stubs for TapReporter/JunitReporter are deliberately marked `NotImplementedException` and explicitly filled in by T2/T3 — not placeholders, they're phase gates.

**3. Type consistency:**
- `IReporter.Report(IReadOnlyList<ScenarioReport>, TextWriter)` — used identically in T1 (interface), T1 (ConsoleReporter), T2 (TapReporter), T3 (JunitReporter), T4 (RunCommand dispatch) ✓
- `ReporterFactory.Create(string)` — T1 defines; T4 calls ✓
- `ScenarioReport.Path` — T1 adds the field; T1/T2/T3 reporters read it (ConsoleReporter → trailing segment, JunitReporter → classname attr; TapReporter ignores it since TAP line format doesn't include file paths) ✓
- All three reporters use the same output method signature; no accidental drift.

**4. Hazard notes:**
- T4's `--output` writer is disposed in the outer `finally` block of RunCommand. If the writer is opened successfully but the scenario run throws before the reporter call, the writer still gets disposed cleanly. Tested indirectly by the `Run_OutputPathUnwritable_ReturnsThree` test (which exercises the open-fails path; dispose isn't exercised on that path but the code path is simple enough to trust).
- T3's XML output uses `XmlWriter` which handles escaping for us. Failure bodies with `<`, `>`, `&`, and quotes are safe.
- T5's smoke commands assume a Flatpak SDV install path matching earlier plans. If a contributor runs this on a different setup, they'll need to adapt — but `./scripts/run-samples.sh` already encapsulates the launch; T5 step 4 uses it, so at most T5 step 2-3's manual launches need path edits.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-23-m2-reporters.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task with two-stage review. Proven across D1.5 / D1.6 / D1.7 / M2-fixture-builder cycles.

**2. Inline Execution** — execute tasks in this session via `superpowers:executing-plans`, batch through with checkpoints.

**Which approach?**
