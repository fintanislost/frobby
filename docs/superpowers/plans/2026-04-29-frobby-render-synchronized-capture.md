# Frobby Render-Synchronized Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a generic Frobby next-render bitmap capture path so scenarios can safely screenshot UI after a state-changing click without wedging Stardew.

**Implementation status:** Completed on branch `frobby-text-bounds-for-starberg-ui`.
The implementation kept `bitmap.capture` compatible and added
`bitmap.capture_next_frame` plus the runner-level `screenshot.capture_next_frame`
action. Starberg scenario 39 validates the feature externally.

**Architecture:** Keep existing `bitmap.capture` behavior intact for compatibility, and add an opt-in render-synchronized capture path that completes from SMAPI's `Display.Rendered` event instead of reading the backbuffer immediately from an RPC update tick. This requires async RPC handler support so a handler can start on the game thread, yield while waiting for render, and complete later without blocking update/render processing.

**Tech Stack:** C#/.NET 6 harness, .NET 10 runner, SMAPI display events, JSON-RPC session, xUnit, Starberg scenario 39 as an external regression.

---

## Non-Goals

- Do not add Starberg-specific logic to Frobby.
- Do not replace all existing screenshots in the first pass.
- Do not remove or change `bitmap.capture`; existing scenarios and tests must keep working.

## File Map

- Modify `src/Harness/Rpc/GameThreadDispatch.cs`: add async game-thread dispatch support.
- Modify `src/Harness/Rpc/RpcDispatcher.cs`: add `RegisterAsync` for handlers that return `Task<JsonElement?>`.
- Create `src/Harness/Capture/BitmapCaptureWriter.cs`: move current backbuffer read/PNG write logic out of `BitmapCaptureHandler` so immediate and next-render handlers share one implementation.
- Create `src/Harness/Capture/RenderSynchronizedCaptureService.cs`: queue capture requests and complete them from `Display.Rendered`.
- Modify `src/Harness/Handlers/BitmapCaptureHandler.cs`: delegate immediate capture to `BitmapCaptureWriter`.
- Create `src/Harness/Handlers/BitmapCaptureNextFrameHandler.cs`: async RPC handler for `bitmap.capture_next_frame`.
- Modify `src/Harness/ModEntry.cs`: initialize render capture service, subscribe to `Display.Rendered`, and register `bitmap.capture_next_frame`.
- Create `src/Protocol/Models/BitmapCaptureRequest.cs`: shared request shape for immediate and next-frame capture.
- Modify `src/Runner/Reports/ScreenshotRecorder.cs`: add opt-in next-frame capture support.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`: add `screenshot.capture_next_frame` action and useful step label.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, and `docs/roadmap.md`: document the generic capture primitive.
- Test `tests/Harness.Tests/GameThreadDispatchTests.cs`
- Test `tests/Harness.Tests/RpcDispatcherTests.cs`
- Test `tests/Harness.Tests/BitmapCaptureHandlerTests.cs`
- Create `tests/Harness.Tests/RenderSynchronizedCaptureServiceTests.cs`
- Create `tests/Harness.Tests/BitmapCaptureNextFrameHandlerTests.cs`
- Test `tests/Runner.Tests/Reports/ScreenshotRecorderTests.cs`
- Test `tests/Runner.Tests/ScenarioRunnerTests.cs`
- External regression in `/home/fintan/stardewRepos/stonks/tests/sdv/39-starberg-chart-timeframes.test.json`

---

## Task 1: Async RPC Dispatch Support

**Files:**
- Modify `src/Harness/Rpc/GameThreadDispatch.cs`
- Modify `src/Harness/Rpc/RpcDispatcher.cs`
- Test `tests/Harness.Tests/GameThreadDispatchTests.cs`
- Test `tests/Harness.Tests/RpcDispatcherTests.cs`

- [ ] **Step 1: Write failing async dispatch tests**

Add to `GameThreadDispatchTests`:

```csharp
[Fact]
public async Task RunAsync_AsyncCallback_DoesNotBlockDrain()
{
    var d = new GameThreadDispatch();
    var inner = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

    var task = d.RunAsync(async () => await inner.Task);

    d.Drain();

    Assert.False(task.IsCompleted);
    Assert.Equal(0, d.PendingCount);

    inner.SetResult(42);

    Assert.Equal(42, await task);
}
```

Add to `RpcDispatcherTests`:

```csharp
[Fact]
public async Task Dispatch_AsyncHandler_CompletesAfterHandlerTask()
{
    var gameThread = new GameThreadDispatch();
    var disp = new RpcDispatcher(gameThread);
    var inner = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
    disp.RegisterAsync("wait", _ => inner.Task);

    var task = disp.DispatchAsync(new JsonRpcRequest { Id = 5, Method = "wait" }, CancellationToken.None);

    gameThread.Drain();
    Assert.False(task.IsCompleted);

    inner.SetResult(JsonDocument.Parse("{\"ok\":true}").RootElement);
    var resp = await task;

    Assert.Null(resp.Error);
    Assert.True(resp.Result!.Value.GetProperty("ok").GetBoolean());
}
```

- [ ] **Step 2: Run tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~GameThreadDispatchTests|FullyQualifiedName~RpcDispatcherTests"
```

Expected: compile failure for missing `GameThreadDispatch.RunAsync(Func<Task<T>>)` and `RpcDispatcher.RegisterAsync`.

- [ ] **Step 3: Implement async dispatch minimally**

Add this overload to `GameThreadDispatch`:

```csharp
public Task<T> RunAsync<T>(Func<Task<T>> fn, CancellationToken ct = default)
{
    var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

    CancellationTokenRegistration reg = default;
    if (ct.CanBeCanceled)
        reg = ct.Register(() => tcs.TrySetCanceled(ct));

    _queue.Enqueue(() =>
    {
        if (ct.IsCancellationRequested)
        {
            reg.Dispose();
            tcs.TrySetCanceled(ct);
            return;
        }

        Task<T> inner;
        try { inner = fn(); }
        catch (Exception ex)
        {
            reg.Dispose();
            tcs.TrySetException(ex);
            return;
        }

        _ = CompleteAsync(inner, tcs, reg, ct);
    });

    return tcs.Task;
}

private static async Task CompleteAsync<T>(
    Task<T> inner,
    TaskCompletionSource<T> tcs,
    CancellationTokenRegistration reg,
    CancellationToken ct)
{
    try
    {
        var result = await inner.ConfigureAwait(false);
        tcs.TrySetResult(result);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        tcs.TrySetCanceled(ct);
    }
    catch (Exception ex)
    {
        tcs.TrySetException(ex);
    }
    finally
    {
        reg.Dispose();
    }
}
```

Change `RpcDispatcher` to store sync and async handlers:

```csharp
private readonly Dictionary<string, Func<JsonElement?, Task<JsonElement?>>> _handlers =
    new(StringComparer.Ordinal);

public void Register(string method, Func<JsonElement?, JsonElement?> handler)
    => RegisterAsync(method, p => Task.FromResult(handler(p)));

public void RegisterAsync(string method, Func<JsonElement?, Task<JsonElement?>> handler)
{
    if (_handlers.ContainsKey(method))
        throw new InvalidOperationException($"duplicate method registration: {method}");
    _handlers[method] = handler;
}
```

In `DispatchAsync`, call:

```csharp
var result = await _gameThread.RunAsync(() => handler(request.Params), ct).ConfigureAwait(false);
```

- [ ] **Step 4: Run tests and verify green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~GameThreadDispatchTests|FullyQualifiedName~RpcDispatcherTests"
```

Expected: all matching tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Harness/Rpc/GameThreadDispatch.cs src/Harness/Rpc/RpcDispatcher.cs tests/Harness.Tests/GameThreadDispatchTests.cs tests/Harness.Tests/RpcDispatcherTests.cs
git commit -m "feat: support async harness RPC handlers"
```

---

## Task 2: Shared Bitmap Capture Writer

**Files:**
- Create `src/Protocol/Models/BitmapCaptureRequest.cs`
- Create `src/Harness/Capture/BitmapCaptureWriter.cs`
- Modify `src/Harness/Handlers/BitmapCaptureHandler.cs`
- Test `tests/Harness.Tests/BitmapCaptureHandlerTests.cs`

- [ ] **Step 1: Write request parsing tests**

Extend `BitmapCaptureHandlerTests` with:

```csharp
[Fact]
public void BitmapCaptureRequest_DefaultsTimeoutAndImmediateMode()
{
    var req = JsonSerializer.Deserialize<BitmapCaptureRequest>(
        "{}",
        ProtocolJson.Options)!;

    Assert.False(req.AllowUnfrozen);
    Assert.Equal(2000, req.TimeoutMs);
}
```

- [ ] **Step 2: Run test and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~BitmapCaptureHandlerTests
```

Expected: compile failure for missing `BitmapCaptureRequest`.

- [ ] **Step 3: Add shared request model**

Create `src/Protocol/Models/BitmapCaptureRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

public sealed class BitmapCaptureRequest
{
    public bool AllowUnfrozen { get; set; }
    public int TimeoutMs { get; set; } = 2000;
    public BitmapCaptureRegion? Region { get; set; }
}

public sealed class BitmapCaptureRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
```

- [ ] **Step 4: Extract writer without changing behavior**

Move the existing backbuffer read, crop, PNG write, and result creation from `BitmapCaptureHandler.Handle` into:

```csharp
namespace SdvTestFramework.Harness.Capture;

public static class BitmapCaptureWriter
{
    public static BitmapCaptureResult CaptureCurrent(JsonElement? paramsElement)
    {
        // Same validation and write behavior currently in BitmapCaptureHandler.Handle.
    }
}
```

Then make `BitmapCaptureHandler.Handle`:

```csharp
public static JsonElement Handle(JsonElement? paramsElement)
    => ProtocolJson.ToElement(BitmapCaptureWriter.CaptureCurrent(paramsElement));
```

- [ ] **Step 5: Run bitmap tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~BitmapCaptureHandlerTests
```

Expected: all matching tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Protocol/Models/BitmapCaptureRequest.cs src/Harness/Capture/BitmapCaptureWriter.cs src/Harness/Handlers/BitmapCaptureHandler.cs tests/Harness.Tests/BitmapCaptureHandlerTests.cs
git commit -m "refactor: share bitmap capture writer"
```

---

## Task 3: Render-Synchronized Capture Service

**Files:**
- Create `src/Harness/Capture/RenderSynchronizedCaptureService.cs`
- Create `tests/Harness.Tests/RenderSynchronizedCaptureServiceTests.cs`

- [ ] **Step 1: Write failing service tests**

Create `tests/Harness.Tests/RenderSynchronizedCaptureServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Harness.Capture;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class RenderSynchronizedCaptureServiceTests
{
    [Fact]
    public async Task RequestAsync_CompletesOnRendered()
    {
        var service = new RenderSynchronizedCaptureService();
        var task = service.RequestAsync(
            () => new BitmapCaptureResult { Path = "/tmp/capture.png", Width = 1280, Height = 720 },
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.False(task.IsCompleted);

        service.OnRendered();

        var result = await task;
        Assert.Equal("/tmp/capture.png", result.Path);
    }

    [Fact]
    public async Task RequestAsync_TimesOutWithoutRendered()
    {
        var service = new RenderSynchronizedCaptureService();

        var task = service.RequestAsync(
            () => new BitmapCaptureResult(),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(async () => await task);
    }

    [Fact]
    public async Task RequestAsync_PropagatesCaptureFailure()
    {
        var service = new RenderSynchronizedCaptureService();
        var task = service.RequestAsync(
            () => throw new InvalidOperationException("capture failed"),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        service.OnRendered();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.Equal("capture failed", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~RenderSynchronizedCaptureServiceTests
```

Expected: compile failure for missing `RenderSynchronizedCaptureService`.

- [ ] **Step 3: Implement service**

Create `src/Harness/Capture/RenderSynchronizedCaptureService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Capture;

public sealed class RenderSynchronizedCaptureService
{
    private readonly object _gate = new();
    private readonly List<PendingCapture> _pending = new();

    public Task<BitmapCaptureResult> RequestAsync(
        Func<BitmapCaptureResult> capture,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var pending = new PendingCapture(capture, timeout, ct);
        lock (_gate)
            _pending.Add(pending);
        return pending.Task;
    }

    public void OnRendered()
    {
        PendingCapture[] captures;
        lock (_gate)
        {
            captures = _pending.ToArray();
            _pending.Clear();
        }

        foreach (var pending in captures)
            pending.Complete();
    }

    private sealed class PendingCapture
    {
        private readonly Func<BitmapCaptureResult> _capture;
        private readonly TaskCompletionSource<BitmapCaptureResult> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _cancelRegistration;
        private readonly Timer _timer;

        public PendingCapture(Func<BitmapCaptureResult> capture, TimeSpan timeout, CancellationToken ct)
        {
            _capture = capture;
            _cancelRegistration = ct.Register(() => _tcs.TrySetCanceled(ct));
            _timer = new Timer(
                _ => _tcs.TrySetException(new TimeoutException($"bitmap.capture_next_frame timed out after {(int)timeout.TotalMilliseconds}ms waiting for Display.Rendered")),
                null,
                timeout,
                Timeout.InfiniteTimeSpan);
        }

        public Task<BitmapCaptureResult> Task => _tcs.Task;

        public void Complete()
        {
            try { _tcs.TrySetResult(_capture()); }
            catch (Exception ex) { _tcs.TrySetException(ex); }
            finally
            {
                _timer.Dispose();
                _cancelRegistration.Dispose();
            }
        }
    }
}
```

- [ ] **Step 4: Run service tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~RenderSynchronizedCaptureServiceTests
```

Expected: all matching tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Harness/Capture/RenderSynchronizedCaptureService.cs tests/Harness.Tests/RenderSynchronizedCaptureServiceTests.cs
git commit -m "feat: queue captures for next rendered frame"
```

---

## Task 4: Next-Frame Bitmap RPC

**Files:**
- Create `src/Harness/Handlers/BitmapCaptureNextFrameHandler.cs`
- Modify `src/Harness/ModEntry.cs`
- Create `tests/Harness.Tests/BitmapCaptureNextFrameHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/BitmapCaptureNextFrameHandlerTests.cs` with a fake `RenderSynchronizedCaptureService` seam. The test should verify:

```csharp
[Fact]
public async Task HandleAsync_RequiresPositiveTimeout()
{
    var p = JsonDocument.Parse("{\"timeout_ms\":0}").RootElement;

    var ex = await Assert.ThrowsAsync<JsonRpcException>(
        async () => await BitmapCaptureNextFrameHandler.HandleAsync(p, CancellationToken.None));

    Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
}
```

and:

```csharp
[Fact]
public async Task HandleAsync_ReturnsNextFrameCaptureResult()
{
    var service = new RenderSynchronizedCaptureService();
    BitmapCaptureNextFrameHandler.CaptureService = service;
    BitmapCaptureNextFrameHandler.CaptureNow = _ => new BitmapCaptureResult
    {
        Path = "/tmp/next.png",
        Width = 1280,
        Height = 720,
    };

    var task = BitmapCaptureNextFrameHandler.HandleAsync(null, CancellationToken.None);

    service.OnRendered();

    var result = await task;
    Assert.Equal("/tmp/next.png", result.GetProperty("path").GetString());
}
```

- [ ] **Step 2: Run tests and verify red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~BitmapCaptureNextFrameHandlerTests
```

Expected: compile failure for missing `BitmapCaptureNextFrameHandler`.

- [ ] **Step 3: Implement handler**

Create:

```csharp
public static class BitmapCaptureNextFrameHandler
{
    public const string Method = "bitmap.capture_next_frame";
    public static RenderSynchronizedCaptureService CaptureService { get; set; } = new();
    public static Func<JsonElement?, BitmapCaptureResult> CaptureNow { get; set; } = BitmapCaptureWriter.CaptureCurrent;

    public static async Task<JsonElement?> HandleAsync(JsonElement? paramsElement, CancellationToken ct)
    {
        var req = paramsElement is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<BitmapCaptureRequest>(obj.GetRawText(), ProtocolJson.Options) ?? new BitmapCaptureRequest()
            : new BitmapCaptureRequest();

        if (req.TimeoutMs < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.timeout_ms must be >= 1");

        var result = await CaptureService.RequestAsync(
            () => CaptureNow(paramsElement),
            TimeSpan.FromMilliseconds(req.TimeoutMs),
            ct);

        return ProtocolJson.ToElement(result);
    }
}
```

In `ModEntry.Entry`:

```csharp
var renderCapture = new RenderSynchronizedCaptureService();
BitmapCaptureNextFrameHandler.CaptureService = renderCapture;
helper.Events.Display.Rendered += (_, _) => renderCapture.OnRendered();
_rpc.RegisterAsync(BitmapCaptureNextFrameHandler.Method, p => BitmapCaptureNextFrameHandler.HandleAsync(p, _shutdownCts.Token));
```

- [ ] **Step 4: Run handler and dispatcher tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~BitmapCaptureNextFrameHandlerTests|FullyQualifiedName~RpcDispatcherTests"
```

Expected: all matching tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Harness/Handlers/BitmapCaptureNextFrameHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/BitmapCaptureNextFrameHandlerTests.cs
git commit -m "feat: add next-frame bitmap capture rpc"
```

---

## Task 5: Runner Opt-In Scenario Action

**Files:**
- Modify `src/Runner/Reports/ScreenshotRecorder.cs`
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
- Test `tests/Runner.Tests/Reports/ScreenshotRecorderTests.cs`
- Test `tests/Runner.Tests/ScenarioRunnerTests.cs`

- [ ] **Step 1: Write failing runner tests**

Add to `ScenarioRunnerTests` a scenario asserting `screenshot.capture_next_frame` calls the screenshot recorder in next-frame mode and records a step detail:

```csharp
[Fact]
public async Task ScreenshotCaptureNextFrame_UsesNextFrameBitmapRpc()
{
    // Existing ScenarioRunner socket-test pattern.
    // Server should expect "bitmap.capture_next_frame" and return {"path":"/tmp/source.png","width":1,"height":1}.
}
```

Add to `ScreenshotRecorderTests`:

```csharp
[Fact]
public async Task CaptureAsync_CanUseNextFrameInvoker()
{
    var inv = new FakeBitmapInvoker { CapturePath = src };
    var rec = new ScreenshotRecorder(inv);

    await rec.CaptureAsync(rd, "my_scenario", "next", CancellationToken.None, captureMode: ScreenshotCaptureMode.NextFrame);

    Assert.Equal(ScreenshotCaptureMode.NextFrame, inv.LastMode);
}
```

- [ ] **Step 2: Run tests and verify red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScreenshotRecorderTests|FullyQualifiedName~ScenarioRunnerTests"
```

Expected: compile failure for missing `ScreenshotCaptureMode` and `screenshot.capture_next_frame`.

- [ ] **Step 3: Implement opt-in capture mode**

Add:

```csharp
public enum ScreenshotCaptureMode
{
    Immediate,
    NextFrame,
}
```

Change `ScreenshotRecorder.IBitmapInvoker`:

```csharp
Task<string?> CaptureAsync(bool allowUnfrozen, ScreenshotCaptureMode mode, int timeoutMs, CancellationToken ct);
```

Make `SessionInvoker` call:

```csharp
var method = mode == ScreenshotCaptureMode.NextFrame ? "bitmap.capture_next_frame" : "bitmap.capture";
var resp = await _session.InvokeAsync(method, args, ct);
```

Add a `screenshot.capture_next_frame` branch in `ScenarioRunner`:

```csharp
else if (step.Action == "screenshot.capture_next_frame")
{
    await CaptureExplicitScreenshotAsync(step, spec.Name, report, ct, ScreenshotCaptureMode.NextFrame);
}
```

Keep `screenshot.capture` and auto step screenshots on immediate mode in this task so existing reports do not change.

- [ ] **Step 4: Run runner tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScreenshotRecorderTests|FullyQualifiedName~ScenarioRunnerTests"
```

Expected: all matching tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runner/Reports/ScreenshotRecorder.cs src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/Reports/ScreenshotRecorderTests.cs tests/Runner.Tests/ScenarioRunnerTests.cs
git commit -m "feat: add next-frame screenshot action"
```

---

## Task 6: Starberg External Regression Without Frobby Special-Casing

**Files:**
- Modify `/home/fintan/stardewRepos/stonks/tests/sdv/39-starberg-chart-timeframes.test.json`
- Modify `/home/fintan/stardewRepos/stonks/docs/FROBBY.md`
- Modify `/home/fintan/stardewRepos/stonks/STARBERG_FEATURE_CANDIDATES.todo.md` but keep it untracked

- [ ] **Step 1: Update scenario 39 to capture the rendered 1M frame**

Replace the frozen workaround tail with:

```json
{ "action": "freeze.begin", "args": {} },
{ "action": "input.click_menu_button", "args": { "label": "1M", "auto_screenshot": false } },
{ "action": "state.assert", "args": { "expr": "state.menu.extra.current_panel_type == 'ChartPanel'", "message": "Chart panel should remain active after clicking the timeframe control" } },
{ "action": "state.assert", "args": { "expr": "state.menu.extra.current_panel_timeframe == 'OneMonth'", "message": "Clicking the 1M control should select the one-month chart timeframe" } },
{ "action": "screenshot.capture_next_frame", "args": { "name": "timeframe-1m", "timeout_ms": 3000 } },
{ "action": "input.click", "args": { "x": 1192, "y": 55, "auto_screenshot": false } },
{ "action": "freeze.end", "args": { "auto_screenshot": false } }
```

- [ ] **Step 2: Run Starberg scenario 39**

Run from `/home/fintan/stardewRepos/stonks`:

```bash
./scripts/sdv-test --no-build --report-dir /tmp/starberg-frobby-results-0.1.0 tests/sdv/39-starberg-chart-timeframes.test.json
```

Expected:
- `PASS starberg_chart_timeframes`
- report contains `timeframe-1m.png`
- no lingering `sdv-test`, `StardewModdingAPI`, or `Xvfb` process after exit

- [ ] **Step 3: Run Starberg scenario 38**

Run:

```bash
./scripts/sdv-test --no-build --report-dir /tmp/starberg-frobby-results-0.1.0 tests/sdv/38-starberg-chart-panel-live.test.json
```

Expected: pass, proving current chart screenshots still work.

- [ ] **Step 4: Update Starberg docs**

Change `docs/FROBBY.md` scenario 39 note to say it now uses Frobby's generic `screenshot.capture_next_frame` and that the previous 1M screenshot gap is closed.

Move the TODO entry in `STARBERG_FEATURE_CANDIDATES.todo.md` from pending gap to completed local note, keeping the file untracked.

- [ ] **Step 5: Commit tracked Starberg scenario and docs**

```bash
git add tests/sdv/39-starberg-chart-timeframes.test.json docs/FROBBY.md
git commit -m "test: capture rendered chart timeframe frame"
```

---

## Task 7: Full Verification And Compatibility

**Files:**
- No source edits expected.

- [ ] **Step 1: Run Frobby focused tests**

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "FullyQualifiedName~GameThreadDispatchTests|FullyQualifiedName~RpcDispatcherTests|FullyQualifiedName~BitmapCaptureHandlerTests|FullyQualifiedName~BitmapCaptureNextFrameHandlerTests|FullyQualifiedName~RenderSynchronizedCaptureServiceTests"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScreenshotRecorderTests|FullyQualifiedName~ScenarioRunnerTests"
```

Expected: 0 failed.

- [ ] **Step 2: Run broader Frobby tests that cover existing behavior**

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj
dotnet test tests/Runner.Tests/Runner.Tests.csproj
```

Expected: 0 failed. This guards existing `bitmap.capture`, `draw.arm`, `ui.click_text`, report screenshots, and scenario runner behavior.

- [ ] **Step 3: Run Starberg focused tests**

```bash
dotnet test tests/Starberg.Tests.Unit/Starberg.Tests.Unit.csproj --filter "FullyQualifiedName~ChartPanelTests|FullyQualifiedName~CandleBufferTests|FullyQualifiedName~SaveMapperTests"
```

Expected: 0 failed.

- [ ] **Step 4: Run Starberg Frobby regressions headless**

```bash
./scripts/sdv-test --no-build --report-dir /tmp/starberg-frobby-results-0.1.0 tests/sdv/38-starberg-chart-panel-live.test.json
./scripts/sdv-test --no-build --report-dir /tmp/starberg-frobby-results-0.1.0 tests/sdv/39-starberg-chart-timeframes.test.json
```

Expected: both pass; scenario 39 report includes `timeframe-1m.png`.

- [ ] **Step 5: Check for lingering processes**

```bash
pgrep -af "sdv-test|Stardew|xvfb|Xvfb"
```

Expected: no matching process from the run.

- [ ] **Step 6: Inspect git state**

```bash
git status --short --branch
git -C /home/fintan/stardewRepos/stonks status --short --branch
```

Expected:
- Frobby has only intentional committed changes.
- Starberg keeps only intentionally untracked local files such as `STARBERG_FEATURE_CANDIDATES.todo.md`, `.codex`, and `build.binlog`.
