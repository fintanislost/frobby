# ScenarioLoader Home Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `ScenarioLoader` back into the shared Protocol project so scenario loading is no longer physically hosted by `Runner.Mcp`.

**Architecture:** Keep the existing `SdvTestFramework.Protocol.Scenarios` namespace and public API stable. Protocol owns schema-backed scenario loading; Runner and Runner.Mcp consume it through their existing Protocol reference. Use a net6-compatible `JsonSchema.Net` version so Harness can still reference Protocol under SMAPI's .NET 6 host.

**Tech Stack:** C# 12, .NET 6 Protocol/Harness, .NET 10 Runner/Runner.Mcp, JsonSchema.Net, xUnit.

---

### Task 1: Lock The Assembly Boundary With A Failing Test

**Files:**
- Modify: `tests/Runner.Tests/ScenarioLoaderTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test near the top of `ScenarioLoaderTests`:

```csharp
[Fact]
public void ScenarioLoader_LivesInProtocolAssembly()
{
    Assert.Equal(
        "SdvTestFramework.Protocol",
        typeof(ScenarioLoader).Assembly.GetName().Name);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoader_LivesInProtocolAssembly" -v minimal
```

Expected: fails because `ScenarioLoader` is currently compiled into `Runner.Mcp`.

### Task 2: Move ScenarioLoader Into Protocol

**Files:**
- Move: `src/Runner.Mcp/ScenarioLoader.cs` to `src/Protocol/Scenarios/ScenarioLoader.cs`
- Modify: `src/Protocol/Protocol.csproj`
- Modify: `src/Runner.Mcp/Runner.Mcp.csproj`
- Modify: `src/Runner/Runner.csproj`

- [ ] **Step 1: Move the file without changing its namespace**

Run:

```bash
mkdir -p src/Protocol/Scenarios
git mv src/Runner.Mcp/ScenarioLoader.cs src/Protocol/Scenarios/ScenarioLoader.cs
```

- [ ] **Step 2: Put schema validation dependency on Protocol**

Add a `JsonSchema.Net` package reference to `src/Protocol/Protocol.csproj` using a net6-compatible major version.

- [ ] **Step 3: Remove direct schema package references from consumers**

Remove the `JsonSchema.Net` package references from `src/Runner.Mcp/Runner.Mcp.csproj` and `src/Runner/Runner.csproj` if no other source file uses the package directly.

- [ ] **Step 4: Keep schema files copied where loaders execute**

Protocol owns the loader, but Runner and Runner.Mcp still execute it from their output directories. Keep the existing schema copy items in Runner and Runner.Mcp unless verification proves Protocol's output copy is sufficient for all consumers.

### Task 3: Update Documentation And Comments

**Files:**
- Modify: `docs/roadmap.md`
- Modify: `src/Protocol/Protocol.csproj`
- Modify: `src/Runner.Mcp/Runner.Mcp.csproj`
- Modify: `src/Runner/Runner.csproj`
- Optional: stale XML doc comments that mention `Runner.Scenarios.ScenarioLoader`

- [ ] **Step 1: Move the roadmap item to Completed**

Record the date and one-line summary under `Completed`, and remove the pending Tier 3 item.

- [ ] **Step 2: Remove stale project comments**

Project comments should say Protocol owns the loader and JsonSchema.Net is pinned to a SMAPI-safe version.

### Task 4: Verify The Boundary And Runtime Build

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoader" -v minimal
dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj -v minimal
dotnet test tests/Runner.Tests/Runner.Tests.csproj -v minimal
dotnet build sdv-test-framework.slnx
git diff --check
```

Expected: all commands pass; build has 0 warnings/errors.
