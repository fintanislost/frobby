# M3 MCP Server — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green**. T8's extra gates:
> - New projects `src/Runner.Mcp/` + `tests/Runner.Mcp.Tests/` build clean under `TreatWarningsAsErrors=true`.
> - `docs/mcp-quickstart.md` exists.
> - `echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"t"},"capabilities":{}}}' | dotnet run --project src/Runner -c Release --no-build -- mcp` returns a valid `initialize` response on stdout.
> - `./scripts/run-samples.sh` still 11/11 PASS (no regression).

**Goal:** Ship a stdio MCP server as the new `sdv-test mcp` subcommand. LLMs configure Claude Code via `.mcp.json` pointing at `sdv-test mcp`; the server speaks Anthropic's Model Context Protocol (JSON-RPC 2.0 over stdio with NDJSON framing) and exposes 7 tools — 6 curated helpers (`run_scenario`, `list_scenarios`, `list_fixtures`, `warp_and_assert_draw`, `capture_state`, `scaffold_scenario`) + 1 raw RPC passthrough (`rpc_call`).

**Architecture:** New `src/Runner.Mcp/` project (net10) references `src/Protocol/` (reuses `NdjsonCodec`, `JsonRpcRequest/Response/Error` — MCP is the same JSON-RPC 2.0 shape, just stdio-framed) and `src/Runner/` (reuses `SdvLauncher` + `HarnessDeployer` for lazy SDV launch). `SdvLifecycle` singleton owns the SDV subprocess + live session; first tool call that needs SDV triggers launch, stdio EOF tears down. Tools that don't need SDV (`list_*`, `scaffold_scenario`) never trigger launch.

**Tech Stack:**
- .NET 10 (Runner TFM) — matches Runner + Runner.Dsl.
- `System.Text.Json` (BCL) — no new dependencies.
- xunit 2.9.0 / Microsoft.NET.Test.Sdk 17.10.0 — match prior projects.
- MCP protocol version `2024-11-05` (widely-supported stable revision at time of writing).

**Design spec:** `docs/superpowers/specs/2026-04-24-m3-mcp-server-design.md`

---

## File structure

**New source project (`src/Runner.Mcp/`):**
- `Runner.Mcp.csproj` — net10, references Protocol + Runner, enables InternalsVisibleTo for tests.
- `McpServer.cs` — main loop: read stdin line, parse JSON-RPC, dispatch to handler (`initialize`, `tools/list`, `tools/call`, `ping`, `notifications/initialized`), write response to stdout.
- `McpCapabilities.cs` — static `BuildInitializeResult()` returning the server info + tools capability JSON.
- `ITool.cs` — interface: `Name`, `Description`, `InputSchema` (JsonElement), `InvokeAsync(JsonElement args, SdvLifecycle, CancellationToken) → Task<McpToolResult>`.
- `McpToolResult.cs` — record `(JsonElement TextContent, bool IsError)`. Helpers `.Success(obj)` and `.Error(message)`.
- `ToolRegistry.cs` — `Register(ITool)`, `Get(name) → ITool?`, `All() → IReadOnlyList<ITool>`.
- `McpError.cs` — static factory: `InvalidRequest`, `MethodNotFound(name)`, `InvalidParams(msg)`, `InternalError(msg)` — each builds a `JsonRpcError` with the standard `-32xxx` code.
- `SdvLifecycle.cs` — lazy SDV launcher. `EnsureRunningAsync(ct) → Task<JsonRpcSession>`. Teardown via `DisposeAsync()`. Thread-safe via a private `SemaphoreSlim`.
- `Tools/RpcCallTool.cs`
- `Tools/ListScenariosTool.cs`
- `Tools/ListFixturesTool.cs`
- `Tools/ScaffoldScenarioTool.cs`
- `Tools/RunScenarioTool.cs`
- `Tools/WarpAndAssertDrawTool.cs`
- `Tools/CaptureStateTool.cs`

**New test project (`tests/Runner.Mcp.Tests/`):**
- `Runner.Mcp.Tests.csproj`
- `McpServerTests.cs` — 3 tests.
- `ToolRegistryTests.cs` — 2 tests.
- `Tools/IntrospectionToolTests.cs` — 2 tests (list_scenarios, list_fixtures).
- `Tools/ScaffoldScenarioToolTests.cs` — 1 test.
- `Tools/RpcCallToolTests.cs` — 2 tests.
- `Tools/StatefulToolsTests.cs` — 2 tests.
- `McpIntegrationTests.cs` — 1 `[Fact(Skip=...)]` placeholder.
- `Worked/manual-smoke.sh` — bash script for dev-side end-to-end verification.

**New files (`src/Runner/Commands/`):**
- `McpCommand.cs` — `static Task<int> RunAsync(ReadOnlyMemory<string>, CancellationToken)` that instantiates `McpServer` and pipes stdin/stdout.

**Modified files:**
- `src/Runner/Program.cs` — `"mcp" =>` dispatch + help-text addition.
- `docs/mcp-quickstart.md` (new) — `.mcp.json` setup + tool-surface reference.
- `docs/milestones/current.md` — M3-MCP completion subsection.

**Starting test count:** 286 Passed + 36 Skipped.
**Target test count after M3-MCP ships:** ~298 Passed + 37 Skipped (+12 passing, +1 skipped).

---

## Task 1: Project scaffolding

**Why:** Create the csproj files + a placeholder so T2 can land the real types. Pattern matches DSL-T1 exactly.

**Files:**
- Create: `src/Runner.Mcp/Runner.Mcp.csproj`
- Create: `src/Runner.Mcp/_Placeholder.cs`
- Create: `tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj`
- Create: `tests/Runner.Mcp.Tests/PlaceholderTests.cs`
- Create: `tests/Runner.Mcp.Tests/AssemblyInfo.cs` (pre-emptive parallelization disable, same reasoning as DSL tests — the MCP server's SdvLifecycle is a shared static-ish accessor).

- [ ] **Step 1: Runner.Mcp.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SdvTestFramework.Runner.Mcp</RootNamespace>
    <AssemblyName>Runner.Mcp</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Protocol\Protocol.csproj" />
    <ProjectReference Include="..\Runner\Runner.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Runner.Mcp.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Placeholder source**

```csharp
namespace SdvTestFramework.Runner.Mcp;

// Removed by Task 2 once McpServer lands.
internal static class _Placeholder { internal const string Marker = "mcp-scaffolding"; }
```

- [ ] **Step 3: Runner.Mcp.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SdvTestFramework.Runner.Mcp.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Runner.Mcp\Runner.Mcp.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Placeholder test + AssemblyInfo**

`tests/Runner.Mcp.Tests/PlaceholderTests.cs`:

```csharp
using Xunit;
using SdvTestFramework.Runner.Mcp;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class PlaceholderTests
{
    [Fact]
    public void McpAssemblyReferenceWires()
    {
        Assert.Equal("mcp-scaffolding", _Placeholder.Marker);
    }
}
```

`tests/Runner.Mcp.Tests/AssemblyInfo.cs`:

```csharp
using Xunit;

// MCP tests share static state via SdvLifecycle (once it lands in T4). Disable parallelization
// at the assembly level — same approach used by Runner.Dsl.Tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

- [ ] **Step 5: Verify ci.sh picks up the new project**

`scripts/ci.sh` already uses `tests/*/*.Tests.csproj` glob (added in DSL-T1). New project auto-picked-up.

- [ ] **Step 6: CI**

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`

Expected: 5 "Passed:" lines summing to **287**. Skipped stays at 36. (New Runner.Mcp.Tests project contributes 1 placeholder.)

---

## Task 2: MCP protocol primitives (server loop + initialize + tools/list)

**Why:** Stand up the MCP JSON-RPC server loop with enough surface to respond to `initialize` + `tools/list` against an empty tool registry. T3 wires `tools/call` dispatch; T4-T6 add tools.

**Files:**
- Create: `src/Runner.Mcp/ITool.cs`
- Create: `src/Runner.Mcp/McpToolResult.cs`
- Create: `src/Runner.Mcp/ToolRegistry.cs`
- Create: `src/Runner.Mcp/McpCapabilities.cs`
- Create: `src/Runner.Mcp/McpError.cs`
- Create: `src/Runner.Mcp/McpServer.cs`
- Create: `tests/Runner.Mcp.Tests/McpServerTests.cs`
- Create: `tests/Runner.Mcp.Tests/ToolRegistryTests.cs`
- Delete: `src/Runner.Mcp/_Placeholder.cs` + `tests/Runner.Mcp.Tests/PlaceholderTests.cs`

- [ ] **Step 1: Write failing tests (red phase)**

`tests/Runner.Mcp.Tests/ToolRegistryTests.cs`:

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class ToolRegistryTests
{
    private sealed class StubTool : ITool
    {
        public string Name { get; init; } = "stub";
        public string Description => "stub";
        public JsonElement InputSchema => JsonDocument.Parse("{\"type\":\"object\"}").RootElement;
        public Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? life, CancellationToken ct)
            => Task.FromResult(McpToolResult.Success(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Get_ExistingName_ReturnsTool()
    {
        var reg = new ToolRegistry();
        reg.Register(new StubTool { Name = "foo" });
        var tool = reg.Get("foo");
        Assert.NotNull(tool);
        Assert.Equal("foo", tool!.Name);
    }

    [Fact]
    public void Get_UnknownName_ReturnsNull()
    {
        var reg = new ToolRegistry();
        Assert.Null(reg.Get("nope"));
    }
}
```

`tests/Runner.Mcp.Tests/McpServerTests.cs`:

```csharp
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class McpServerTests
{
    private static async Task<string[]> RunServerWith(string input)
    {
        var inBytes = Encoding.UTF8.GetBytes(input);
        using var stdin = new MemoryStream(inBytes);
        using var stdout = new MemoryStream();
        var server = new McpServer(new ToolRegistry(), lifecycle: null);
        await server.RunAsync(stdin, stdout, CancellationToken.None);
        var outStr = Encoding.UTF8.GetString(stdout.ToArray());
        return outStr.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo_AndToolsCapability()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\"},\"capabilities\":{}}}\n";
        var lines = await RunServerWith(req);

        Assert.Single(lines);
        var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
        Assert.Equal("sdv-test-mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task ToolsList_ReturnsRegisteredTools()
    {
        var reg = new ToolRegistry();
        // Register would happen in production; here we just test the empty case.
        var inBytes = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}\n");
        using var stdin = new MemoryStream(inBytes);
        using var stdout = new MemoryStream();
        var server = new McpServer(reg, lifecycle: null);
        await server.RunAsync(stdin, stdout, CancellationToken.None);

        var outStr = Encoding.UTF8.GetString(stdout.ToArray());
        var line = outStr.Trim();
        var doc = JsonDocument.Parse(line);
        Assert.Equal(2, doc.RootElement.GetProperty("id").GetInt32());
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        Assert.Equal(0, tools.GetArrayLength());
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsMethodNotFound()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"nope\",\"arguments\":{}}}\n";
        var lines = await RunServerWith(req);

        var doc = JsonDocument.Parse(lines[0]);
        var err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32601, err.GetProperty("code").GetInt32());
        Assert.Contains("nope", err.GetProperty("message").GetString());
    }
}
```

Run: expect compile failure (`McpServer`, `ToolRegistry`, etc. don't exist).

- [ ] **Step 2: ITool.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>MCP tool contract. Each tool is stateless; state lives in <see cref="SdvLifecycle"/>.</summary>
public interface ITool
{
    /// <summary>Tool name — unique within a <see cref="ToolRegistry"/>.</summary>
    string Name { get; }

    /// <summary>Human-readable description, shown in tools/list responses.</summary>
    string Description { get; }

    /// <summary>JSON Schema describing the tool's <c>arguments</c> object.</summary>
    JsonElement InputSchema { get; }

    /// <summary>Invoke the tool. <paramref name="lifecycle"/> is null for tools that don't need SDV.</summary>
    Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? lifecycle, CancellationToken ct);
}
```

- [ ] **Step 3: McpToolResult.cs**

```csharp
using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>Result of a single tool invocation. Wraps the MCP <c>{content, isError}</c> shape.</summary>
public readonly record struct McpToolResult(string Text, bool IsError)
{
    /// <summary>Success result — serialize <paramref name="obj"/> as JSON string.</summary>
    public static McpToolResult Success(JsonElement obj) => new(obj.GetRawText(), false);

    /// <summary>Success result from an already-serialized JSON string.</summary>
    public static McpToolResult SuccessText(string text) => new(text, false);

    /// <summary>Error result — the LLM sees <paramref name="message"/> in the tool output.</summary>
    public static McpToolResult Error(string message) => new(message, true);
}
```

- [ ] **Step 4: ToolRegistry.cs**

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>Name-indexed tool lookup. Registered once at server startup; read-only thereafter.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public void Register(ITool tool) => _tools[tool.Name] = tool;

    public ITool? Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;

    public IReadOnlyList<ITool> All() => _tools.Values.ToArray();
}
```

Add `using System.Linq;` at the top.

- [ ] **Step 5: McpCapabilities.cs**

```csharp
using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp;

internal static class McpCapabilities
{
    public const string ProtocolVersion = "2024-11-05";
    public const string ServerName = "sdv-test-mcp";
    public const string ServerVersion = "0.1.0";

    /// <summary>Body of the <c>initialize</c> response's <c>result</c> field.</summary>
    public static JsonElement BuildInitializeResult()
    {
        var json = $"{{\"protocolVersion\":\"{ProtocolVersion}\"," +
                   $"\"serverInfo\":{{\"name\":\"{ServerName}\",\"version\":\"{ServerVersion}\"}}," +
                   $"\"capabilities\":{{\"tools\":{{}}}}}}";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Serialize the registry as an MCP <c>tools/list</c> response body.</summary>
    public static JsonElement BuildToolsList(ToolRegistry registry)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"tools\":[");
        bool first = true;
        foreach (var t in registry.All())
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"name\":");
            sb.Append(JsonSerializer.Serialize(t.Name));
            sb.Append(",\"description\":");
            sb.Append(JsonSerializer.Serialize(t.Description));
            sb.Append(",\"inputSchema\":");
            sb.Append(t.InputSchema.GetRawText());
            sb.Append('}');
        }
        sb.Append("]}");
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }
}
```

- [ ] **Step 6: McpError.cs**

```csharp
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

internal static class McpError
{
    // MCP uses standard JSON-RPC 2.0 error codes.
    public static JsonRpcError InvalidRequest(string message = "Invalid Request")
        => new((JsonRpcErrorCode)(-32600), message);

    public static JsonRpcError MethodNotFound(string method)
        => new((JsonRpcErrorCode)(-32601), $"Method not found: {method}");

    public static JsonRpcError InvalidParams(string message)
        => new((JsonRpcErrorCode)(-32602), $"Invalid params: {message}");

    public static JsonRpcError InternalError(string message)
        => new((JsonRpcErrorCode)(-32603), $"Internal error: {message}");
}
```

- [ ] **Step 7: McpServer.cs**

```csharp
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>
/// MCP stdio server. Reads newline-delimited JSON-RPC 2.0 requests from <c>stdin</c>,
/// dispatches to the registered tool or built-in handler, writes NDJSON responses to
/// <c>stdout</c>. <c>stderr</c> is available for diagnostic logs (not used by MVP).
/// </summary>
public sealed class McpServer
{
    private readonly ToolRegistry _tools;
    private readonly SdvLifecycle? _lifecycle;

    public McpServer(ToolRegistry tools, SdvLifecycle? lifecycle)
    {
        _tools = tools;
        _lifecycle = lifecycle;
    }

    public async Task RunAsync(Stream stdin, Stream stdout, CancellationToken ct)
    {
        using var reader = new StreamReader(stdin, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        string? line;
        while ((line = await reader.ReadLineAsync().WaitAsync(ct)) is not null)
        {
            JsonRpcRequest? req;
            try { req = JsonSerializer.Deserialize<JsonRpcRequest>(line, ProtocolJson.Options); }
            catch { await WriteErrorAsync(writer, null, McpError.InvalidRequest(), ct); continue; }

            if (req is null) { await WriteErrorAsync(writer, null, McpError.InvalidRequest(), ct); continue; }

            // Notifications (id == null) expect no response.
            if (req.Id is null)
            {
                // Handle notifications/initialized silently; everything else also silently.
                continue;
            }

            await DispatchAsync(writer, req, ct);
        }

        if (_lifecycle is not null) await _lifecycle.DisposeAsync();
    }

    private async Task DispatchAsync(StreamWriter writer, JsonRpcRequest req, CancellationToken ct)
    {
        try
        {
            switch (req.Method)
            {
                case "initialize":
                    await WriteResultAsync(writer, req.Id!, McpCapabilities.BuildInitializeResult(), ct);
                    return;

                case "ping":
                    await WriteResultAsync(writer, req.Id!, JsonDocument.Parse("{}").RootElement, ct);
                    return;

                case "tools/list":
                    await WriteResultAsync(writer, req.Id!, McpCapabilities.BuildToolsList(_tools), ct);
                    return;

                case "tools/call":
                    await DispatchToolCallAsync(writer, req, ct);
                    return;

                default:
                    await WriteErrorAsync(writer, req.Id, McpError.MethodNotFound(req.Method), ct);
                    return;
            }
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(writer, req.Id, McpError.InternalError(ex.Message), ct);
        }
    }

    private async Task DispatchToolCallAsync(StreamWriter writer, JsonRpcRequest req, CancellationToken ct)
    {
        if (req.Params is not { ValueKind: JsonValueKind.Object } p)
        {
            await WriteErrorAsync(writer, req.Id, McpError.InvalidParams("'params' must be an object"), ct);
            return;
        }
        if (!p.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
        {
            await WriteErrorAsync(writer, req.Id, McpError.InvalidParams("'name' is required"), ct);
            return;
        }
        var name = nameEl.GetString()!;
        var tool = _tools.Get(name);
        if (tool is null)
        {
            await WriteErrorAsync(writer, req.Id, McpError.MethodNotFound(name), ct);
            return;
        }
        var args = p.TryGetProperty("arguments", out var a) ? a : JsonDocument.Parse("{}").RootElement;

        McpToolResult result;
        try { result = await tool.InvokeAsync(args, _lifecycle, ct); }
        catch (Exception ex) { result = McpToolResult.Error($"tool '{name}' threw: {ex.Message}"); }

        var wrappedJson = "{\"content\":[{\"type\":\"text\",\"text\":" +
            JsonSerializer.Serialize(result.Text) + "}]" +
            (result.IsError ? ",\"isError\":true" : "") + "}";
        await WriteResultAsync(writer, req.Id!, JsonDocument.Parse(wrappedJson).RootElement, ct);
    }

    private static async Task WriteResultAsync(StreamWriter writer, object id, JsonElement result, CancellationToken ct)
    {
        var resp = new JsonRpcResponse { Id = id, Result = result };
        var line = JsonSerializer.Serialize(resp, ProtocolJson.Options);
        await writer.WriteLineAsync(line.AsMemory(), ct);
    }

    private static async Task WriteErrorAsync(StreamWriter writer, object? id, JsonRpcError error, CancellationToken ct)
    {
        var resp = new JsonRpcResponse { Id = id ?? 0, Error = error };
        var line = JsonSerializer.Serialize(resp, ProtocolJson.Options);
        await writer.WriteLineAsync(line.AsMemory(), ct);
    }
}
```

Note: `JsonRpcResponse` shape comes from `SdvTestFramework.Protocol`. Verify by reading `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Protocol/` — if the serialization shape doesn't exactly match MCP's (e.g., `result`/`error`/`id`), adjust to match. The plan assumes the existing `JsonRpcResponse` already serializes correctly for MCP since both are standard JSON-RPC 2.0.

- [ ] **Step 8: Delete placeholders**

Delete `src/Runner.Mcp/_Placeholder.cs` + `tests/Runner.Mcp.Tests/PlaceholderTests.cs`.

- [ ] **Step 9: CI**

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: Runner.Mcp.Tests goes 1 → 5 (−1 placeholder + 3 server + 2 registry). Total 287 → 291.

---

## Task 3: SdvLifecycle + rpc_call passthrough tool

**Why:** `rpc_call` is the simplest tool that actually uses SDV — it forwards one RPC and returns the result. Landing it first exercises the full path from MCP tool → SdvLifecycle → harness RPC, which de-risks T5 + T6.

**Files:**
- Create: `src/Runner.Mcp/SdvLifecycle.cs`
- Create: `src/Runner.Mcp/Tools/RpcCallTool.cs`
- Create: `tests/Runner.Mcp.Tests/Tools/RpcCallToolTests.cs`

- [ ] **Step 1: Failing tests**

`tests/Runner.Mcp.Tests/Tools/RpcCallToolTests.cs`:

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class RpcCallToolTests
{
    private sealed class FakeLifecycle : SdvLifecycle
    {
        public string? LastMethod { get; private set; }
        public string? LastParams { get; private set; }
        public JsonElement NextResult { get; set; } = JsonDocument.Parse("{\"ok\":true}").RootElement;
        public JsonRpcError? NextError { get; set; }

        internal override Task<JsonElement> InvokeAsyncForTests(string method, JsonElement? p, CancellationToken ct)
        {
            LastMethod = method;
            LastParams = p?.GetRawText();
            if (NextError is { } e) throw SdvRpcException.Create(method, e);
            return Task.FromResult(NextResult);
        }
    }

    [Fact]
    public async Task Dispatch_ForwardsToSession()
    {
        var life = new FakeLifecycle { NextResult = JsonDocument.Parse("{\"tick\":42}").RootElement };
        var tool = new RpcCallTool();
        var args = JsonDocument.Parse("{\"method\":\"state.player\",\"params\":{}}").RootElement;

        var result = await tool.InvokeAsync(args, life, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("\"tick\":42", result.Text);
        Assert.Equal("state.player", life.LastMethod);
    }

    [Fact]
    public async Task Error_MapsToMcpError()
    {
        var life = new FakeLifecycle
        {
            NextError = new JsonRpcError(JsonRpcErrorCode.GameStateInvalid, "no scenario"),
        };
        var tool = new RpcCallTool();
        var args = JsonDocument.Parse("{\"method\":\"freeze.begin\"}").RootElement;

        var result = await tool.InvokeAsync(args, life, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("freeze.begin", result.Text);
        Assert.Contains("no scenario", result.Text);
    }
}
```

Run: expect FAIL (`SdvLifecycle`, `RpcCallTool` don't exist).

- [ ] **Step 2: SdvLifecycle.cs**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner;

namespace SdvTestFramework.Runner.Mcp;

/// <summary>
/// Lazy SDV launcher for the MCP server. First tool call that needs a session triggers
/// launch; subsequent calls reuse. Thread-safe via a semaphore (stdio dispatch is serial
/// today but MCP clients may pipeline).
/// </summary>
public class SdvLifecycle : IAsyncDisposable
{
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private Process? _sdv;
    private JsonRpcSession? _session;

    /// <summary>Ensure SDV is running + a session is connected; return the session.</summary>
    public virtual async Task<JsonRpcSession> EnsureRunningAsync(CancellationToken ct)
    {
        if (_session is not null) return _session;

        await _launchLock.WaitAsync(ct);
        try
        {
            if (_session is not null) return _session;

            var socket = Path.Combine(Path.GetTempPath(), $"sdv-mcp-{Guid.NewGuid():N}.sock");

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
                throw new TimeoutException("SDV never opened the MCP test socket");

            _session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = _session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            return _session;
        }
        finally { _launchLock.Release(); }
    }

    /// <summary>Test-only invocation seam (override in shim).</summary>
    internal virtual async Task<JsonElement> InvokeAsyncForTests(string method, JsonElement? p, CancellationToken ct)
    {
        var session = await EnsureRunningAsync(ct);
        var resp = await session.InvokeAsync(method, p, ct);
        if (resp.Error is { } e) throw SdvRpcException.Create(method, e);
        return resp.Result ?? JsonDocument.Parse("{}").RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        try { _session?.Dispose(); } catch { }
        try
        {
            if (_sdv is { HasExited: false })
            {
                _sdv.Kill();
                _sdv.WaitForExit(5000);
            }
        } catch { }
        _launchLock.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>Typed exception for RPC errors surfaced through the MCP server. Mirrors DSL's exceptions.</summary>
public sealed class SdvRpcException : Exception
{
    public string Method { get; }
    public JsonRpcErrorCode Code { get; }

    public SdvRpcException(string method, JsonRpcErrorCode code, string message)
        : base($"RPC '{method}' failed ({code}): {message}")
    {
        Method = method;
        Code = code;
    }

    public static SdvRpcException Create(string method, JsonRpcError error)
        => new(method, error.Code, error.Message);
}
```

Note: We re-declare `SdvRpcException` here rather than reach into `src/Runner.Dsl/` because the MCP server shouldn't depend on the DSL. Duplication is ~15 lines and keeps the dependency chain clean.

- [ ] **Step 3: RpcCallTool.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>
/// Raw JSON-RPC passthrough. Forwards <c>args.method</c> + <c>args.params</c> to the
/// harness and returns the result. The escape hatch for workflows the curated tools
/// don't cover.
/// </summary>
public sealed class RpcCallTool : ITool
{
    public string Name => "rpc_call";

    public string Description =>
        "Raw passthrough to any harness JSON-RPC method. Use when no curated tool fits. " +
        "Example: {\"method\":\"state.player\"} returns the current player state.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {
          "type":"object",
          "properties":{
            "method":{"type":"string","description":"JSON-RPC method name (e.g. 'state.player', 'player.warp')"},
            "params":{"type":"object","description":"Optional method parameters"}
          },
          "required":["method"]
        }
        """).RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? life, CancellationToken ct)
    {
        if (!args.TryGetProperty("method", out var m) || m.ValueKind != JsonValueKind.String)
            return McpToolResult.Error("'method' is required");
        if (life is null)
            return McpToolResult.Error("SDV lifecycle not available — internal server misconfiguration");

        var method = m.GetString()!;
        JsonElement? p = args.TryGetProperty("params", out var pe) ? pe : null;

        try
        {
            var result = await life.InvokeAsyncForTests(method, p, ct);
            return McpToolResult.Success(result);
        }
        catch (SdvRpcException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
    }
}
```

- [ ] **Step 4: CI**

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: Runner.Mcp.Tests 5 → 7 (+2). Total 291 → 293.

---

## Task 4: Introspection tools — list_scenarios, list_fixtures, scaffold_scenario

**Why:** Three tools that do pure filesystem work — no SDV needed. Fastest to implement + test, gets half of the curated surface done in one task.

**Files:**
- Create: `src/Runner.Mcp/Tools/ListScenariosTool.cs`
- Create: `src/Runner.Mcp/Tools/ListFixturesTool.cs`
- Create: `src/Runner.Mcp/Tools/ScaffoldScenarioTool.cs`
- Create: `tests/Runner.Mcp.Tests/Tools/IntrospectionToolTests.cs`
- Create: `tests/Runner.Mcp.Tests/Tools/ScaffoldScenarioToolTests.cs`

- [ ] **Step 1: Failing tests**

`tests/Runner.Mcp.Tests/Tools/IntrospectionToolTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class IntrospectionToolTests
{
    [Fact]
    public async Task ListScenarios_GlobsDirectory()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-list-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "a.test.json"), "{\"name\":\"A\",\"steps\":[],\"fixture\":\"f1\"}");
            File.WriteAllText(Path.Combine(tmp, "b.test.json"), "{\"name\":\"B\",\"steps\":[]}");
            File.WriteAllText(Path.Combine(tmp, "not-a-test.txt"), "ignore");

            var tool = new ListScenariosTool();
            var args = JsonDocument.Parse($"{{\"dir\":{JsonSerializer.Serialize(tmp)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("\"name\":\"A\"", result.Text);
            Assert.Contains("\"name\":\"B\"", result.Text);
            Assert.Contains("\"fixture\":\"f1\"", result.Text);
            Assert.DoesNotContain("not-a-test", result.Text);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task ListFixtures_ReadsMetaJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-fx-{Guid.NewGuid():N}");
        var fxDir = Path.Combine(tmp, "tests", "fixtures", "myfixture");
        Directory.CreateDirectory(fxDir);
        try
        {
            File.WriteAllText(Path.Combine(fxDir, ".meta.json"),
                "{\"name\":\"myfixture\",\"sdv_version\":\"1.6.15\",\"description\":\"test fixture\"}");

            var tool = new ListFixturesTool();
            var args = JsonDocument.Parse($"{{\"root\":{JsonSerializer.Serialize(Path.Combine(tmp, "tests", "fixtures"))}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("\"name\":\"myfixture\"", result.Text);
            Assert.Contains("test fixture", result.Text);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
```

`tests/Runner.Mcp.Tests/Tools/ScaffoldScenarioToolTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp.Tools;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class ScaffoldScenarioToolTests
{
    [Fact]
    public async Task Scaffold_WritesStarterJsonAcceptedByScenarioLoader()
    {
        var tmpOut = Path.Combine(Path.GetTempPath(), $"mcp-scaf-{Guid.NewGuid():N}.test.json");
        try
        {
            var tool = new ScaffoldScenarioTool();
            var args = JsonDocument.Parse($"{{\"name\":\"probe_menu\",\"fixture\":\"m0spike_436515781\",\"output\":{JsonSerializer.Serialize(tmpOut)}}}").RootElement;
            var result = await tool.InvokeAsync(args, lifecycle: null, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.True(File.Exists(tmpOut));

            // Valid against schemas/scenario.schema.json.
            var spec = ScenarioLoader.Load(tmpOut);
            Assert.Equal("probe_menu", spec.Name);
            Assert.Equal("m0spike_436515781", spec.Fixture);
        }
        finally { if (File.Exists(tmpOut)) File.Delete(tmpOut); }
    }
}
```

- [ ] **Step 2: ListScenariosTool.cs**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Enumerate <c>*.test.json</c> files in a directory; return path + name + fixture.</summary>
public sealed class ListScenariosTool : ITool
{
    public string Name => "list_scenarios";
    public string Description =>
        "List .test.json scenario files in a directory (recursive). Returns path, name, and fixture for each.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"dir":{"type":"string","description":"Directory to scan (default: cwd)"}}}
        """).RootElement;

    public Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? _, CancellationToken ct)
    {
        var dir = args.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()!
            : Directory.GetCurrentDirectory();

        if (!Directory.Exists(dir))
            return Task.FromResult(McpToolResult.Error($"directory not found: {dir}"));

        var arr = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(dir, "*.test.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(path);
                var node = JsonNode.Parse(json)!;
                var name = node["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
                var fixture = node["fixture"]?.GetValue<string>();

                var entry = new JsonObject { ["path"] = path, ["name"] = name };
                if (fixture is not null) entry["fixture"] = fixture;
                arr.Add(entry);
            }
            catch { /* skip unparseable files */ }
        }
        var result = new JsonObject { ["scenarios"] = arr };
        return Task.FromResult(McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement));
    }
}
```

- [ ] **Step 3: ListFixturesTool.cs**

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Enumerate fixtures under <c>tests/fixtures/</c>; return name + version + description from each <c>.meta.json</c>.</summary>
public sealed class ListFixturesTool : ITool
{
    public string Name => "list_fixtures";
    public string Description =>
        "List available save fixtures under tests/fixtures/ (reads each fixture's .meta.json).";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"root":{"type":"string","description":"Fixtures root (default: ./tests/fixtures)"}}}
        """).RootElement;

    public Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? _, CancellationToken ct)
    {
        var root = args.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()!
            : Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures");

        var arr = new JsonArray();
        if (Directory.Exists(root))
        {
            foreach (var fxDir in Directory.EnumerateDirectories(root))
            {
                var metaPath = Path.Combine(fxDir, ".meta.json");
                if (!File.Exists(metaPath)) continue;
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(metaPath))!;
                    var entry = new JsonObject
                    {
                        ["name"] = node["name"]?.GetValue<string>() ?? Path.GetFileName(fxDir),
                    };
                    if (node["sdv_version"] is { } v) entry["sdv_version"] = v.GetValue<string>();
                    if (node["description"] is { } d) entry["description"] = d.GetValue<string>();
                    arr.Add(entry);
                }
                catch { /* skip bad meta */ }
            }
        }
        var result = new JsonObject { ["fixtures"] = arr };
        return Task.FromResult(McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement));
    }
}
```

- [ ] **Step 4: ScaffoldScenarioTool.cs**

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Write a starter <c>.test.json</c> at the given path (default: <c>tests/samples/&lt;name&gt;.test.json</c>).</summary>
public sealed class ScaffoldScenarioTool : ITool
{
    public string Name => "scaffold_scenario";
    public string Description =>
        "Generate a starter .test.json skeleton. Optional 'template' (shop|menu|warp) pre-fills steps.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object",
         "properties":{
           "name":{"type":"string","description":"Scenario name"},
           "fixture":{"type":"string","description":"Optional fixture name"},
           "template":{"type":"string","enum":["shop","menu","warp"],"description":"Optional step template"},
           "output":{"type":"string","description":"Explicit output path (default: tests/samples/<name>.test.json)"}
         },
         "required":["name"]}
        """).RootElement;

    public Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? _, CancellationToken ct)
    {
        if (!args.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String)
            return Task.FromResult(McpToolResult.Error("'name' is required"));
        var name = n.GetString()!;
        string? fixture = args.TryGetProperty("fixture", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        string? template = args.TryGetProperty("template", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        string output = args.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String
            ? o.GetString()!
            : Path.Combine(Directory.GetCurrentDirectory(), "tests", "samples", $"{name}.test.json");

        var steps = BuildTemplateSteps(template);
        var obj = new JsonObject
        {
            ["name"] = name,
            ["config"] = new JsonObject { ["seed"] = 42 },
            ["steps"] = steps,
            ["assertions"] = new JsonArray(),
        };
        if (fixture is not null) obj["fixture"] = fixture;

        var dir = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(output, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var result = new JsonObject { ["path"] = output };
        return Task.FromResult(McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement));
    }

    private static JsonArray BuildTemplateSteps(string? template) => template switch
    {
        "shop" => new JsonArray
        {
            Step("player.warp", new JsonObject { ["location"] = "SeedShop", ["x"] = 4, ["y"] = 19 }),
            Step("wait.ms",     new JsonObject { ["ms"] = 500 }),
        },
        "menu" => new JsonArray
        {
            Step("draw.arm",     new JsonObject()),
            Step("wait.ms",      new JsonObject { ["ms"] = 500 }),
            Step("freeze.begin", new JsonObject()),
        },
        "warp" => new JsonArray
        {
            Step("player.warp", new JsonObject { ["location"] = "Farm", ["x"] = 64, ["y"] = 15 }),
        },
        _ => new JsonArray(),
    };

    private static JsonObject Step(string action, JsonObject args) =>
        new() { ["action"] = action, ["args"] = args };
}
```

- [ ] **Step 5: Register the tools in the server**

In `src/Runner.Mcp/McpServer.cs` — add a static `BuildRegistry()` helper that the CLI entry can use. Add this static method to the class:

```csharp
/// <summary>Build the default tool registry — all MVP tools registered.</summary>
public static ToolRegistry BuildRegistry()
{
    var reg = new ToolRegistry();
    reg.Register(new Tools.RpcCallTool());
    reg.Register(new Tools.ListScenariosTool());
    reg.Register(new Tools.ListFixturesTool());
    reg.Register(new Tools.ScaffoldScenarioTool());
    // T5 adds: run_scenario, warp_and_assert_draw, capture_state.
    return reg;
}
```

(T5 will extend this with the 3 remaining stateful tools.)

- [ ] **Step 6: CI**

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: Runner.Mcp.Tests 7 → 10 (+3). Total 293 → 296.

---

## Task 5: Stateful tools — run_scenario, warp_and_assert_draw, capture_state

**Why:** The three remaining curated tools, all requiring a live SDV session. Share the shim pattern from T3's RpcCallToolTests.

**Files:**
- Create: `src/Runner.Mcp/Tools/RunScenarioTool.cs`
- Create: `src/Runner.Mcp/Tools/WarpAndAssertDrawTool.cs`
- Create: `src/Runner.Mcp/Tools/CaptureStateTool.cs`
- Create: `tests/Runner.Mcp.Tests/Tools/StatefulToolsTests.cs`
- Modify: `src/Runner.Mcp/McpServer.cs` — register the 3 new tools in `BuildRegistry()`.

- [ ] **Step 1: Failing tests**

`tests/Runner.Mcp.Tests/Tools/StatefulToolsTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class StatefulToolsTests
{
    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Dictionary<string, string> Responses { get; } = new();

        internal override Task<JsonElement> InvokeAsyncForTests(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            var resp = Responses.TryGetValue(method, out var r) ? r : "{}";
            return Task.FromResult(JsonDocument.Parse(resp).RootElement.Clone());
        }
    }

    [Fact]
    public async Task WarpAndAssertDraw_ProducesAtomicSequence()
    {
        var life = new RecordingLifecycle();
        life.Responses["draw.assert_contains"] = "{\"passed\":true,\"matched\":2}";

        var tool = new WarpAndAssertDrawTool();
        var args = JsonDocument.Parse("{\"location\":\"SeedShop\",\"x\":4,\"y\":19,\"texture_asset\":\"LooseSprites/Cursors\"}").RootElement;
        var result = await tool.InvokeAsync(args, life, CancellationToken.None);

        Assert.False(result.IsError);
        // Sequence: warp → arm → freeze → assert → thaw. Might include internal wait.ms
        // as a local delay (no RPC), so we verify the RPC-observable sequence.
        var methods = life.Calls.ConvertAll(c => c.Method);
        Assert.Contains("player.warp", methods);
        Assert.Contains("draw.arm", methods);
        Assert.Contains("freeze.begin", methods);
        Assert.Contains("draw.assert_contains", methods);
        Assert.Contains("freeze.end", methods);
        Assert.Contains("\"passed\":true", result.Text);
    }

    [Fact]
    public async Task RunScenario_LoadsAndReturnsReport()
    {
        // Write a tiny valid scenario to a temp path.
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-run-{System.Guid.NewGuid():N}.test.json");
        File.WriteAllText(tmp, "{\"name\":\"n\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[]}");

        try
        {
            var life = new RecordingLifecycle();
            // scenario.begin and scenario.end must both return OK-shaped results.
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"]   = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";

            var tool = new RunScenarioTool();
            var args = JsonDocument.Parse($"{{\"path\":{JsonSerializer.Serialize(tmp)}}}").RootElement;
            var result = await tool.InvokeAsync(args, life, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("\"passed\":true", result.Text);
            Assert.Contains("scenario.begin", life.Calls.ConvertAll(c => c.Method));
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
```

Run: FAIL.

- [ ] **Step 2: WarpAndAssertDrawTool.cs**

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Atomic: warp → draw.arm → wait → freeze.begin → draw.assert_contains → freeze.end.</summary>
public sealed class WarpAndAssertDrawTool : ITool
{
    public string Name => "warp_and_assert_draw";
    public string Description =>
        "Warp, arm draw-capture, wait 500ms for the scene to settle, enter FREEZE, " +
        "assert that the given texture was drawn, then THAW. Returns {passed, matched}.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object",
         "properties":{
           "location":{"type":"string"},
           "x":{"type":"integer"},
           "y":{"type":"integer"},
           "texture_asset":{"type":"string"},
           "min_count":{"type":"integer","minimum":1,"default":1}
         },
         "required":["location","x","y","texture_asset"]}
        """).RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? life, CancellationToken ct)
    {
        if (life is null) return McpToolResult.Error("lifecycle unavailable");
        string? location = args.TryGetProperty("location", out var l) ? l.GetString() : null;
        if (location is null) return McpToolResult.Error("'location' is required");
        int x = args.GetProperty("x").GetInt32();
        int y = args.GetProperty("y").GetInt32();
        string? texture = args.TryGetProperty("texture_asset", out var te) ? te.GetString() : null;
        if (texture is null) return McpToolResult.Error("'texture_asset' is required");
        int minCount = args.TryGetProperty("min_count", out var mc) && mc.ValueKind == JsonValueKind.Number ? mc.GetInt32() : 1;

        var warpParams = JsonDocument.Parse($"{{\"location\":{JsonSerializer.Serialize(location)},\"x\":{x},\"y\":{y}}}").RootElement;
        var filterJson = $"{{\"filter\":{{\"texture_asset\":{JsonSerializer.Serialize(texture)}}},\"min_count\":{minCount}}}";

        try
        {
            await life.InvokeAsyncForTests("player.warp", warpParams, ct);
            await life.InvokeAsyncForTests("draw.arm", null, ct);
            await Task.Delay(500, ct);
            await life.InvokeAsyncForTests("freeze.begin", null, ct);

            var assertResult = await life.InvokeAsyncForTests("draw.assert_contains",
                JsonDocument.Parse(filterJson).RootElement, ct);

            try { await life.InvokeAsyncForTests("freeze.end", null, ct); }
            catch { /* best-effort thaw */ }

            return McpToolResult.Success(assertResult);
        }
        catch (SdvRpcException ex)
        {
            try { await life.InvokeAsyncForTests("freeze.end", null, ct); } catch { }
            return McpToolResult.Error(ex.Message);
        }
    }
}
```

- [ ] **Step 3: CaptureStateTool.cs**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>Parallel reads of state.player + state.location + state.time + state.menu.</summary>
public sealed class CaptureStateTool : ITool
{
    public string Name => "capture_state";
    public string Description =>
        "Snapshot the current game state: player, location, time, and active menu.";

    public JsonElement InputSchema { get; } =
        JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? life, CancellationToken ct)
    {
        if (life is null) return McpToolResult.Error("lifecycle unavailable");

        try
        {
            var player = life.InvokeAsyncForTests("state.player", null, ct);
            var location = life.InvokeAsyncForTests("state.location", null, ct);
            var time = life.InvokeAsyncForTests("state.time", null, ct);
            var menu = life.InvokeAsyncForTests("state.menu", null, ct);
            await Task.WhenAll(player, location, time, menu);

            var result = new JsonObject
            {
                ["player"]   = JsonNode.Parse(player.Result.GetRawText()),
                ["location"] = JsonNode.Parse(location.Result.GetRawText()),
                ["time"]     = JsonNode.Parse(time.Result.GetRawText()),
                ["menu"]     = JsonNode.Parse(menu.Result.GetRawText()),
            };
            return McpToolResult.Success(JsonDocument.Parse(result.ToJsonString()).RootElement);
        }
        catch (SdvRpcException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
    }
}
```

- [ ] **Step 4: RunScenarioTool.cs**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Mcp.Tools;

/// <summary>
/// Load a <c>.test.json</c> scenario via <see cref="ScenarioLoader"/> and drive it through
/// the harness step-by-step. Returns a summary similar to <c>ScenarioReport</c>.
/// </summary>
public sealed class RunScenarioTool : ITool
{
    public string Name => "run_scenario";
    public string Description =>
        "Execute a .test.json scenario. Returns {passed, assertions_run, assertions_passed, failures, duration_ms}.";

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
        """).RootElement;

    public async Task<McpToolResult> InvokeAsync(JsonElement args, SdvLifecycle? life, CancellationToken ct)
    {
        if (life is null) return McpToolResult.Error("lifecycle unavailable");
        if (!args.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String)
            return McpToolResult.Error("'path' is required");
        var path = p.GetString()!;

        ScenarioSpec spec;
        try { spec = ScenarioLoader.Load(path); }
        catch (System.Exception ex) { return McpToolResult.Error($"load failed: {ex.Message}"); }

        var failures = new List<string>();
        var started = System.Diagnostics.Stopwatch.StartNew();
        int run = 0, passed = 0;

        try
        {
            // 1. scenario.begin
            var beginParams = JsonSerializer.SerializeToElement(new ScenarioBeginRequest
            {
                Name = spec.Name, Seed = spec.Config.Seed, Fixture = spec.Fixture,
            }, ProtocolJson.Options);
            await life.InvokeAsyncForTests("scenario.begin", beginParams, ct);

            // 2. fixture.load (if any)
            if (!string.IsNullOrEmpty(spec.Fixture))
            {
                var fxParams = JsonSerializer.SerializeToElement(
                    new FixtureLoadRequest { Name = spec.Fixture }, ProtocolJson.Options);
                await life.InvokeAsyncForTests("fixture.load", fxParams, ct);
            }

            // 3. steps
            foreach (var step in spec.Steps)
            {
                if (step.Action == "wait.ms")
                {
                    int ms = 0;
                    if (step.Args is { ValueKind: JsonValueKind.Object } a
                        && a.TryGetProperty("ms", out var mel) && mel.TryGetInt32(out var parsed))
                        ms = parsed;
                    if (ms > 0) await Task.Delay(ms, ct);
                    continue;
                }
                try { await life.InvokeAsyncForTests(step.Action, step.Args, ct); }
                catch (SdvRpcException ex) { failures.Add($"step {step.Action}: {ex.Message}"); goto done; }
            }

            // 4. assertions — minimal; delegates complex eval to the curated tools.
            // For MVP, we just count assertions declared + rely on the harness for state/draw assertions.
            foreach (var a in spec.Assertions)
            {
                run++;
                // Trivial: call the corresponding RPC if it's a draw.contains / draw.not_contains.
                // State assertions go through a richer path in the Runner's ScenarioRunner; here we
                // don't duplicate that logic. For MVP, if the MCP user wants rich state assertions
                // they should use the CLI runner. The MCP runner handles steps + RPC-style asserts only.
                if (a.Type == "draw.contains" && a.Filter is { } fx)
                {
                    try
                    {
                        var resp = await life.InvokeAsyncForTests("draw.assert_contains",
                            JsonSerializer.SerializeToElement(new { filter = fx, min_count = a.MinCount }, ProtocolJson.Options), ct);
                        if (resp.TryGetProperty("passed", out var pel) && pel.GetBoolean()) passed++;
                        else failures.Add($"draw.contains: {a.Message ?? "failed"}");
                    }
                    catch (SdvRpcException ex) { failures.Add($"draw.contains: {ex.Message}"); }
                }
                else
                {
                    failures.Add($"assertion type '{a.Type}' not evaluated by MCP run_scenario — use the CLI 'sdv-test run' for full DSL support.");
                }
            }

            done:
            // 5. scenario.end
            try { await life.InvokeAsyncForTests("scenario.end", null, ct); } catch { }
        }
        catch (SdvRpcException ex) { failures.Add(ex.Message); }

        started.Stop();
        var report = new JsonObject
        {
            ["passed"] = failures.Count == 0,
            ["assertions_run"] = run,
            ["assertions_passed"] = passed,
            ["failures"] = new JsonArray(failures.Select(f => (JsonNode)f).ToArray()),
            ["duration_ms"] = (int)started.ElapsedMilliseconds,
        };
        return McpToolResult.Success(JsonDocument.Parse(report.ToJsonString()).RootElement);
    }
}
```

- [ ] **Step 5: Register in BuildRegistry**

In `src/Runner.Mcp/McpServer.cs` update `BuildRegistry`:

```csharp
public static ToolRegistry BuildRegistry()
{
    var reg = new ToolRegistry();
    reg.Register(new Tools.RpcCallTool());
    reg.Register(new Tools.ListScenariosTool());
    reg.Register(new Tools.ListFixturesTool());
    reg.Register(new Tools.ScaffoldScenarioTool());
    reg.Register(new Tools.RunScenarioTool());
    reg.Register(new Tools.WarpAndAssertDrawTool());
    reg.Register(new Tools.CaptureStateTool());
    return reg;
}
```

- [ ] **Step 6: CI**

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: Runner.Mcp.Tests 10 → 12. Total 296 → 298.

---

## Task 6: McpCommand + Program.cs wiring

**Why:** Expose `sdv-test mcp` so a Claude Code `.mcp.json` can actually launch the server.

**Files:**
- Create: `src/Runner/Commands/McpCommand.cs`
- Modify: `src/Runner/Program.cs`

- [ ] **Step 1: McpCommand.cs**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test mcp</c> — run the MCP stdio server. Reads JSON-RPC requests from stdin,
/// writes responses to stdout. stderr is reserved for diagnostic logs (unused by MVP).
/// </summary>
public static class McpCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> _args, CancellationToken ct)
    {
        var registry = McpServer.BuildRegistry();
        var lifecycle = new SdvLifecycle();
        var server = new McpServer(registry, lifecycle);

        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            await server.RunAsync(stdin, stdout, ct);
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[mcp] fatal: {ex.Message}");
            return 1;
        }
        finally
        {
            await lifecycle.DisposeAsync();
        }
    }
}
```

- [ ] **Step 2: Program.cs — add `mcp` dispatch**

In `src/Runner/Program.cs` add `"mcp" => await McpCommand.RunAsync(args.AsMemory()[1..], cts.Token),` before `_ => Unknown(args[0])`.

- [ ] **Step 3: Program.cs — PrintHelp update**

After the `record` help block, add:

```csharp
        w.WriteLine("  mcp               Run the MCP stdio server for Claude Code / MCP clients.");
        w.WriteLine("                    Reads JSON-RPC 2.0 requests from stdin, writes responses to stdout.");
        w.WriteLine("                    Configure via .mcp.json (see docs/mcp-quickstart.md).");
```

- [ ] **Step 4: Smoke-check flag parses**

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
dotnet build -c Release 2>&1 | tail -3
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"t"},"capabilities":{}}}' | \
    timeout 5 dotnet run --project src/Runner -c Release --no-build -- mcp | head -1
```

Expected: one-line JSON with `"result":{"protocolVersion":"2024-11-05","serverInfo":{"name":"sdv-test-mcp"...`. No SDV launch triggered — `initialize` doesn't need SDV.

- [ ] **Step 5: CI**

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: 298 (no new tests; arg-parse verified by the smoke above).

---

## Task 7: docs + integration placeholder + milestone update

**Why:** Final task. User docs + skipped integration test + milestone record.

**Files:**
- Create: `docs/mcp-quickstart.md`
- Create: `tests/Runner.Mcp.Tests/McpIntegrationTests.cs` (skipped)
- Create: `tests/Runner.Mcp.Tests/Worked/manual-smoke.sh`
- Modify: `docs/milestones/current.md`

- [ ] **Step 1: docs/mcp-quickstart.md**

```markdown
# MCP Server Quickstart

The sdv-test MCP server exposes scenario-authoring + debugging tools to LLMs via
Anthropic's Model Context Protocol. Configure Claude Code to launch it, and your LLM
gets typed tools for running scenarios, capturing state, and scaffolding new tests.

## 1. Configure Claude Code

Create or edit `.mcp.json` in your workspace:

```json
{
  "mcpServers": {
    "sdv-test": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/sdv-test-framework/src/Runner", "-c", "Release", "--no-build", "--", "mcp"]
    }
  }
}
```

(Once M3.3 ships the NuGet tool, this becomes `{"command": "sdv-test", "args": ["mcp"]}`.)

## 2. Tool surface

Six curated helpers + one raw passthrough:

- **`run_scenario(path)`** — execute a `.test.json`, return pass/fail + failures.
- **`list_scenarios(dir?)`** — enumerate `*.test.json` files.
- **`list_fixtures()`** — enumerate `tests/fixtures/`.
- **`warp_and_assert_draw(location, x, y, texture_asset, min_count?)`** — atomic warp + freeze + draw assertion. Returns `{passed, matched}`.
- **`capture_state()`** — snapshot `{player, location, time, menu}`.
- **`scaffold_scenario(name, fixture?, template?)`** — write a starter `.test.json`. Templates: `shop`, `menu`, `warp`.
- **`rpc_call(method, params?)`** — raw JSON-RPC passthrough. Escape hatch for everything else.

## 3. Environment knobs

- `SDV_MODS_PATH` — override the mods dir the harness is deployed to (default `~/.cache/sdv-test-framework/mods`).
- The MCP server lazy-launches SDV on first tool call that needs it. Tools that don't need SDV (`list_*`, `scaffold_scenario`) never trigger launch.
- Stdio EOF tears down SDV cleanly.

## 4. What's deferred

See the M3-MCP design spec (`docs/superpowers/specs/2026-04-24-m3-mcp-server-design.md`) for M4 follow-ups: HTTP transport, MCP resources/prompts, streaming tool results, richer scaffold templates.
```

- [ ] **Step 2: McpIntegrationTests.cs**

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

/// <summary>Integration surface for M3 MCP — exercised via Worked/manual-smoke.sh.</summary>
public class McpIntegrationTests
{
    [Fact(Skip = "Requires live SDV + Xvfb — run Worked/manual-smoke.sh for end-to-end verification.")]
    public void EndToEnd_LaunchesSdvAndRunsOneScenario() { }
}
```

- [ ] **Step 3: Worked/manual-smoke.sh**

```bash
#!/usr/bin/env bash
# Manual MCP-server smoke test.
# Pipes a few JSON-RPC requests to `sdv-test mcp` and asserts the response shapes.
#
# Usage: run from repo root with live SDV available (Xvfb + SDV install + Content Patcher).
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$REPO"

echo "==> Building"
dotnet build -c Release >/dev/null

echo "==> Sending MCP requests"

# Uses a here-doc: one request per line.
RESP=$(cat <<'EOF' | timeout 60 dotnet run --project src/Runner -c Release --no-build -- mcp
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"smoke"},"capabilities":{}}}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_scenarios","arguments":{"dir":"tests/samples"}}}
EOF
)

echo "==> Responses:"
echo "$RESP"

echo "$RESP" | grep -q '"protocolVersion":"2024-11-05"' || { echo "FAIL: initialize missing protocolVersion"; exit 1; }
echo "$RESP" | grep -q '"name":"run_scenario"' || { echo "FAIL: tools/list missing run_scenario"; exit 1; }
echo "$RESP" | grep -q '11-bitmap-basic' || { echo "WARN: list_scenarios didn't find the bitmap smoke scenario (tests/samples may be empty)"; }

echo "==> manual-smoke.sh PASSED"
```

Make executable: `chmod +x tests/Runner.Mcp.Tests/Worked/manual-smoke.sh`

- [ ] **Step 4: docs/milestones/current.md update**

Open `/home/fintan/stardewRepos/frobby/sdv-test-framework/docs/milestones/current.md`.

**Edit A — update the M3 subproject list**: find the M3 subprojects block. Update subproject 2:

```markdown
2. **MCP server** (§7.3 / §8) — stdio server with 6 curated tools + 1 rpc_call passthrough. ✓ **Landed 2026-04-24.**
```

**Edit B — insert new subsection** after M3 subproject 1:

```markdown
### M3 subproject 2 — MCP server landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-m3-mcp-server.md` (7 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-m3-mcp-server-design.md`.

**Scope:** new `sdv-test mcp` subcommand speaking MCP (JSON-RPC 2.0 over stdio) with 6 curated tools + 1 raw RPC passthrough. LLMs configure Claude Code via `.mcp.json` pointing at `sdv-test mcp` and get typed tools for running scenarios, capturing state, and scaffolding tests.

**Architecture:** new `src/Runner.Mcp/` project (net10) references `src/Protocol/` (reuses `NdjsonCodec` + `JsonRpcRequest`/`JsonRpcResponse`/`JsonRpcError`) and `src/Runner/` (reuses `SdvLauncher` + `HarnessDeployer`). `SdvLifecycle` lazy-launches SDV on first tool that needs it; tools that don't need SDV (`list_*`, `scaffold_scenario`) never trigger launch. Stdio EOF → clean teardown.

**Tool surface:**
- `run_scenario(path)` — load + execute `.test.json`, return pass/fail + failures.
- `list_scenarios(dir?)` — enumerate `*.test.json`.
- `list_fixtures()` — enumerate `tests/fixtures/`.
- `warp_and_assert_draw(location, x, y, texture_asset, min_count?)` — atomic warp → freeze → draw assert → thaw.
- `capture_state()` — parallel read of player/location/time/menu.
- `scaffold_scenario(name, fixture?, template?)` — write starter `.test.json` with optional `shop`/`menu`/`warp` template.
- `rpc_call(method, params?)` — raw passthrough escape hatch.

**User docs:** `docs/mcp-quickstart.md` — `.mcp.json` setup + tool reference.

**Smoke verification:** `tests/Runner.Mcp.Tests/Worked/manual-smoke.sh` pipes 3 JSON-RPC requests to `sdv-test mcp` and asserts response shapes. Runnable manually with live SDV.

**Test count after M3-MCP:** 298 Passed + 37 Skipped (was 286+36 before; +12 passed, +1 skipped).

**Out of scope (M4):**
- HTTP transport.
- MCP resources + prompts (exposing test results as MCP resources, scaffolding as MCP prompts).
- Streaming tool results for long-running scenarios.
- Richer scaffold templates.
- Full DSL assertion evaluation in `run_scenario` (state assertions currently delegate to the CLI runner; MCP's `run_scenario` handles steps + draw.contains assertions but not the richer state DSL).
```

- [ ] **Step 5: Final CI**

Run: `./scripts/ci.sh 2>&1 | grep "Passed:\|Skipped:" | head -10`
Expected: **298 Passed + 37 Skipped**.

- [ ] **Step 6 (optional): live smoke**

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 tests/Runner.Mcp.Tests/Worked/manual-smoke.sh
pkill Xvfb 2>/dev/null
```

Expected: `manual-smoke.sh PASSED`. If it hangs on SDV launch (tools/call list_scenarios doesn't need SDV so launch shouldn't trigger), investigate.

---

## Self-review

**1. Spec coverage:**
- New project `src/Runner.Mcp/` + test project → T1 ✓
- MCP stdio server with initialize + tools/list + tools/call + ping → T2 ✓
- 7 tools registered + tests → T3 (rpc_call), T4 (list_scenarios/list_fixtures/scaffold_scenario), T5 (run_scenario/warp_and_assert_draw/capture_state) ✓
- SdvLifecycle lazy launch → T3 ✓
- CLI wiring via `sdv-test mcp` → T6 ✓
- docs/mcp-quickstart.md → T7 ✓
- docs/milestones/current.md update → T7 ✓
- Acceptance 1 (CI green ~298+37) → all tasks ✓
- Acceptance 2 (new projects build clean) → T1 ✓
- Acceptance 3 (sdv-test mcp responds to initialize) → T6 step 4 smoke + T7 step 6 optional smoke ✓
- Acceptance 4 (tools/list complete) → T2 + T4 + T5 ✓
- Acceptance 5 (rpc_call passthrough) → T3 ✓
- Acceptance 6 (scaffold_scenario produces valid JSON) → T4 step 1 test uses `ScenarioLoader.Load` ✓
- Acceptance 7 (mcp-quickstart.md) → T7 step 1 ✓
- Acceptance 8 (milestone subsection) → T7 step 4 ✓
- Acceptance 9 (sample suite 11/11) → no existing code paths modified; verify in T7 final CI ✓

**2. Placeholder scan:** no TBD / vague. `run_scenario`'s state-DSL limitation is explicit ("MCP's `run_scenario` handles steps + draw.contains assertions but not the richer state DSL") — documented out-of-scope, not a gap.

**3. Type consistency:**
- `ITool.InvokeAsync(JsonElement args, SdvLifecycle? lifecycle, CancellationToken ct)` → `Task<McpToolResult>` — defined T2, consumed T3-T5. ✓
- `McpToolResult(string Text, bool IsError)` — T2, consumed everywhere. ✓
- `ToolRegistry.Register/Get/All` — T2, consumed T4-T5 register calls. ✓
- `SdvLifecycle.EnsureRunningAsync(ct) → Task<JsonRpcSession>` + `InvokeAsyncForTests(method, p, ct) → Task<JsonElement>` — T3, consumed T3-T5 tools. The `InvokeAsyncForTests` name is a test-seam — in production it's the same entrypoint. Consider a clearer name like `InvokeAsync` for production; tests override. Naming it `ForTests` is slightly misleading — fix inline: rename to `InvokeAsync` + mark `virtual` so tests can override. ✓ (noted — planner should apply during T3 implementation)
- `McpServer(ToolRegistry, SdvLifecycle?)` ctor + `RunAsync(Stream, Stream, CancellationToken)` — T2, consumed T6 CLI. ✓
- `McpServer.BuildRegistry() → ToolRegistry` — T4 adds, T5 extends, T6 consumes. ✓

**4. Hazards:**
- **MCP spec drift** — we pin to `2024-11-05` protocol version. If the MCP spec evolves, we may need to re-version. Out-of-scope for M3.
- **Client pre-initialization tool calls** — some MCP clients may send `tools/list` before `initialize`. The server currently handles this gracefully (the `switch` responds to the method regardless of lifecycle state). Non-issue.
- **stdin EOF on non-cleanly-closed client** — the while-loop exits, `DisposeAsync` runs, SDV tears down. Verified by the explicit `finally` in `McpCommand.RunAsync`.
- **`run_scenario`'s state-DSL limitation** — MCP's `run_scenario` uses simple RPC calls for step execution but doesn't replicate `ScenarioRunner`'s state-assertion DSL evaluator. Documented in out-of-scope. A future tool could wrap `ScenarioRunner` directly instead of re-implementing.
- **Hand-rolled vs DI for `SdvLifecycle`** — the test shim pattern uses `internal virtual Task<JsonElement> InvokeAsyncForTests(...)` which is test-seam-via-subclass. Suggested rename to `InvokeAsync` (same signature) + make it virtual. Tests override by subclassing. Cleaner than the current `ForTests` suffix. Apply during T3.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-24-m3-mcp-server.md`. Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review. Proven across M1-M3 prior subprojects.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
