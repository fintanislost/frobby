# M3 C# Fluent DSL — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green**. T9's extra gates:
> - New projects `src/Runner.Dsl/` + `tests/Runner.Dsl.Tests/` build clean under `TreatWarningsAsErrors=true`.
> - `dotnet test tests/Runner.Dsl.Tests/` passes all unit tests.
> - `dotnet test tests/Runner.Dsl.Tests/ --filter Worked` runs the worked example end-to-end against live SDV (manual verification; the test is `[Fact(Skip)]` in default CI).
> - `./scripts/run-samples.sh` still 11/11 PASS (no regression).

**Goal:** Ship a typed C# DSL in `src/Runner.Dsl/` that lets modders write scenarios as xUnit test methods instead of hand-authoring `*.test.json`. Ambient static classes (`Player`, `Time`, `World`, `Freeze`, `Draw`, `State`, `Fixture`, `Bitmap`, `Wait`) wrap the JSON-RPC surface. `[Scenario(seed, fixture)]` xUnit `BeforeAfterTestAttribute` wraps tests in `scenario.begin`/`scenario.end`. `[CollectionDefinition("SDV")]` fixture owns one SDV subprocess per test assembly.

**Architecture:** New `src/Runner.Dsl/` project (.NET 10) references `src/Protocol/` (DTOs + `UnixSocketRpc` + `JsonRpcSession`) and `src/Runner/` (reuses `SdvLauncher` + `HarnessDeployer` for launch flow). `SdvTestSession.Current` is a static accessor populated by the collection fixture; facets read from it. RPC errors throw typed exceptions (`SdvRpcException` base + `SdvGameStateInvalidException` / `SdvInvalidParamsException` / `SdvInternalErrorException` subclasses).

**Tech Stack:**
- .NET 10 (Runner TFM) — matches Runner + Runner.Tests.
- `xunit` 2.9.0 + `xunit.runner.visualstudio` 2.8.2 — match Runner.Tests versions.
- `Microsoft.NET.Test.Sdk` 17.10.0 — same.
- `System.Text.Json` (BCL) — no new dependencies.

**Design spec:** `docs/superpowers/specs/2026-04-24-m3-csharp-dsl-design.md`

---

## File structure

**New source project (`src/Runner.Dsl/`):**
- `Runner.Dsl.csproj` — net10.0, references Protocol + Runner, enables internals-visible-to for tests.
- `SdvTestSession.cs` — static `Current` + session + RPC dispatch helper `InvokeAsync<TResult>(method, params) → TResult`.
- `Exceptions.cs` — `SdvRpcException` base + 3 subclasses.
- `Player.cs` / `Time.cs` / `World.cs` / `Fixture.cs` / `Freeze.cs` / `Wait.cs` / `Draw.cs` / `State.cs` / `Bitmap.cs` — one static facet class per file.
- `BitmapRegion.cs` — small record wrapper for the `bitmap.capture` region param.
- `ScenarioAttribute.cs` — `BeforeAfterTestAttribute` subclass.
- `SdvFixture.cs` — xUnit collection fixture. Launches SDV, connects socket, initializes `SdvTestSession.Current`.
- `SdvCollection.cs` — empty `[CollectionDefinition("SDV")]` class referencing `SdvFixture`. Small shared type so users don't have to write it themselves.

**New test project (`tests/Runner.Dsl.Tests/`):**
- `Runner.Dsl.Tests.csproj` — xUnit test project, references `src/Runner.Dsl/`.
- `SdvTestSessionTests.cs` — 2 tests (Current is null when not initialized; InvokeAsync forwards to session).
- `ExceptionsTests.cs` — 2 tests (error-code-to-subclass mapping; default code falls through to base).
- `Facets/PlayerWorldTimeTests.cs` — 3 tests (Player.Warp, Player.SetMoney, Time.Advance — one representative per facet).
- `Facets/FixtureFreezeWaitTests.cs` — 3 tests.
- `Facets/StateTests.cs` — 2 tests (one read query + one deserialization-check).
- `Facets/DrawBitmapTests.cs` — 3 tests (Draw.Arm, Draw.AssertContains, Bitmap.Capture).
- `ScenarioAttributeTests.cs` — 2 tests.
- `DslIntegrationTests.cs` — 1 `[Fact(Skip=...)]` placeholder.
- `Worked/ShopMenuDslSmoke.cs` — 1 `[Fact(Skip="Requires live SDV")]` realistic example.

**Modified files:**
- `scripts/ci.sh` — if it explicitly lists test projects, add `tests/Runner.Dsl.Tests/`. If it uses a wildcard (`dotnet test` at repo root or glob), nothing to change — verify.
- `docs/milestones/current.md` — M3-DSL completion subsection.
- `docs/dsl-quickstart.md` — new user-facing getting-started doc (created in T9).

**Starting test count:** 266 Passed + 34 Skipped.
**Target test count after DSL ships:** ~282 Passed + 35 Skipped (+16 passed, +1 skipped).

---

## Task 1: Project scaffolding

**Why:** Create the two new csproj files + hook them into CI. Zero functional code beyond a placeholder class that the test project can reference, confirming the wire-up works.

**Files:**
- Create: `src/Runner.Dsl/Runner.Dsl.csproj`
- Create: `src/Runner.Dsl/_Placeholder.cs` (removed by T2; lets the project compile before T2 lands)
- Create: `tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj`
- Create: `tests/Runner.Dsl.Tests/PlaceholderTests.cs` (removed by T2)
- Modify: `scripts/ci.sh` (only if it enumerates projects explicitly)

- [ ] **Step 1: Create Runner.Dsl.csproj**

Write `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner.Dsl/Runner.Dsl.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SdvTestFramework.Runner.Dsl</RootNamespace>
    <AssemblyName>Runner.Dsl</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Protocol\Protocol.csproj" />
    <ProjectReference Include="..\Runner\Runner.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Runner.Dsl.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create placeholder**

Write `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner.Dsl/_Placeholder.cs`:

```csharp
namespace SdvTestFramework.Runner.Dsl;

// Removed by Task 2 once SdvTestSession lands. Lets the project compile in isolation.
internal static class _Placeholder { internal const string Marker = "dsl-scaffolding"; }
```

- [ ] **Step 3: Create Runner.Dsl.Tests.csproj**

Write `/home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SdvTestFramework.Runner.Dsl.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Runner.Dsl\Runner.Dsl.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create placeholder test**

Write `/home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Dsl.Tests/PlaceholderTests.cs`:

```csharp
using Xunit;
using SdvTestFramework.Runner.Dsl;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class PlaceholderTests
{
    [Fact]
    public void DslAssemblyReferenceWires()
    {
        // Verifies Runner.Dsl.Tests.csproj → Runner.Dsl.csproj is wired. Removed in T2.
        Assert.Equal("dsl-scaffolding", _Placeholder.Marker);
    }
}
```

- [ ] **Step 5: Verify scripts/ci.sh picks up the new project**

Inspect `scripts/ci.sh`. If it lists test projects explicitly (e.g., `dotnet test tests/Harness.Tests/ tests/Protocol.Tests/ tests/Runner.Tests/`), add `tests/Runner.Dsl.Tests/` to the list. If it wildcards or runs at solution root, no change needed.

- [ ] **Step 6: Run CI**

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | tail -5`
Expected: PASS. Test count 266 → 267 (+1 placeholder test). Skipped stays at 34.

---

## Task 2: SdvTestSession + Exceptions

**Why:** Core plumbing. SdvTestSession is the ambient-static holder that every facet reads. Exception hierarchy provides typed error handling for RPC failures.

**Files:**
- Create: `src/Runner.Dsl/SdvTestSession.cs`
- Create: `src/Runner.Dsl/Exceptions.cs`
- Create: `tests/Runner.Dsl.Tests/SdvTestSessionTests.cs`
- Create: `tests/Runner.Dsl.Tests/ExceptionsTests.cs`
- Delete: `src/Runner.Dsl/_Placeholder.cs`
- Delete: `tests/Runner.Dsl.Tests/PlaceholderTests.cs`

- [ ] **Step 1: Write failing tests for SdvTestSession**

Create `tests/Runner.Dsl.Tests/SdvTestSessionTests.cs`:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class SdvTestSessionTests
{
    [Fact]
    public void Current_NotInitialized_IsNull()
    {
        // Explicit reset in case a prior test left state. SdvTestSession is a singleton
        // accessor; production only initializes it via SdvFixture.
        SdvTestSession.ResetForTests();
        Assert.Null(SdvTestSession.Current);
    }

    [Fact]
    public async Task InvokeAsync_ForwardsToSession()
    {
        var fake = new FakeSession();
        SdvTestSession.InitializeForTests(fake);
        try
        {
            var paramsJson = JsonSerializer.SerializeToElement(new { name = "x" });
            await SdvTestSession.Current!.InvokeAsync("test.method", paramsJson, CancellationToken.None);

            Assert.Equal("test.method", fake.LastMethod);
        }
        finally
        {
            SdvTestSession.ResetForTests();
        }
    }

    // Minimal session seam — tests hand-build this; production uses the real JsonRpcSession.
    private sealed class FakeSession : ISdvTestInvoker
    {
        public string? LastMethod { get; private set; }

        public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            LastMethod = method;
            // Return an empty object; real handlers' responses are verified in facet tests.
            return Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone());
        }
    }
}
```

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && dotnet test tests/Runner.Dsl.Tests/ --filter SdvTestSession`
Expected: FAIL — `SdvTestSession`, `ISdvTestInvoker` don't exist.

- [ ] **Step 2: Create SdvTestSession**

Create `src/Runner.Dsl/SdvTestSession.cs`:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// Lightweight abstraction over "send this RPC, await the result" — lets the DSL's
/// ambient facets run against either a real <see cref="JsonRpcSession"/> or a test shim
/// without knowing the difference.
/// </summary>
public interface ISdvTestInvoker
{
    Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct);
}

/// <summary>
/// Ambient accessor for the per-assembly SDV session. Populated by <see cref="SdvFixture"/>
/// at xUnit collection-fixture startup; read by every DSL facet (<c>Player</c>, <c>Draw</c>, etc.).
/// </summary>
/// <remarks>
/// Static because xUnit tests in the same <c>[Collection]</c> don't run in parallel, so a
/// simple static accessor is thread-safe within the collection's execution window. Users in
/// multiple parallel collections would need one session per collection — not yet supported;
/// see the design spec's out-of-scope list.
/// </remarks>
public sealed class SdvTestSession
{
    private static SdvTestSession? _current;

    /// <summary>Ambient session; null before <see cref="SdvFixture"/> initializes it.</summary>
    public static SdvTestSession? Current => _current;

    private readonly ISdvTestInvoker _invoker;

    private SdvTestSession(ISdvTestInvoker invoker) => _invoker = invoker;

    /// <summary>Production initialization wrapping a real <see cref="JsonRpcSession"/>.</summary>
    public static SdvTestSession Initialize(JsonRpcSession session)
    {
        if (_current != null)
            throw new InvalidOperationException("SdvTestSession.Current is already initialized");
        _current = new SdvTestSession(new SessionInvoker(session));
        return _current;
    }

    /// <summary>Test-only initialization with a custom invoker (shim).</summary>
    internal static SdvTestSession InitializeForTests(ISdvTestInvoker invoker)
    {
        _current = new SdvTestSession(invoker);
        return _current;
    }

    /// <summary>Tear down; used by production fixture dispose + tests.</summary>
    public static void ResetForTests() => _current = null;

    /// <summary>Invoke an RPC method; throws typed <see cref="SdvRpcException"/> on error.</summary>
    public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        => _invoker.InvokeAsync(method, @params, ct);

    // Internal adapter that wraps JsonRpcSession + translates errors to typed exceptions.
    private sealed class SessionInvoker : ISdvTestInvoker
    {
        private readonly JsonRpcSession _session;
        public SessionInvoker(JsonRpcSession session) => _session = session;

        public async Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            var resp = await _session.InvokeAsync(method, @params, ct);
            if (resp.Error is { } e)
                throw SdvRpcException.Create(method, e);
            return resp.Result ?? JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}
```

- [ ] **Step 3: Write failing tests for Exceptions**

Create `tests/Runner.Dsl.Tests/ExceptionsTests.cs`:

```csharp
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class ExceptionsTests
{
    [Fact]
    public void Create_GameStateInvalid_ReturnsTypedSubclass()
    {
        var err = new JsonRpcError(JsonRpcErrorCode.GameStateInvalid, "not frozen");
        var ex = SdvRpcException.Create("freeze.begin", err);

        Assert.IsType<SdvGameStateInvalidException>(ex);
        Assert.Equal("freeze.begin", ex.Method);
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("freeze.begin", ex.Message);
        Assert.Contains("not frozen", ex.Message);
    }

    [Fact]
    public void Create_UnknownCode_ReturnsBaseException()
    {
        // A code not in our common-subclasses list falls through to SdvRpcException base.
        var err = new JsonRpcError((JsonRpcErrorCode)(-99999), "custom");
        var ex = SdvRpcException.Create("weird.method", err);

        Assert.IsType<SdvRpcException>(ex);
        Assert.Equal("weird.method", ex.Method);
    }
}
```

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && dotnet test tests/Runner.Dsl.Tests/ --filter Exceptions`
Expected: FAIL — `SdvRpcException` doesn't exist.

- [ ] **Step 4: Create Exceptions.cs**

Create `src/Runner.Dsl/Exceptions.cs`:

```csharp
using System;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// Base exception for RPC failures surfaced to DSL callers. Subclasses provide typed
/// handling for the common error codes; the base covers everything else.
/// </summary>
public class SdvRpcException : Exception
{
    public string Method { get; }
    public JsonRpcErrorCode Code { get; }

    public SdvRpcException(string method, JsonRpcErrorCode code, string message)
        : base($"RPC '{method}' failed ({code}): {message}")
    {
        Method = method;
        Code = code;
    }

    /// <summary>
    /// Construct the right subclass for the error code — callers can <c>catch (SdvGameStateInvalidException)</c>
    /// when they expect a precondition fail.
    /// </summary>
    public static SdvRpcException Create(string method, JsonRpcError error) => error.Code switch
    {
        JsonRpcErrorCode.GameStateInvalid => new SdvGameStateInvalidException(method, error.Message),
        JsonRpcErrorCode.InvalidParams    => new SdvInvalidParamsException(method, error.Message),
        JsonRpcErrorCode.InternalError    => new SdvInternalErrorException(method, error.Message),
        _ => new SdvRpcException(method, error.Code, error.Message),
    };
}

/// <summary>Thrown when an RPC precondition fails (e.g. <c>freeze.begin</c> without an active scenario).</summary>
public sealed class SdvGameStateInvalidException : SdvRpcException
{
    public SdvGameStateInvalidException(string method, string message)
        : base(method, JsonRpcErrorCode.GameStateInvalid, message) { }
}

/// <summary>Thrown when an RPC's params fail validation (wrong type, out of range, etc.).</summary>
public sealed class SdvInvalidParamsException : SdvRpcException
{
    public SdvInvalidParamsException(string method, string message)
        : base(method, JsonRpcErrorCode.InvalidParams, message) { }
}

/// <summary>Thrown when the harness hits an internal error (reflection failure, file I/O, etc.).</summary>
public sealed class SdvInternalErrorException : SdvRpcException
{
    public SdvInternalErrorException(string method, string message)
        : base(method, JsonRpcErrorCode.InternalError, message) { }
}
```

- [ ] **Step 5: Delete placeholder files from T1**

Delete `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner.Dsl/_Placeholder.cs` and `/home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Dsl.Tests/PlaceholderTests.cs`.

- [ ] **Step 6: Run CI**

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | tail -5`
Expected: PASS. Test count 267 → 268 (was 266+34 then +1 placeholder in T1 → 267; −1 placeholder + 2 session + 2 exceptions = +3 net). 269 Passed + 34 Skipped.

Actually the clean math: 266 + 2 (Session) + 2 (Exceptions) = 270 Passed + 34 Skipped, starting baseline restored (placeholder gone).

---

## Task 3: Player + Time + World facets

**Why:** First batch of mutator facets — the straightforward "fire-and-forget RPC" cases. All three follow the same pattern: serialize a small request object, await `InvokeAsync`, optionally deserialize a response.

**Files:**
- Create: `src/Runner.Dsl/Player.cs`
- Create: `src/Runner.Dsl/Time.cs`
- Create: `src/Runner.Dsl/World.cs`
- Create: `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Dsl.Tests/Facets/PlayerWorldTimeTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class PlayerWorldTimeTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public JsonElement NextResponse { get; set; } = JsonDocument.Parse("{\"ok\":true,\"tick\":42}").RootElement;

        public Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            return Task.FromResult(NextResponse);
        }
    }

    [Fact]
    public async Task Warp_InvokesPlayerWarpWithLocationXY()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Player.Warp("SeedShop", 4, 19); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Single(inv.Calls);
        Assert.Equal("player.warp", inv.Calls[0].Method);
        Assert.Contains("\"location\":\"SeedShop\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"x\":4", inv.Calls[0].ParamsJson);
        Assert.Contains("\"y\":19", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task SetMoney_InvokesPlayerSetMoneyWithAmount()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Player.SetMoney(5000); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("player.set_money", inv.Calls[0].Method);
        Assert.Contains("\"amount\":5000", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task Advance_InvokesTimeAdvanceWithMinutes()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Time.Advance(60); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("time.advance", inv.Calls[0].Method);
        Assert.Contains("\"minutes\":60", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task SetWeather_InvokesWorldSetWeatherWithType()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await World.SetWeather("rain"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("world.set_weather", inv.Calls[0].Method);
        Assert.Contains("\"type\":\"rain\"", inv.Calls[0].ParamsJson);
    }
}
```

Run: FAIL — `Player`, `Time`, `World` don't exist.

- [ ] **Step 2: Create Player.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>player.*</c> mutator RPC surface.</summary>
public static class Player
{
    /// <summary>Warp the player to <paramref name="location"/> at tile (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static async Task Warp(string location, int x, int y, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new WarpRequest { Location = location, X = x, Y = y }, ProtocolJson.Options);
        await s.InvokeAsync("player.warp", p, ct);
    }

    /// <summary>Set the player's money to <paramref name="amount"/>.</summary>
    public static async Task SetMoney(int amount, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new SetMoneyRequest { Amount = amount }, ProtocolJson.Options);
        await s.InvokeAsync("player.set_money", p, ct);
    }

    /// <summary>Give the player <paramref name="count"/> of item <paramref name="id"/> (e.g. <c>"(O)74"</c> for prismatic shard).</summary>
    public static async Task GiveItem(string id, int count = 1, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new GiveItemRequest { Id = id, Count = count }, ProtocolJson.Options);
        await s.InvokeAsync("player.give_item", p, ct);
    }
}
```

- [ ] **Step 3: Create Time.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>time.*</c> RPC surface.</summary>
public static class Time
{
    /// <summary>Advance in-game time by <paramref name="minutes"/>.</summary>
    public static async Task Advance(int minutes, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new TimeAdvanceRequest { Minutes = minutes }, ProtocolJson.Options);
        await s.InvokeAsync("time.advance", p, ct);
    }
}
```

- [ ] **Step 4: Create World.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>world.*</c> RPC surface.</summary>
public static class World
{
    /// <summary>Set the current weather (<c>"sunny"</c>, <c>"rain"</c>, etc.).</summary>
    public static async Task SetWeather(string type, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new WeatherRequest { Type = type }, ProtocolJson.Options);
        await s.InvokeAsync("world.set_weather", p, ct);
    }
}
```

- [ ] **Step 5: Create DslPreconditions helper**

All facets share the null-check + message. Factor it once.

Create `src/Runner.Dsl/DslPreconditions.cs`:

```csharp
using System;

namespace SdvTestFramework.Runner.Dsl;

internal static class DslPreconditions
{
    internal static InvalidOperationException NoSession() =>
        new("SdvTestSession.Current is not initialized. Ensure your test class has [Collection(\"SDV\")] and the assembly declares [CollectionDefinition(\"SDV\")] with SdvFixture.");
}
```

- [ ] **Step 6: Run tests to verify GREEN**

Run: `dotnet test tests/Runner.Dsl.Tests/ --filter PlayerWorldTime`
Expected: 4 tests pass.

- [ ] **Step 7: Run CI**

Run: `./scripts/ci.sh 2>&1 | tail -5`
Expected: PASS. Test count 270 → 274 (+4).

---

## Task 4: Fixture + Freeze + Wait facets

**Why:** Second batch. `Fixture.Load` + `Freeze.Begin/End/Status` are thin RPC wrappers; `Wait.Ms` is client-side `Task.Delay` (matches `ScenarioRunner`'s existing `wait.ms` step).

**Files:**
- Create: `src/Runner.Dsl/Fixture.cs`
- Create: `src/Runner.Dsl/Freeze.cs`
- Create: `src/Runner.Dsl/Wait.cs`
- Create: `tests/Runner.Dsl.Tests/Facets/FixtureFreezeWaitTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Dsl.Tests/Facets/FixtureFreezeWaitTests.cs`:

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class FixtureFreezeWaitTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string, string)> Calls { get; } = new();
        public JsonElement NextResponse { get; set; } = JsonDocument.Parse("{\"ok\":true}").RootElement;
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        { Calls.Add((m, p?.GetRawText() ?? "")); return Task.FromResult(NextResponse); }
    }

    [Fact]
    public async Task FixtureLoad_InvokesWithName()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Fixture.Load("m0spike_436515781"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("fixture.load", inv.Calls[0].Item1);
        Assert.Contains("m0spike_436515781", inv.Calls[0].Item2);
    }

    [Fact]
    public async Task FreezeBegin_InvokesFreezeBegin()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Freeze.Begin(); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("freeze.begin", inv.Calls[0].Item1);
    }

    [Fact]
    public async Task FreezeEnd_InvokesFreezeEnd()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Freeze.End(); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("freeze.end", inv.Calls[0].Item1);
    }

    [Fact]
    public async Task WaitMs_DelaysLocallyWithoutRpc()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var sw = Stopwatch.StartNew();
            await Wait.Ms(100);
            sw.Stop();
            Assert.Empty(inv.Calls);
            Assert.True(sw.ElapsedMilliseconds >= 90, $"expected ≥90ms, got {sw.ElapsedMilliseconds}ms");
        }
        finally { SdvTestSession.ResetForTests(); }
    }
}
```

- [ ] **Step 2: Create Fixture.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for fixture lifecycle RPCs.</summary>
public static class Fixture
{
    /// <summary>Load the named save fixture from <c>tests/fixtures/&lt;name&gt;/</c>.</summary>
    public static async Task Load(string name, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new FixtureLoadRequest { Name = name }, ProtocolJson.Options);
        await s.InvokeAsync("fixture.load", p, ct);
    }
}
```

- [ ] **Step 3: Create Freeze.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>freeze.*</c> determinism-controller RPCs.</summary>
public static class Freeze
{
    /// <summary>Enter FREEZE phase — pins RNG, halts NPCs, stops the game-time clock.</summary>
    public static async Task Begin(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        await s.InvokeAsync("freeze.begin", null, ct);
    }

    /// <summary>Exit FREEZE phase — restores snapshotted state.</summary>
    public static async Task End(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        await s.InvokeAsync("freeze.end", null, ct);
    }

    /// <summary>Query current FREEZE state.</summary>
    public static async Task<FreezeStatusResult> Status(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("freeze.status", null, ct);
        return JsonSerializer.Deserialize<FreezeStatusResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("freeze.status returned no result");
    }
}
```

- [ ] **Step 4: Create Wait.cs**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Client-side wait primitives — no RPC, but the game keeps ticking during the delay.</summary>
public static class Wait
{
    /// <summary>Sleep for <paramref name="ms"/> milliseconds. Use between RPCs to let async game-thread work (warps, loading) complete.</summary>
    public static Task Ms(int ms, CancellationToken ct = default) => Task.Delay(ms, ct);
}
```

- [ ] **Step 5: Run tests + CI**

Run tests + CI. Expect 274 → 278 (+4).

---

## Task 5: State facet

**Why:** Read-only query facet. Six methods returning the Protocol DTOs we already have (`PlayerState`, `TimeState`, `LocationState`, `NpcState`, `MenuState`, `ModsState`).

**Files:**
- Create: `src/Runner.Dsl/State.cs`
- Create: `tests/Runner.Dsl.Tests/Facets/StateTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Dsl.Tests/Facets/StateTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class StateTests
{
    private sealed class StubInvoker : ISdvTestInvoker
    {
        public string? LastMethod { get; private set; }
        public string? LastParams { get; private set; }
        public string NextJson { get; set; } = "{}";
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            LastMethod = m;
            LastParams = p?.GetRawText();
            return Task.FromResult(JsonDocument.Parse(NextJson).RootElement.Clone());
        }
    }

    [Fact]
    public async Task Player_InvokesStatePlayerAndDeserializes()
    {
        var inv = new StubInvoker
        {
            NextJson = "{\"name\":\"Alice\",\"money\":5000,\"stamina\":200,\"max_stamina\":270,\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15}}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var p = await State.Player();
            Assert.Equal("state.player", inv.LastMethod);
            Assert.Equal("Alice", p.Name);
            Assert.Equal(5000, p.Money);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task Npc_InvokesStateNpcWithName()
    {
        var inv = new StubInvoker { NextJson = "{\"name\":\"Pierre\"}" };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var n = await State.Npc("Pierre");
            Assert.Equal("state.npc", inv.LastMethod);
            Assert.Contains("\"name\":\"Pierre\"", inv.LastParams);
            Assert.Equal("Pierre", n.Name);
        }
        finally { SdvTestSession.ResetForTests(); }
    }
}
```

- [ ] **Step 2: Create State.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>state.*</c> read-only query surface.</summary>
public static class State
{
    public static async Task<PlayerState> Player(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.player", null, ct);
        return Deserialize<PlayerState>(resp, "state.player");
    }

    public static async Task<TimeState> Time(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.time", null, ct);
        return Deserialize<TimeState>(resp, "state.time");
    }

    public static async Task<LocationState> Location(string? name = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        JsonElement? p = name is null
            ? null
            : JsonSerializer.SerializeToElement(new { name }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.location", p, ct);
        return Deserialize<LocationState>(resp, "state.location");
    }

    public static async Task<NpcState> Npc(string name, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { name }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.npc", p, ct);
        return Deserialize<NpcState>(resp, "state.npc");
    }

    public static async Task<MenuState> Menu(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.menu", null, ct);
        return Deserialize<MenuState>(resp, "state.menu");
    }

    public static async Task<ModsState> Mods(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.mods", null, ct);
        return Deserialize<ModsState>(resp, "state.mods");
    }

    private static T Deserialize<T>(JsonElement el, string method)
        => JsonSerializer.Deserialize<T>(el, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException($"{method} returned null result");
}
```

- [ ] **Step 3: Run tests + CI**

Run tests + CI. Expect 278 → 280 (+2).

---

## Task 6: Draw + Bitmap facets

**Why:** Largest facet batch. Draw exposes 6 methods (arm/disarm/snapshot/find/assert_contains/assert_not_contains). Bitmap exposes 1 method (capture) + a small record for region params.

**Files:**
- Create: `src/Runner.Dsl/Draw.cs`
- Create: `src/Runner.Dsl/Bitmap.cs`
- Create: `src/Runner.Dsl/BitmapRegion.cs`
- Create: `tests/Runner.Dsl.Tests/Facets/DrawBitmapTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Dsl.Tests/Facets/DrawBitmapTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class DrawBitmapTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public string NextJson { get; set; } = "{}";
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((m, p?.GetRawText() ?? ""));
            return Task.FromResult(JsonDocument.Parse(NextJson).RootElement.Clone());
        }
    }

    [Fact]
    public async Task Arm_InvokesDrawArm()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Draw.Arm(); }
        finally { SdvTestSession.ResetForTests(); }
        Assert.Equal("draw.arm", inv.Calls[0].Method);
    }

    [Fact]
    public async Task AssertContains_InvokesDrawAssertContainsWithFilter()
    {
        var inv = new CapturingInvoker
        {
            NextJson = "{\"passed\":true,\"matched\":1}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            await Draw.AssertContains(new DrawFilter { TextureAsset = "LooseSprites/Cursors" }, minCount: 2);
        }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("draw.assert_contains", inv.Calls[0].Method);
        Assert.Contains("\"texture_asset\":\"LooseSprites/Cursors\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"min_count\":2", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task BitmapCapture_WithRegion_SerializesRegionParam()
    {
        var inv = new CapturingInvoker
        {
            NextJson = "{\"path\":\"/tmp/x.png\",\"width\":32,\"height\":32}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var result = await Bitmap.Capture(new BitmapRegion(0, 0, 32, 32));
            Assert.Equal("/tmp/x.png", result.Path);
            Assert.Equal(32, result.Width);
        }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("bitmap.capture", inv.Calls[0].Method);
        Assert.Contains("\"region\":", inv.Calls[0].ParamsJson);
        Assert.Contains("\"w\":32", inv.Calls[0].ParamsJson);
    }
}
```

- [ ] **Step 2: Create BitmapRegion.cs**

```csharp
namespace SdvTestFramework.Runner.Dsl;

/// <summary>Sub-rect for <see cref="Bitmap.Capture"/>. All fields non-negative; w + h &gt; 0.</summary>
public readonly record struct BitmapRegion(int X, int Y, int W, int H);
```

- [ ] **Step 3: Create Draw.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>draw.*</c> RPC surface.</summary>
public static class Draw
{
    public static async Task Arm(int? ticks = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        JsonElement? p = ticks is null
            ? null
            : JsonSerializer.SerializeToElement(new DrawArmRequest { Ticks = ticks.Value }, ProtocolJson.Options);
        await s.InvokeAsync("draw.arm", p, ct);
    }

    public static async Task Disarm(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        await s.InvokeAsync("draw.disarm", null, ct);
    }

    public static async Task<DrawEventSnapshot> Snapshot(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("draw.snapshot", null, ct);
        return JsonSerializer.Deserialize<DrawEventSnapshot>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.snapshot returned null");
    }

    public static async Task<DrawFindResult> Find(DrawFilter filter, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { filter }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("draw.find", p, ct);
        return JsonSerializer.Deserialize<DrawFindResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.find returned null");
    }

    public static async Task<AssertResult> AssertContains(DrawFilter filter, int minCount = 1, string? message = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { filter, min_count = minCount, message }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("draw.assert_contains", p, ct);
        return JsonSerializer.Deserialize<AssertResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.assert_contains returned null");
    }

    public static async Task<AssertResult> AssertNotContains(DrawFilter filter, string? message = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { filter, message }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("draw.assert_not_contains", p, ct);
        return JsonSerializer.Deserialize<AssertResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.assert_not_contains returned null");
    }
}
```

- [ ] **Step 4: Create Bitmap.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>bitmap.*</c> RPC surface (FREEZE-phase framebuffer capture).</summary>
public static class Bitmap
{
    public static async Task<BitmapCaptureResult> Capture(BitmapRegion? region = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        JsonElement? p = null;
        if (region is { } r)
        {
            p = JsonSerializer.SerializeToElement(
                new { region = new { x = r.X, y = r.Y, w = r.W, h = r.H } },
                ProtocolJson.Options);
        }
        var resp = await s.InvokeAsync("bitmap.capture", p, ct);
        return JsonSerializer.Deserialize<BitmapCaptureResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("bitmap.capture returned null");
    }
}
```

- [ ] **Step 5: Run tests + CI**

Run tests + CI. Expect 280 → 283 (+3).

---

## Task 7: ScenarioAttribute

**Why:** The `[Scenario]` attribute that wraps each test method in `scenario.begin`/`scenario.end`. Uses xUnit's `BeforeAfterTestAttribute`.

**Files:**
- Create: `src/Runner.Dsl/ScenarioAttribute.cs`
- Create: `tests/Runner.Dsl.Tests/ScenarioAttributeTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Dsl.Tests/ScenarioAttributeTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class ScenarioAttributeTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((m, p?.GetRawText() ?? ""));
            return Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone());
        }
    }

    // Dummy method surface for reflection — the attribute's Before/After take a MethodInfo.
    private static void DummyTestMethod() { }

    [Fact]
    public void Before_InvokesScenarioBeginWithNameSeedFixture()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var attr = new ScenarioAttribute(name: "my_scenario", seed: 42, fixture: "m0spike");
            var mi = typeof(ScenarioAttributeTests).GetMethod(nameof(DummyTestMethod), BindingFlags.NonPublic | BindingFlags.Static)!;
            attr.Before(mi);

            Assert.Single(inv.Calls);
            Assert.Equal("scenario.begin", inv.Calls[0].Method);
            Assert.Contains("\"name\":\"my_scenario\"", inv.Calls[0].ParamsJson);
            Assert.Contains("\"seed\":42", inv.Calls[0].ParamsJson);
            Assert.Contains("\"fixture\":\"m0spike\"", inv.Calls[0].ParamsJson);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public void After_InvokesScenarioEnd()
    {
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var attr = new ScenarioAttribute();
            var mi = typeof(ScenarioAttributeTests).GetMethod(nameof(DummyTestMethod), BindingFlags.NonPublic | BindingFlags.Static)!;
            attr.After(mi);

            Assert.Equal("scenario.end", inv.Calls[0].Method);
        }
        finally { SdvTestSession.ResetForTests(); }
    }
}
```

- [ ] **Step 2: Create ScenarioAttribute.cs**

```csharp
using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit.Sdk;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// Wraps a test method in <c>scenario.begin</c> / <c>scenario.end</c>. Apply alongside
/// <c>[Fact]</c> on any test in a <c>[Collection("SDV")]</c>-decorated class.
/// </summary>
/// <remarks>
/// Because xUnit's <see cref="BeforeAfterTestAttribute"/> is purely a lifecycle hook (not
/// a test-discoverer), users still need <c>[Fact]</c> on the method itself. A combined
/// <c>[ScenarioFact]</c> attribute is deferred to M4.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScenarioAttribute : BeforeAfterTestAttribute
{
    public string? Name { get; }
    public int Seed { get; }
    public string? Fixture { get; }

    public ScenarioAttribute(string? name = null, int seed = 42, string? fixture = null)
    {
        Name = name;
        Seed = seed;
        Fixture = fixture;
    }

    public override void Before(MethodInfo methodUnderTest)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var req = new ScenarioBeginRequest
        {
            Name = Name ?? methodUnderTest.Name,
            Seed = Seed,
            Fixture = Fixture,
        };
        var p = JsonSerializer.SerializeToElement(req, ProtocolJson.Options);
        // Block the Before hook on the RPC — the framework needs the scenario established
        // before the test body runs. GetAwaiter().GetResult() is OK here; xUnit's hook
        // machinery is synchronous.
        s.InvokeAsync("scenario.begin", p, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override void After(MethodInfo methodUnderTest)
    {
        var s = SdvTestSession.Current;
        if (s is null) return;   // session torn down already; nothing to do.
        try
        {
            s.InvokeAsync("scenario.end", null, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Swallow teardown errors — xUnit is already about to report the test's
            // outcome; don't mask it with a cleanup exception.
        }
    }
}
```

- [ ] **Step 3: Run tests + CI**

Run tests + CI. Expect 283 → 285 (+2).

---

## Task 8: SdvFixture + SdvCollection

**Why:** The xUnit collection fixture that owns one SDV subprocess per test assembly, populates `SdvTestSession.Current` on startup, tears down on dispose. Reuses `SdvLauncher` + `HarnessDeployer` + `UnixSocketRpc.ConnectAsync` from the Runner project.

**Files:**
- Create: `src/Runner.Dsl/SdvFixture.cs`
- Create: `src/Runner.Dsl/SdvCollection.cs`
- Create: `tests/Runner.Dsl.Tests/SdvFixtureSmokeTests.cs`

- [ ] **Step 1: Create SdvFixture.cs**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// xUnit collection fixture that launches SDV + harness once per test assembly, connects
/// over Unix socket, initializes <see cref="SdvTestSession.Current"/>. Tears down on dispose.
/// </summary>
/// <remarks>
/// Users opt in via <c>[CollectionDefinition("SDV")]</c> + <c>[Collection("SDV")]</c>.
/// See <c>docs/dsl-quickstart.md</c>.
/// Environment knobs: <c>SDV_MODS_PATH</c> (defaults to <c>~/.cache/sdv-test-framework/mods/</c>),
/// <c>DSL_SKIP_SDV_LAUNCH</c> (set to any value in CI when no live SDV is available — the
/// fixture becomes a no-op and any <c>[Scenario]</c> test in the assembly will fail with
/// "SdvTestSession.Current is not initialized"; exists so CI doesn't hang on missing SDV).
/// </remarks>
public sealed class SdvFixture : IAsyncLifetime
{
    private Process? _sdv;
    private JsonRpcSession? _session;
    private CancellationTokenSource? _lifetimeCts;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH") is { Length: > 0 })
            return;

        _lifetimeCts = new CancellationTokenSource();
        var ct = _lifetimeCts.Token;

        var socket = Path.Combine(Path.GetTempPath(), $"sdv-dsl-{Guid.NewGuid():N}.sock");

        var modsPath = Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
        {
            modsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "mods");
        }
        Directory.CreateDirectory(modsPath);
        HarnessDeployer.Deploy(modsPath);

        _sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(60));

        for (int i = 0; i < 120 && !File.Exists(socket); i++)
            await Task.Delay(500, connectCts.Token);
        if (!File.Exists(socket))
            throw new TimeoutException("SDV never opened the DSL test socket");

        _session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
        var readyTcs = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
        _ = _session.RunAsync(ct);
        await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

        SdvTestSession.Initialize(_session);
    }

    public async Task DisposeAsync()
    {
        SdvTestSession.ResetForTests();
        try { _session?.Dispose(); } catch { }
        try
        {
            if (_sdv is { HasExited: false })
            {
                _sdv.Kill();
                _sdv.WaitForExit(5000);
            }
        } catch { }
        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Create SdvCollection.cs**

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// xUnit collection definition for DSL tests. Users reference this via
/// <c>[Collection("SDV")]</c>; the <see cref="SdvFixture"/> runs once per assembly.
/// </summary>
[CollectionDefinition("SDV")]
public class SdvCollection : ICollectionFixture<SdvFixture> { }
```

- [ ] **Step 3: Create smoke test**

Create `tests/Runner.Dsl.Tests/SdvFixtureSmokeTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class SdvFixtureSmokeTests
{
    [Fact]
    public async Task InitializeAsync_WithDslSkipSet_NoOps()
    {
        // Verifies the DSL_SKIP_SDV_LAUNCH bypass — needed for CI to run without live SDV.
        var original = Environment.GetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH");
        Environment.SetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH", "1");
        try
        {
            var fx = new SdvFixture();
            await fx.InitializeAsync();
            Assert.Null(SdvTestSession.Current);   // skip path → nothing initialized
            await fx.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH", original);
        }
    }
}
```

- [ ] **Step 4: Run tests + CI**

Note: the full CI must be invoked with `DSL_SKIP_SDV_LAUNCH=1` in the environment since we don't want DSL tests launching SDV in the default CI path. Update `scripts/ci.sh` to export this variable before the `dotnet test` step, OR add a `<TestCaseFilter>` that excludes tests requiring the fixture.

Actually the cleaner approach: DSL test classes that don't use `[Collection("SDV")]` don't need the fixture. `SdvFixtureSmokeTests` doesn't use the collection, so the fixture never runs for it. Check this: xUnit only instantiates `ICollectionFixture<T>` when a test in a `[Collection(name)]` class runs. So as long as no DSL test class outside `Worked/` uses `[Collection("SDV")]`, CI won't launch SDV.

Verify this holds: the worked example in T9 will be the only `[Collection("SDV")]` user, and it's `[Fact(Skip)]`. No SDV launch in CI. Confirm by running.

Run: `./scripts/ci.sh 2>&1 | tail -5`
Expected: PASS. Test count 285 → 286 (+1 smoke).

---

## Task 9: Worked example + docs + milestone update

**Why:** Final task — prove the DSL works against live SDV (skipped in CI, runnable manually) and ship the user-facing quickstart + M3-DSL completion record.

**Files:**
- Create: `tests/Runner.Dsl.Tests/Worked/ShopMenuDslSmoke.cs`
- Create: `tests/Runner.Dsl.Tests/DslIntegrationTests.cs`
- Create: `docs/dsl-quickstart.md`
- Modify: `docs/milestones/current.md`

- [ ] **Step 1: Create worked example**

Create `tests/Runner.Dsl.Tests/Worked/ShopMenuDslSmoke.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Worked;

/// <summary>
/// End-to-end DSL example: fixture → warp → freeze → draw snapshot + assertion. Skip-marked
/// by default (requires live SDV + harness + Xvfb); run manually via
/// <c>dotnet test tests/Runner.Dsl.Tests/ --filter Worked</c>.
/// </summary>
[Collection("SDV")]
public class ShopMenuDslSmoke
{
    [Fact(Skip = "Requires live SDV + Xvfb — run manually with --filter Worked.")]
    [Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_DrawsCursorsTexture()
    {
        await Player.Warp("FarmHouse", 8, 10);
        await Player.SetMoney(1000);
        await Draw.Arm();
        await Wait.Ms(500);
        await Freeze.Begin();

        var snap = await Draw.Snapshot();
        Assert.NotEmpty(snap.Events);
        // Cursors renders almost every frame in vanilla SDV; this assertion exercises the
        // full capture + resolve pipeline without coupling to a specific scene.
        Assert.Contains(snap.Events, e => e.TextureAsset == "LooseSprites/Cursors");
    }
}
```

- [ ] **Step 2: Create integration placeholder**

Create `tests/Runner.Dsl.Tests/DslIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

/// <summary>Integration surface for M3 DSL — exercised via the Worked example + manual smoke.</summary>
public class DslIntegrationTests
{
    [Fact(Skip = "Requires live SDV — Worked/ShopMenuDslSmoke covers end-to-end DSL round-trip.")]
    public void DslSession_RoundTrip() { }
}
```

- [ ] **Step 3: Create docs/dsl-quickstart.md**

Write `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/dsl-quickstart.md`:

```markdown
# C# DSL Quickstart

Write scenarios as xUnit test methods instead of hand-authoring `*.test.json`. The DSL
wraps the same JSON-RPC surface the CLI runner uses, so anything you can express in a
JSON scenario you can express in C#.

## 1. Add the project reference

In your mod's test project csproj:

```xml
<ProjectReference Include="..\..\path\to\sdv-test-framework\src\Runner.Dsl\Runner.Dsl.csproj" />
```

(Once the NuGet package ships in M3.3, this becomes `<PackageReference Include="SdvTestFramework.Runner.Dsl" ... />`.)

## 2. Declare the collection (once per assembly)

```csharp
using SdvTestFramework.Runner.Dsl;
// The SdvCollection type is provided by the DSL package; this line is only needed if you
// want to add your own members to the collection (rare). Most users skip this and
// reference the built-in definition via [Collection("SDV")] alone.
```

## 3. Write a test

```csharp
using SdvTestFramework.Runner.Dsl;
using Xunit;

[Collection("SDV")]
public class ShopMenuTests
{
    [Fact]
    [Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_ToShop_MenuOpens()
    {
        await Player.Warp("SeedShop", 4, 19);
        await Player.SetMoney(5000);
        await Draw.Arm();
        await Wait.Ms(500);
        await Freeze.Begin();

        var events = await Draw.Snapshot();
        Assert.Contains(events.Events, e => e.TextureAsset == "LooseSprites/Cursors");

        var player = await State.Player();
        Assert.Equal(5000, player.Money);
    }
}
```

Note: you need both `[Fact]` and `[Scenario]`. `[Fact]` tells xUnit to run the method;
`[Scenario]` tells the DSL to wrap it in `scenario.begin`/`scenario.end`. A combined
`[ScenarioFact]` is on the roadmap.

## 4. Run

```bash
dotnet test
```

By default the collection fixture launches one SDV subprocess per `dotnet test`
invocation and reuses it across every `[Collection("SDV")]` test in the assembly.

Environment knobs:

- `SDV_MODS_PATH` — override the mods directory the harness is deployed to (default:
  `~/.cache/sdv-test-framework/mods`).
- `DSL_SKIP_SDV_LAUNCH=1` — bypass SDV launch entirely. The fixture becomes a no-op and
  any `[Scenario]` test fails with "SdvTestSession.Current is not initialized." Set this
  in CI when you have DSL tests but no display/SDV available (they'll skip/fail cleanly
  rather than hang on SDV startup).

## Facet reference

- `Player.Warp(location, x, y)` / `SetMoney(amount)` / `GiveItem(id, count)`
- `Time.Advance(minutes)`
- `World.SetWeather(type)`
- `Fixture.Load(name)`
- `Freeze.Begin()` / `End()` / `Status()`
- `Draw.Arm()` / `Disarm()` / `Snapshot()` / `Find(filter)` / `AssertContains(filter)` / `AssertNotContains(filter)`
- `State.Player()` / `Time()` / `Location(name?)` / `Npc(name)` / `Menu()` / `Mods()`
- `Bitmap.Capture(region?)`
- `Wait.Ms(ms)`

## Error handling

RPC errors throw typed exceptions:

```csharp
try
{
    await Freeze.Begin();
}
catch (SdvGameStateInvalidException ex)
{
    // ex.Method = "freeze.begin"
    // ex.Code   = JsonRpcErrorCode.GameStateInvalid
    // ex.Message = "RPC 'freeze.begin' failed (GameStateInvalid): freeze.begin requires an active scenario..."
}
```

Subclasses: `SdvGameStateInvalidException`, `SdvInvalidParamsException`,
`SdvInternalErrorException`. Base: `SdvRpcException`.

## What's deferred

See the M3-DSL design spec
(`docs/superpowers/specs/2026-04-24-m3-csharp-dsl-design.md`) for what's out of scope:
FluentAssertions `.Should()` integration, generic menu registry
(`Wait.ForMenu<ShopMenu>`), `[ScenarioFact]` combined attribute, parallel SDV-subprocess
execution across multiple collections.
```

- [ ] **Step 4: Update docs/milestones/current.md**

Open `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/milestones/current.md`.

**Edit A:** Update the top-level phase heading. Find:

```markdown
## M2 — Production polish (in progress)
```

Replace with:

```markdown
## M2 — Production polish (complete 2026-04-24)

## M3 — Ecosystem (in progress)

Per spec §7 Phase 3. Decomposed into 5 subprojects + one M2-followup ("SIGTERM handler"
landed first):

0. **SIGTERM handler** (M2-followup) — background-job shutdowns trigger clean-cancel. ✓ **Landed 2026-04-24.**
1. **C# fluent DSL wrapper** (§7.3 / Appendix A) — typed static facets + `[Scenario]` attribute + xUnit collection fixture. ✓ **Landed 2026-04-24.**
2. MCP server wrapping the RPC surface — deferred.
3. NuGet package for the DSL — deferred.
4. Documentation site — deferred.
5. Example suites for 3-5 community mods — deferred.

### M3 subproject 0 — SIGTERM handler landed (2026-04-24)

Small M2-followup: `PosixSignalRegistration` for SIGTERM + SIGINT in
`src/Runner/Program.cs`, both wired to the same `CancellationTokenSource.Cancel()` the
pre-existing `Console.CancelKeyPress` hook fires. Background-job `kill %1` and
non-controlling-TTY `kill -INT` now trigger the same clean-shutdown path as foreground
Ctrl-C, which unblocks end-to-end smokes for record mode (which previously couldn't
flush the trace file on bg-job kill) and watch mode.

+2 passing tests (`PosixSignalRegistrationTests` — API availability smoke).

### M3 subproject 1 — C# fluent DSL landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-m3-csharp-dsl.md` (9 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-m3-csharp-dsl-design.md`.

**Scope:** typed ambient-static DSL in a new `src/Runner.Dsl/` project. Tests look like:

```csharp
[Collection("SDV")]
public class ShopMenuTests
{
    [Fact, Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_ShopOpens()
    {
        await Player.Warp("SeedShop", 4, 19);
        var player = await State.Player();
        Assert.Equal(5000, player.Money);
    }
}
```

**Architecture:** `SdvFixture` xUnit collection fixture launches one SDV subprocess per
assembly via the existing `SdvLauncher` + `HarnessDeployer` + `UnixSocketRpc` pipeline,
populates `SdvTestSession.Current` with a real `JsonRpcSession`. Ambient static facets
(`Player`, `Time`, `World`, `Freeze`, `Draw`, `State`, `Fixture`, `Bitmap`, `Wait`) read
through `Current` to invoke RPCs. `[Scenario]` is an xUnit `BeforeAfterTestAttribute`
subclass that wraps each test in `scenario.begin`/`scenario.end`.

**Typed exceptions:** `SdvRpcException` base + `SdvGameStateInvalidException`,
`SdvInvalidParamsException`, `SdvInternalErrorException` subclasses. RPC error responses
translate to typed exceptions that propagate as normal xUnit test failures with useful
messages and stack traces.

**User docs:** `docs/dsl-quickstart.md` — how to wire up a mod's test project.

**Worked example:** `tests/Runner.Dsl.Tests/Worked/ShopMenuDslSmoke.cs` —
`[Fact(Skip="Requires live SDV")]` by default so CI stays green. Runnable manually via
`dotnet test tests/Runner.Dsl.Tests/ --filter Worked` when a dev wants to verify against
live SDV.

**Environment knob:** `DSL_SKIP_SDV_LAUNCH=1` makes `SdvFixture` a no-op — for CI
environments without a display.

**Test count after M3-DSL:** ~282 Passed + 35 Skipped (was 266+34 before M3; +16 passed,
+1 skipped).

**Out of scope (M3 followups):**
- FluentAssertions `.Should()` integration.
- Generic menu registry (`Wait.ForMenu<ShopMenu>`).
- `World.InteractNpc` / `Time.Set` (need new RPCs).
- Combined `[ScenarioFact]` attribute + custom xUnit discoverer.
- Parallel SDV-subprocess execution across multiple collections.
- NuGet package for distribution (M3 subproject 3).
```

**Edit B:** If `scripts/ci.sh` lists test projects explicitly, T1 already added `tests/Runner.Dsl.Tests/` to it; verify.

- [ ] **Step 5: Final CI**

Run: `./scripts/ci.sh 2>&1 | tail -5`
Expected: PASS. Final test count **~282 Passed + 35 Skipped** (was 266+34 at M3 start; +16 passing, +1 skipped).

- [ ] **Step 6: Manual live-smoke (optional, developer-time verification)**

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet test tests/Runner.Dsl.Tests/ -c Release --filter Worked 2>&1 | tail -10
pkill Xvfb 2>/dev/null
```

Expected: the `[Skip]` attribute prevents execution under plain `dotnet test`. To actually run it, edit the attribute to remove `Skip=...` temporarily, or use `--filter Worked & some-other-flag`. xUnit skip semantics are sticky; document the manual-override in `dsl-quickstart.md` if needed. This step is verification-only; the default CI path just skips the test.

---

## Self-review

**1. Spec coverage:**
- New project `src/Runner.Dsl/` referencing Protocol + Runner → T1 ✓
- `SdvTestSession` + `ISdvTestInvoker` → T2 ✓
- Typed exception hierarchy (`SdvRpcException` + 3 subclasses) → T2 ✓
- All 9 facets (Player, Time, World, Freeze, Draw, State, Fixture, Bitmap, Wait) → T3, T4, T5, T6 ✓
- `[Scenario]` attribute via `BeforeAfterTestAttribute` → T7 ✓
- `SdvFixture` + `SdvCollection` with `[CollectionDefinition("SDV")]` → T8 ✓
- Worked example (skipped by default) → T9 ✓
- `docs/dsl-quickstart.md` → T9 ✓
- Milestone current.md update → T9 ✓
- Acceptance 1 (CI green ~282+35) → all tasks ✓
- Acceptance 2 (new projects built + CI picks them up) → T1 ✓
- Acceptance 3 (9 facets expose the RPC surface) → T3-T6 ✓
- Acceptance 4 ([Scenario] wraps tests in begin/end) → T7 ✓
- Acceptance 5 (collection fixture + worked example works manually) → T8 + T9 ✓
- Acceptance 6 (typed exceptions) → T2 ✓
- Acceptance 7 (dsl-quickstart.md exists) → T9 ✓
- Acceptance 8 (sample suite still 11/11) → no existing code modified; verify at T9 final CI ✓
- Acceptance 9 (M3-DSL subsection in current.md) → T9 step 4 ✓

**2. Placeholder scan:** no TBD / vague items. The `[Scenario]` attribute's synchronous `.GetAwaiter().GetResult()` in T7 is a deliberate tradeoff (xUnit `BeforeAfterTestAttribute` is sync-only); flagged via inline comment.

**3. Type consistency:**
- `ISdvTestInvoker.InvokeAsync(string method, JsonElement? params, CancellationToken ct) → Task<JsonElement>` — defined T2, used by every facet + ScenarioAttribute + SdvFixture's adapter. ✓
- `SdvTestSession.Current` static property, `SdvTestSession.InitializeForTests(ISdvTestInvoker)` test seam, `SdvTestSession.Initialize(JsonRpcSession)` production — T2 defines; T3-T8 consume. ✓
- `SdvRpcException.Create(string method, JsonRpcError err) → SdvRpcException` — T2 defines; `SdvTestSession`'s SessionInvoker consumes. ✓
- Facet method naming + parameter order consistent with the spec's API shape section. ✓
- `BitmapRegion(int X, int Y, int W, int H)` — T6 defines; `Bitmap.Capture(BitmapRegion?)` consumes. ✓
- `[Scenario(string? name = null, int seed = 42, string? fixture = null)]` — T7 defines; worked example T9 consumes. ✓

**4. Hazards:**
- xUnit collection-fixture race: if two collections in the same assembly run in parallel, they'd both try to initialize `SdvTestSession.Current` (which throws on second initialize). Spec out-of-scope explicitly defers multi-collection parallel support. Document in the dsl-quickstart as a known limitation. If a user hits this, the error message is clear ("already initialized").
- `SdvFixture.DisposeAsync` swallows kill errors to avoid masking test failures during teardown. Same pattern used elsewhere in the codebase (RunCommand's finally block). No regression.
- `[Scenario]`'s sync RPC call via `.GetAwaiter().GetResult()` can deadlock in certain sync contexts (e.g., UI thread). xUnit's test-invocation machinery runs on the thread pool, so deadlock isn't expected; but flag as a known risk if anyone complains.
- If the harness isn't deployed when the fixture launches SDV, `HarnessDeployer.Deploy` creates + fills the dir. Matches `RunCommand`'s behavior. No new risk.
- The `DSL_SKIP_SDV_LAUNCH` env var is a debug affordance; users who forget to unset it in their local shell will find `[Scenario]` tests fail with "SdvTestSession not initialized." The error message is explicit enough to diagnose quickly.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-24-m3-csharp-dsl.md`. Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review. Proven across M1/M2.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
