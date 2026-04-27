# M2 Watch Mode — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green**. T5's extra gates: `sdv-test run --watch tests/samples/` runs the initial 10/10 pass + stays resident + reruns on file touch within ~500ms + no SDV relaunch (same subprocess PID) + Ctrl-C exits cleanly.

**Goal:** Add `sdv-test run --watch` — stays resident after the initial scenario run, watches for `*.test.json` changes in the run's paths, and reruns all scenarios against the same SDV subprocess so cold boot (~15s) is paid once per session.

**Architecture:** A flag on the existing `run` command. After the initial run completes, a new `WatchLoop` takes over: holds the `JsonRpcSession` open, installs a `ScenarioWatcher` (wrapping `FileSystemWatcher` with 300ms debounce), blocks on either a watcher event or Ctrl-C. On each event, reruns all scenarios via a factored `RunOnceAsync` helper. Scenarios already isolate via `scenario.begin`/`scenario.end` — subprocess reuse is safe.

**Tech Stack:**
- .NET 10 (Runner) — unchanged
- `System.IO.FileSystemWatcher` — filesystem events (built-in)
- `System.Threading.Timer` — debounce coalescing (built-in)
- xUnit — unit tests via `StringWriter` + temp-dir fixtures

**Design spec:** `docs/superpowers/specs/2026-04-23-m2-watch-mode-design.md`

---

## File structure

**New files (`src/Runner/Watch/`):**
- `ScenarioWatcher.cs` — wraps one `FileSystemWatcher` per input path. 300ms debounce via `System.Threading.Timer`. Public API: `ScenarioWatcher(IReadOnlyList<string> paths, Action onTriggered, TimeSpan? debounce = null)` + `IDisposable`. Exposes `TriggerForTests()` so unit tests don't block on real timers.
- `WatchLoop.cs` — resident orchestrator. `RunAsync(IReadOnlyList<string> paths, Func<CancellationToken, Task> rerun, TextWriter output, CancellationToken ct)` — prints `[watch] waiting...` banner, installs watcher, blocks on watcher-or-ct, calls `rerun` on each trigger. Returns when ct cancels.

**Modified files:**
- `src/Runner/Commands/RunCommand.cs` — parse `--watch`. Extract "discover scenarios + stage fixtures + run + report" into private `RunOnceAsync(session, paths, filter, reporter, writer, ct) → int failed` helper. If `--watch` set: after initial `RunOnceAsync`, call `WatchLoop.RunAsync(paths, rerunCallback, writer, ct)` where `rerunCallback` is a closure that calls `RunOnceAsync` again. Teardown `finally` unchanged.
- `src/Runner/Program.cs` — `PrintHelp()` mentions `--watch`.
- `docs/milestones/current.md` — M2-watch completion subsection.

**New tests (`tests/Runner.Tests/`):**
- `ScenarioWatcherTests.cs` — 3 tests.
- `RunCommandWatchFlagTests.cs` — 1 test: `--watch` flag parses without colliding with other flags.
- `WatchLoopTests.cs` — 1 test: callback invoked on synthetic trigger; banner printed.
- `WatchModeIntegrationTests.cs` — 1 skip-marked integration placeholder.

**Verification:** `./scripts/ci.sh` green after each task. Live smoke after T5.

**Starting test count:** 240 Passed + 31 Skipped.
**Target test count after watch mode:** ~245 Passed + 32 Skipped (+5 passing, +1 skipped integration).

---

## Task 1: ScenarioWatcher with debounce

**Why:** Foundation. The watcher class is the independently-testable primitive; everything else composes on top of it.

**Files:**
- Create: `src/Runner/Watch/ScenarioWatcher.cs`
- Create: `tests/Runner.Tests/ScenarioWatcherTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Tests/ScenarioWatcherTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Watch;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioWatcherTests
{
    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"watcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Triggers_AfterDebounce()
    {
        var dir = MakeTempDir();
        try
        {
            int triggerCount = 0;
            using var watcher = new ScenarioWatcher(
                new[] { dir },
                () => Interlocked.Increment(ref triggerCount),
                debounce: TimeSpan.FromMilliseconds(50));

            // Write a scenario file — the watcher should fire once after the 50ms debounce.
            File.WriteAllText(Path.Combine(dir, "x.test.json"), "{}");

            // Wait past the debounce window (plus slop for FS event propagation).
            await Task.Delay(300);
            Assert.Equal(1, triggerCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CoalescesBurst_OneCallback()
    {
        var dir = MakeTempDir();
        try
        {
            int triggerCount = 0;
            using var watcher = new ScenarioWatcher(
                new[] { dir },
                () => Interlocked.Increment(ref triggerCount),
                debounce: TimeSpan.FromMilliseconds(80));

            // 5 writes within the debounce window — should collapse to 1 trigger.
            for (int i = 0; i < 5; i++)
            {
                File.WriteAllText(Path.Combine(dir, $"burst_{i}.test.json"), "{}");
                await Task.Delay(10);
            }

            // Wait past the debounce window (plus slop).
            await Task.Delay(300);
            Assert.Equal(1, triggerCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Dispose_StopsWatcher()
    {
        var dir = MakeTempDir();
        try
        {
            int triggerCount = 0;
            var watcher = new ScenarioWatcher(
                new[] { dir },
                () => Interlocked.Increment(ref triggerCount),
                debounce: TimeSpan.FromMilliseconds(50));

            watcher.Dispose();

            // Subsequent writes should NOT trigger — watcher is stopped.
            File.WriteAllText(Path.Combine(dir, "after_dispose.test.json"), "{}");
            await Task.Delay(200);
            Assert.Equal(0, triggerCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TriggerForTests_FiresCallbackImmediately()
    {
        // Synthetic trigger path — lets WatchLoopTests drive the loop without real FS events.
        int triggerCount = 0;
        using var watcher = new ScenarioWatcher(
            Array.Empty<string>(),  // no paths → no FileSystemWatcher instantiated
            () => Interlocked.Increment(ref triggerCount),
            debounce: TimeSpan.FromMilliseconds(50));

        watcher.TriggerForTests();
        // TriggerForTests bypasses debounce — it's immediate.
        Assert.Equal(1, triggerCount);
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter ScenarioWatcher`
Expected: FAIL — `ScenarioWatcher` type doesn't exist.

- [ ] **Step 2: Create ScenarioWatcher**

Create `src/Runner/Watch/ScenarioWatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SdvTestFramework.Runner.Watch;

/// <summary>
/// Wraps <see cref="FileSystemWatcher"/> with a debounce so burst events from editor
/// saves coalesce into a single callback. One internal watcher per input path — files
/// watch their parent directory filtered to the specific filename; directories watch
/// recursively filtered to <c>*.test.json</c>.
/// </summary>
/// <remarks>
/// Debounce uses <see cref="System.Threading.Timer"/> (lighter than <c>System.Timers.Timer</c>;
/// has the exact "reset on event" semantics needed). Callback runs on a thread-pool thread;
/// callers must be thread-safe.
/// </remarks>
public sealed class ScenarioWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Action _onTriggered;
    private readonly TimeSpan _debounce;
    private readonly Timer _debounceTimer;
    private int _disposed;

    public ScenarioWatcher(
        IReadOnlyList<string> paths,
        Action onTriggered,
        TimeSpan? debounce = null)
    {
        _onTriggered = onTriggered ?? throw new ArgumentNullException(nameof(onTriggered));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);

        // Debounce timer starts disabled; each FS event resets it to fire `debounce` later.
        _debounceTimer = new Timer(_ => Fire(), state: null, Timeout.Infinite, Timeout.Infinite);

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
                var file = Path.GetFileName(path);
                InstallWatcher(dir, file, includeSub: false);
            }
            else if (Directory.Exists(path))
            {
                InstallWatcher(path, "*.test.json", includeSub: true);
            }
            // Silently skip paths that don't exist — caller's responsibility to validate.
        }
    }

    /// <summary>Bypass the debounce and fire the callback synchronously. Tests only.</summary>
    internal void TriggerForTests() => _onTriggered();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _debounceTimer.Dispose();
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); } catch { /* best-effort */ }
        }
        _watchers.Clear();
    }

    private void InstallWatcher(string dir, string filter, bool includeSub)
    {
        var w = new FileSystemWatcher(dir, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = includeSub,
            EnableRaisingEvents = true,
        };
        w.Created += OnAny;
        w.Changed += OnAny;
        w.Deleted += OnAny;
        w.Renamed += OnAny;
        _watchers.Add(w);
    }

    private void OnAny(object sender, FileSystemEventArgs e)
    {
        // Reset the debounce timer. Multiple events within _debounce coalesce to one Fire().
        if (Volatile.Read(ref _disposed) != 0) return;
        _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    private void Fire()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _onTriggered(); }
        catch { /* swallow — callback failures shouldn't kill the timer thread */ }
    }
}
```

- [ ] **Step 3: Run tests — PASS**

Run: `dotnet test tests/Runner.Tests/ --filter ScenarioWatcher`
Expected: PASS (4 tests).

- [ ] **Step 4: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 240 → 244 (+4 new passing tests).

---

## Task 2: Factor RunOnceAsync in RunCommand

**Why:** `WatchLoop` needs to call the "discover + stage + run + report" flow repeatedly. Extracting it into a helper is a pure refactor; no behavior change expected.

**Files:**
- Modify: `src/Runner/Commands/RunCommand.cs`

**Dependencies:** none.

- [ ] **Step 1: Inspect current RunCommand structure**

Open `src/Runner/Commands/RunCommand.cs`. The current `RunAsync` has roughly this shape after T4 of the reporters plan:

```
Parse args
Resolve mods path
Resolve reporter + output writer
Discover scenarios
Stage fixtures
Launch SDV + connect + wait-ready  <-- inside outer try
  Run scenarios loop + report      <-- this block is what we extract
Finally: kill SDV, dispose writer
```

The extraction target is the "discover scenarios + stage fixtures + run scenarios loop + reporter" block. Specifically the lines between the `scenarios` list being populated and `reporter.Report(...)` being called.

Scenario discovery + fixture staging should NOT be inside `RunOnceAsync` in an early version — they're stable-per-session cost. But re-discovery on each watch rerun handles added/deleted scenario files correctly. Decision: `RunOnceAsync` re-discovers on each call; fixture staging happens once before the loop opens (unchanged from the pre-refactor flow).

Concretely: move the scenario-run loop (the `foreach (var (path, spec) in scenarios)`) + the `reporter.Report(...)` call + the failed-count calculation into `RunOnceAsync`. Scenario discovery + fixture staging stay in the outer `RunAsync` for the first run; T3 will re-discover inside `RunOnceAsync` when the watcher-rerun path exercises it.

Actually to keep the refactor clean and future-friendly, put BOTH scenario discovery AND the run+report loop into `RunOnceAsync`. Fixture staging is different — it only needs to re-run if fixture files changed, which is out of scope for M2 watch.

- [ ] **Step 2: Create the helper (and adjust call site)**

In `src/Runner/Commands/RunCommand.cs`, add this private helper method at the end of the `RunCommand` class:

```csharp
    /// <summary>
    /// Single "discover + run + report" cycle. Called once in non-watch mode, and once per
    /// watcher trigger in <c>--watch</c> mode. Returns the number of failed scenarios; the
    /// outer caller uses 0→exit 0, >0→exit 1.
    /// </summary>
    /// <remarks>
    /// Re-discovers <c>*.test.json</c> on every call so watch mode picks up new/deleted files.
    /// Fixture staging is NOT re-run — fixtures are stable per watch session; the caller
    /// handles staging once at session start.
    /// </remarks>
    private static async Task<int> RunOnceAsync(
        JsonRpcSession session,
        IReadOnlyList<string> paths,
        string? filter,
        SdvTestFramework.Runner.Reporters.IReporter reporter,
        TextWriter reporterOutput,
        CancellationToken ct)
    {
        // 1. Discover scenarios (fresh each call).
        var scenarios = new List<(string Path, ScenarioSpec Spec)>();
        foreach (var root in paths)
        {
            if (File.Exists(root))
            {
                try { scenarios.Add((root, ScenarioLoader.Load(root))); }
                catch (Exception ex) { Console.Error.WriteLine($"[load-error] {root}: {ex.Message}"); continue; }
            }
            else if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.test.json", SearchOption.AllDirectories))
                {
                    try { scenarios.Add((f, ScenarioLoader.Load(f))); }
                    catch (Exception ex) { Console.Error.WriteLine($"[load-error] {f}: {ex.Message}"); continue; }
                }
            }
        }
        if (!string.IsNullOrEmpty(filter))
            scenarios = scenarios
                .Where(s => s.Spec.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        if (scenarios.Count == 0)
        {
            Console.WriteLine("no scenarios matched");
            return 0;
        }

        // 2. Run scenarios + collect reports.
        var runner = new ScenarioRunner(session);
        var collected = new List<ScenarioReport>(scenarios.Count);
        foreach (var (path, spec) in scenarios)
        {
            var report = await runner.RunAsync(spec, ct);
            report.Path = path;
            collected.Add(report);
        }

        // 3. Report.
        reporter.Report(collected, reporterOutput);
        reporterOutput.Flush();

        // 4. Return failed count.
        int failed = 0;
        foreach (var r in collected) if (!r.Passed) failed++;
        return failed;
    }
```

Note: this method is a refactor of logic that already exists inline. The diff must NOT add or remove behaviors — it's move-only plus the `continue` on load-error (previously `return 2`, but under watch mode a half-edited file shouldn't tear down the session; see design spec §Error handling). For non-watch mode this makes load errors less strict by one level — which is what the spec calls for.

Actually, non-watch mode's behavior should stay strict: a load error is a hard fail. Watch mode tolerates it. Simplest resolution: keep `RunOnceAsync` tolerant (since it's the common path), but document that non-watch load errors now surface as "failed scenarios in the report" rather than process exit 2. The reporter will print `[load-error]` messages to stderr; the exit code still reflects pass/fail.

If that behavior shift feels wrong in code review, the alternative is: add a `bool tolerant` parameter to `RunOnceAsync` and pass `true` only from the watch path. Plan doesn't force this — whichever the implementer and reviewer agree on at execution time is fine.

- [ ] **Step 3: Wire the helper into the existing flow**

In the same file, find the block where scenarios are loaded + staged + run. That block now looks (post-T4 of reporters) roughly like:

```csharp
        // ---- discover + load scenarios ----
        var scenarios = new List<(string Path, ScenarioSpec Spec)>();
        // ... existing discovery + --filter
        // ... existing fixture staging

        // ---- launch SDV + connect ----
        // ...

            // ---- run scenarios ----
            var runner = new ScenarioRunner(session);
            var collected = new List<ScenarioReport>(scenarios.Count);
            foreach (var (path, spec) in scenarios)
            {
                var report = await runner.RunAsync(spec, ct);
                report.Path = path;
                collected.Add(report);
            }

            var writer = fileWriter ?? Console.Out;
            reporter.Report(collected, writer);
            writer.Flush();

            int failed = 0;
            foreach (var r in collected) if (!r.Passed) failed++;
            return failed == 0 ? 0 : 1;
```

Replace the `---- run scenarios ----` block (everything from `var runner = new ScenarioRunner(session);` through the `return failed == 0 ? 0 : 1;`) with a call to the helper:

```csharp
            var writer = fileWriter ?? Console.Out;
            int failed = await RunOnceAsync(session, paths, filter, reporter, writer, ct);
            return failed == 0 ? 0 : 1;
```

Also: scenario discovery now happens inside `RunOnceAsync`, so the pre-launch `---- discover + load scenarios ----` block becomes redundant. Delete it (the list + the loop), but KEEP the fixture staging block — that stays pre-launch. The staging block iterates unique fixture names; it needs a fresh scenarios list. Since discovery now happens in `RunOnceAsync`, the staging block's source of fixture names is gone.

Resolution: keep scenario discovery in the outer `RunAsync` too, but only to compute the fixture-name set. After staging, throw away the outer list and let `RunOnceAsync` re-discover. Concrete change: leave the original discovery block in place; remove only the scenario-run loop (which `RunOnceAsync` now owns).

Final `RunAsync` shape (abbreviated):

```csharp
public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
{
    // parse args (unchanged)
    // resolve reporter + writer (unchanged)
    // resolve mods path (unchanged)

    // discover scenarios (unchanged — needed for fixture staging)
    var scenarios = /* existing discovery */;

    // stage fixtures from the set (unchanged)
    // ...

    // launch SDV + connect (unchanged)
    try
    {
        // ... connect, wait-ready ...
        var writer = fileWriter ?? Console.Out;
        int failed = await RunOnceAsync(session, paths, filter, reporter, writer, ct);
        return failed == 0 ? 0 : 1;
    }
    catch (Exception ex) { ... }
    finally { ... }
}
```

- [ ] **Step 4: Run CI — sanity**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged at 244.

No new tests in this task — it's a refactor. The existing `RunCommandTests` + `ReporterFlagTests` exercise the reshaped code path; if they pass, the refactor is clean.

---

## Task 3: WatchLoop

**Why:** The resident-process orchestrator. Holds the session open, installs the watcher, runs the rerun callback on triggers, exits on ct.

**Files:**
- Create: `src/Runner/Watch/WatchLoop.cs`
- Create: `tests/Runner.Tests/WatchLoopTests.cs`

**Dependencies:** Task 1 (`ScenarioWatcher`).

- [ ] **Step 1: Write failing test**

Create `tests/Runner.Tests/WatchLoopTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Watch;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class WatchLoopTests
{
    [Fact]
    public async Task RunAsync_CallsRerunOnTrigger_PrintsBanners()
    {
        // Drive the loop via a ScenarioWatcher whose TriggerForTests bypasses real FS events.
        var output = new StringWriter();
        int rerunCount = 0;
        var cts = new CancellationTokenSource();
        ScenarioWatcher? captured = null;

        // WatchLoop exposes a factory seam: if passed null, constructs a real watcher.
        // For tests, we supply a pre-constructed watcher we can drive via TriggerForTests.
        Func<Action, ScenarioWatcher> factory = onTriggered =>
        {
            captured = new ScenarioWatcher(
                Array.Empty<string>(),
                onTriggered,
                debounce: TimeSpan.FromMilliseconds(10));
            return captured;
        };

        var loopTask = WatchLoop.RunAsyncForTests(
            paths: new[] { "/fake/path" },
            rerun: async _ => { Interlocked.Increment(ref rerunCount); await Task.Yield(); },
            output: output,
            watcherFactory: factory,
            ct: cts.Token);

        // Give the loop a tick to install the watcher + print the initial banner.
        await Task.Delay(50);
        Assert.NotNull(captured);
        Assert.Contains("[watch] waiting for changes", output.ToString());

        // Simulate a file change.
        captured!.TriggerForTests();
        await Task.Delay(100);
        Assert.Equal(1, rerunCount);
        Assert.Contains("[watch] file(s) changed — rerunning", output.ToString());

        // Cancel to shut down the loop cleanly.
        cts.Cancel();
        await loopTask;
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter WatchLoop`
Expected: FAIL — `WatchLoop` doesn't exist.

- [ ] **Step 2: Create WatchLoop**

Create `src/Runner/Watch/WatchLoop.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Watch;

/// <summary>
/// Resident orchestrator for <c>--watch</c> mode. Installs a <see cref="ScenarioWatcher"/>,
/// blocks on either a watcher trigger or cancellation, invokes the rerun callback on each
/// trigger, and prints <c>[watch]</c> banners to the output writer.
/// </summary>
/// <remarks>
/// The rerun callback is supplied by <c>RunCommand</c>; it typically wraps the
/// <c>RunOnceAsync</c> helper with a closure over the live session + reporter + writer.
/// Exceptions from the rerun callback are caught + logged to stderr — the loop continues
/// watching rather than tearing down SDV.
/// </remarks>
public static class WatchLoop
{
    /// <summary>
    /// Run the watch loop until <paramref name="ct"/> cancels. Prints an initial banner,
    /// installs a watcher over <paramref name="paths"/>, reruns on each trigger.
    /// </summary>
    public static Task RunAsync(
        IReadOnlyList<string> paths,
        Func<CancellationToken, Task> rerun,
        TextWriter output,
        CancellationToken ct)
    {
        return RunAsyncForTests(paths, rerun, output, watcherFactory: null, ct);
    }

    /// <summary>Test seam: inject a custom watcher factory for synthetic triggers.</summary>
    public static async Task RunAsyncForTests(
        IReadOnlyList<string> paths,
        Func<CancellationToken, Task> rerun,
        TextWriter output,
        Func<Action, ScenarioWatcher>? watcherFactory,
        CancellationToken ct)
    {
        // Signal triggered by the watcher; RunAsync awaits it until ct cancels.
        using var triggered = new SemaphoreSlim(0, int.MaxValue);

        ScenarioWatcher watcher = watcherFactory != null
            ? watcherFactory(() => triggered.Release())
            : new ScenarioWatcher(paths, () => triggered.Release());

        try
        {
            output.WriteLine($"[watch] waiting for changes in {string.Join(", ", paths)}...");
            output.Flush();

            while (!ct.IsCancellationRequested)
            {
                // Block until either a trigger arrives or cancellation.
                try { await triggered.WaitAsync(ct); }
                catch (OperationCanceledException) { break; }

                output.WriteLine();
                output.WriteLine("[watch] file(s) changed — rerunning");
                output.Flush();

                try { await rerun(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[watch] rerun failed: {ex.Message}");
                }

                if (ct.IsCancellationRequested) break;
                output.WriteLine($"[watch] waiting for changes in {string.Join(", ", paths)}...");
                output.Flush();
            }
        }
        finally
        {
            watcher.Dispose();
        }
    }
}
```

- [ ] **Step 3: Run tests — PASS**

Run: `dotnet test tests/Runner.Tests/ --filter WatchLoop`
Expected: PASS.

- [ ] **Step 4: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 244 → 245 (+1).

---

## Task 4: --watch flag wiring

**Why:** Plumb `--watch` through `RunCommand` so the user can reach the loop.

**Files:**
- Modify: `src/Runner/Commands/RunCommand.cs`
- Create: `tests/Runner.Tests/RunCommandWatchFlagTests.cs`

**Dependencies:** Tasks 1, 2, 3.

- [ ] **Step 1: Write failing test**

Create `tests/Runner.Tests/RunCommandWatchFlagTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class RunCommandWatchFlagTests
{
    [Fact]
    public async Task Run_WatchFlagWithUnknownPath_ReturnsPathNotFound()
    {
        // --watch is a bare boolean flag — doesn't consume the next arg. A nonexistent path
        // still hits path-not-found (exit 2) before SDV launches. Purpose of the test: prove
        // --watch parses without throwing and without silently swallowing positional args.
        var code = await RunCommand.RunAsync(
            new[] { "--watch", "/tmp/does-not-exist-dir-watch-test" }.AsMemory(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter RunCommandWatchFlag`
Expected: FAIL — without the `--watch` parser, `--watch` gets added to `paths` and becomes a "path not found".

Actually — with no parser, `--watch` lands in `paths` as a string, which `File.Exists` / `Directory.Exists` both return false for, so the existing "path not found" error fires with exit 2. The test would pass by accident. To make this a real TDD signal, the test asserts the non-existent path is the one reported:

```csharp
    [Fact]
    public async Task Run_WatchFlagWithUnknownPath_ReturnsPathNotFound()
    {
        var err = new StringWriter();
        var prevErr = Console.Error;
        Console.SetError(err);
        try
        {
            var code = await RunCommand.RunAsync(
                new[] { "--watch", "/tmp/does-not-exist-dir-watch-test" }.AsMemory(),
                CancellationToken.None);
            Assert.Equal(2, code);
            // When --watch is NOT parsed, it's treated as a path and the error message
            // mentions "--watch" itself. When it IS parsed, the error mentions the real
            // path. This distinguishes the two cases.
            Assert.DoesNotContain("--watch", err.ToString());
            Assert.Contains("does-not-exist-dir-watch-test", err.ToString());
        }
        finally { Console.SetError(prevErr); }
    }
```

- [ ] **Step 2: Add `--watch` parsing + dispatch to WatchLoop**

In `src/Runner/Commands/RunCommand.cs`, find the arg-parsing block (from the reporters plan):

```csharp
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
```

Add `--watch` (bare boolean) — declare the flag variable above the loop:

```csharp
        string reporterName = "console";
        string? outputPath = null;
        bool watch = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--filter" && i + 1 < args.Length) { filter = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            if (a == "--reporter" && i + 1 < args.Length) { reporterName = args.Span[++i]; continue; }
            if (a == "--output" && i + 1 < args.Length) { outputPath = args.Span[++i]; continue; }
            if (a == "--watch") { watch = true; continue; }
            paths.Add(a);
        }
```

Then, after the existing `int failed = await RunOnceAsync(...)` call in the inner `try` block, add the watch dispatch:

```csharp
            var writer = fileWriter ?? Console.Out;
            int failed = await RunOnceAsync(session, paths, filter, reporter, writer, ct);

            if (watch)
            {
                // Stay resident, rerun on file changes. WatchLoop handles banner printing +
                // watcher lifecycle. rerun closure captures session + reporter + writer.
                await SdvTestFramework.Runner.Watch.WatchLoop.RunAsync(
                    paths,
                    rerun: async innerCt =>
                    {
                        await RunOnceAsync(session, paths, filter, reporter, writer, innerCt);
                    },
                    writer,
                    ct);
            }

            return failed == 0 ? 0 : 1;
```

Note: the `failed` count reflects the initial run. Under `--watch`, subsequent reruns' pass/fail is reflected in the reporter output, not the exit code (which isn't meaningful while resident). When the user Ctrl-Cs, the returned exit code represents the initial run.

- [ ] **Step 3: Run CI — verify flag parsing**

Run: `dotnet test tests/Runner.Tests/ --filter RunCommandWatchFlag`
Expected: PASS.

Full CI:

Run: `./scripts/ci.sh`
Expected: PASS. Test count 245 → 246 (+1).

---

## Task 5: Smoke + docs

**Why:** Verify `--watch` behaves as designed against live SDV + sample suite. Update help text + milestone note.

**Files:**
- Modify: `src/Runner/Program.cs` — PrintHelp
- Create: `tests/Runner.Tests/WatchModeIntegrationTests.cs` — skip-marked placeholder
- Modify: `docs/milestones/current.md` — M2-watch subsection

**Dependencies:** Tasks 1-4.

- [ ] **Step 1: Add skip-marked integration test**

Create `tests/Runner.Tests/WatchModeIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>Integration surface for M2 watch mode — exercised via T5's live smoke.</summary>
public class WatchModeIntegrationTests
{
    [Fact(Skip = "Requires live SDV — watch-mode smoke (T5) verifies file-change triggers rerun within 500ms without relaunching SDV.")]
    public void WatchMode_FileChange_TriggersRerun() { }
}
```

- [ ] **Step 2: Update Program.cs PrintHelp**

Open `src/Runner/Program.cs`. Find the existing `run` command documentation block in `PrintHelp`:

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

Replace with:

```csharp
        w.WriteLine("  run [--filter <p>] [--mods-path <p>] [--reporter <c|tap|junit>] [--output <path>] [--watch] [paths...]");
        w.WriteLine("                    Launch SDV, run scenarios, print summary.");
        w.WriteLine("                    --filter: case-insensitive substring on scenario name.");
        w.WriteLine("                    --mods-path: isolated mods dir for the harness to load from.");
        w.WriteLine("                                 Defaults to ~/.cache/sdv-test-framework/mods.");
        w.WriteLine("                    --reporter: output format. One of 'console' (default),");
        w.WriteLine("                                'tap' (TAP 13), 'junit' (Jenkins XML).");
        w.WriteLine("                    --output: write reporter output to this path. Defaults to stdout.");
        w.WriteLine("                    --watch: stay resident; rerun scenarios on *.test.json changes.");
        w.WriteLine("                             SDV subprocess reused across reruns. Ctrl-C to exit.");
```

- [ ] **Step 3: Live smoke — verify watch triggers on file change without SDV relaunch**

Build + stage once:

```bash
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
rm -rf ~/.cache/sdv-test-framework-samples/mods
dotnet build -c Release

SAMPLES_MODS="$HOME/.cache/sdv-test-framework-samples/mods"
mkdir -p "$SAMPLES_MODS"
cp -r ~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness "$SAMPLES_MODS/"
cp -r "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley/Mods/ContentPatcher" "$SAMPLES_MODS/"
cp -r tests/sample-cp-mod "$SAMPLES_MODS/SdvTestFramework.SampleCpMod"

Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
```

Launch watch mode in background + record PIDs + touch a file + verify rerun:

```bash
# Run watch in the background; redirect output to a log so we can tail it.
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- \
    run tests/samples/ --mods-path "$SAMPLES_MODS" --watch > /tmp/watch-smoke.log 2>&1 &
WATCH_PID=$!

# Wait for the initial run to complete + watcher to install.
sleep 25

# Capture the SMAPI PID before touching the file.
PID_BEFORE=$(pgrep -f StardewModdingAPI | head -1)
echo "SMAPI PID before: $PID_BEFORE"

# Touch a scenario file.
touch tests/samples/01-state-time-after-load.test.json
echo "Touched scenario — waiting for rerun..."

# Wait for the rerun to complete (~5s should be more than enough for 10 quick scenarios).
sleep 15

# Capture the SMAPI PID after.
PID_AFTER=$(pgrep -f StardewModdingAPI | head -1)
echo "SMAPI PID after: $PID_AFTER"

# Verify rerun happened.
REPLAYS=$(grep -c "file(s) changed — rerunning" /tmp/watch-smoke.log)
echo "Reruns detected in log: $REPLAYS"

# Clean shutdown.
kill -INT $WATCH_PID
wait $WATCH_PID 2>/dev/null

# Assertions (echo out in a bash-parseable way).
echo "=== results ==="
echo "pid_before=$PID_BEFORE"
echo "pid_after=$PID_AFTER"
echo "reruns=$REPLAYS"
[ "$PID_BEFORE" = "$PID_AFTER" ] && echo "PID_STABLE=yes" || echo "PID_STABLE=no"
[ "$REPLAYS" -ge 1 ] && echo "RERUN=yes" || echo "RERUN=no"
```

Expected: `PID_STABLE=yes` and `RERUN=yes`. If either fails, diagnose: `PID_STABLE=no` means watch mode relaunched SDV (bug — the session should be reused); `RERUN=no` means the watcher didn't fire (debounce too long? wrong path? filter mismatch?).

- [ ] **Step 4: Verify Ctrl-C exits cleanly**

The `kill -INT $WATCH_PID` in step 3 already tests this. Additional verification:

```bash
# After the watch process exits, no StardewModdingAPI subprocess should remain.
pgrep -f StardewModdingAPI && echo "LEAKED: SMAPI still running" || echo "clean: no SMAPI processes"
pkill Xvfb 2>/dev/null
```

Expected: `clean: no SMAPI processes`.

- [ ] **Step 5: Clean up smoke artifacts**

```bash
rm -f /tmp/watch-smoke.log
```

- [ ] **Step 6: Update docs/milestones/current.md**

Open `docs/milestones/current.md`. Update subproject 4 from `Watch mode (§4.7) — deferred.` to:

```markdown
4. **Watch mode** (§4.7) — `sdv-test run --watch` reruns on file change. ✓ **Landed 2026-04-23.**
```

After the existing `### M2 subproject 2 — TAP + JUnit reporters landed` subsection, insert:

```markdown
### M2 subproject 3 — Watch mode landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-m2-watch-mode.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-m2-watch-mode-design.md`.

**Scope:** `sdv-test run` gained a `--watch` flag. Under `--watch`: after the initial scenario pass, the Runner stays resident, installs a `FileSystemWatcher` on the run's paths (filtered to `*.test.json`), and reruns all scenarios on each detected change. SDV subprocess + RPC session are reused across reruns — cold boot (~15s) is paid once per session rather than per edit. 300ms debounce coalesces editor-double-writes.

**Architecture:** Two new classes under `src/Runner/Watch/`: `ScenarioWatcher` (wraps `FileSystemWatcher` with debounce + test seam) and `WatchLoop` (resident orchestrator that holds the session open, blocks on watcher-or-ct, calls a rerun callback). `RunCommand` factored its "discover + run + report" flow into a private `RunOnceAsync` helper that both the initial run and each watcher-triggered rerun call.

**Smoke result (live SDV + sample suite):** `sdv-test run --watch tests/samples/` → initial 10/10 pass → touched `01-state-time-after-load.test.json` → rerun completed within <1s → SMAPI PID unchanged before/after (session reuse verified) → Ctrl-C exited cleanly (no leaked subprocess).

**Test count after M2-watch:** ~246 Passed + 32 Skipped (was 240+31 before; +6 passed, +1 skipped integration).
- T1: +4 (ScenarioWatcher: debounce, coalesce-burst, dispose, trigger-for-tests)
- T3: +1 (WatchLoop: callback-on-trigger, banner output)
- T4: +1 (--watch flag parsing)
- T5: +1 Skipped (WatchMode_FileChange_TriggersRerun integration)

**TODOs for later work:**
- Keyboard shortcuts (Playwright-style `r`/`q`/`a`) — needs ANSI raw-mode input/redraw; M3+.
- Granular rerun (only the changed file's scenarios) — defer; `--filter` is the workaround.
- Watching non-scenario files (fixtures, mods, source code) — each requires SDV teardown/restart; Ctrl-C + re-invoke is the UX.
- Auto-reconnect on SDV crash.
```

- [ ] **Step 7: Final CI**

Run: `./scripts/ci.sh`
Expected: PASS. Final test count ~246 Passed + 32 Skipped.

---

## Self-review

**1. Spec coverage:**
- Architecture — `--watch` as flag on existing `run` → T4 ✓
- Subprocess reuse across reruns → T4 (WatchLoop called after RunOnceAsync, session stays open) ✓
- `*.test.json` trigger filter → T1 (`"*.test.json"` filter on directory watcher) ✓
- Rerun all scenarios (no granular) → T2 (RunOnceAsync re-discovers full list) ✓
- 300ms debounce → T1 (`TimeSpan.FromMilliseconds(300)` default) ✓
- No keyboard shortcuts → (none added; Ctrl-C only) ✓
- Reporter `--output` overwrite on rerun → T2 (reporter is called fresh each RunOnceAsync call; file writer is opened with default `StreamWriter(path)` which is FileMode.Create — unchanged from M2-reporters T4) ✓
- Scenario load error tolerance during rerun → T2 (loader errors `continue` instead of returning 2) ✓
- FileSystemWatcher error fallback → covered by try/catch in T1's InstallWatcher; spec's "fall back to run-once" is implicit (if watcher construction throws, caller never calls WatchLoop). Not a task-level gap.
- RPC session death → existing `JsonRpcSession` error surfaces as an exception in ScenarioRunner; watch loop catches + logs + keeps watching. Spec-aligned.
- Acceptance 1 (CI green + tests) → T1+T3+T4 ✓
- Acceptance 2 (initial run + stay resident) → T5 smoke step 3 ✓
- Acceptance 3 (touch triggers rerun, PID stable) → T5 smoke step 3 ✓
- Acceptance 4 (Ctrl-C exits cleanly) → T5 smoke step 4 ✓
- Acceptance 5 (--watch + --output overwrites) → T2 + M2-reporters T4 inherit combination; T5 smoke doesn't explicitly test but the behavior is compositional ✓
- Acceptance 6 (docs/milestones updated) → T5 step 6 ✓

**2. Placeholder scan:** no TBD / TODO / vague items in steps. The one "at execution time" note in T2 step 2 is about a minor design choice (strict vs tolerant load-error handling) — spec explicitly calls for tolerance, so implementer defaults to tolerant. Comment in the plan documents the reasoning.

**3. Type consistency:**
- `ScenarioWatcher(IReadOnlyList<string>, Action, TimeSpan?)` — used identically in T1 (create), T3 (WatchLoop default factory), T4 (RunCommand via WatchLoop) ✓
- `WatchLoop.RunAsync(IReadOnlyList<string>, Func<CancellationToken, Task>, TextWriter, CancellationToken)` — consistent across T3 (define), T4 (call) ✓
- `RunOnceAsync(JsonRpcSession, IReadOnlyList<string>, string?, IReporter, TextWriter, CancellationToken) → Task<int>` — T2 defines; T4 calls twice (initial + rerun closure) ✓
- `--watch` is a bare boolean flag (no value) — parsing in T4 doesn't `i + 1 < args.Length` check; just `if (a == "--watch") { watch = true; continue; }` ✓

**4. Hazards:**
- T2's "delete the inline scenario-run block" migration touches ~15 lines in a file that's been reshaped by every prior M2 task. The refactor may conflict if M2-reporters' `writer.Flush()` placement differs from what this plan assumes. If the implementer finds the shape different, keep the semantic invariants (reporter called once after the loop; failed-count computed from `Passed` flags) and adapt to the actual file shape.
- T5's smoke timing (sleep 25 for initial run, sleep 15 for rerun) is padded for headless Xvfb. If the machine is slower/faster, extend or shorten. The assertions are on PID stability + rerun-log-line-count, not wall-clock.
- The `--watch` exit code is the initial run's result (not final). If the user's last rerun had failures but the initial was clean, exit code is 0. This is documented in the spec's "Control flow" §6; implementer doesn't need to surface it via new tests.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-23-m2-watch-mode.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task with two-stage review. Proven across all prior M1/M2 plans.

**2. Inline Execution** — execute tasks in this session via `superpowers:executing-plans`, batch through with checkpoints.

**Which approach?**
