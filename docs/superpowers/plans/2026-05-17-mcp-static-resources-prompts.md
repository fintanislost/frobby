# MCP Static Resources And Prompts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add read-only MCP `resources/*` and `prompts/*` support for core Frobby docs, scenario context, and workflow prompts.

**Architecture:** Keep the MCP server simple: route four new protocol methods from `McpServer` to small registries that return JSON elements shaped to the MCP 2024-11-05 spec. Resources are read-only and URI allowlisted. Prompts are static workflow templates with small optional argument dictionaries.

**Tech Stack:** C# 12, .NET 10 Runner.Mcp, System.Text.Json, xUnit, MCP JSON-RPC 2024-11-05.

---

### Task 1: Capabilities And Method Routing

**Files:**
- Modify: `tests/Runner.Mcp.Tests/McpServerTests.cs`
- Modify: `src/Runner.Mcp/McpCapabilities.cs`
- Modify: `src/Runner.Mcp/McpServer.cs`

- [ ] Add failing tests that initialize declares `resources` and `prompts`.
- [ ] Add failing tests that `resources/list` and `prompts/list` no longer return method-not-found.
- [ ] Update `McpCapabilities.BuildInitializeResult()` to include `resources: {}` and `prompts: {}`.
- [ ] Route `resources/list`, `resources/read`, `prompts/list`, and `prompts/get` from `McpServer`.

### Task 2: Static Resources

**Files:**
- Create: `src/Runner.Mcp/McpResources.cs`
- Modify: `tests/Runner.Mcp.Tests/McpServerTests.cs`

- [ ] Add failing tests for resource listing, doc reading, scenario index reading, and unknown URI errors.
- [ ] Implement an allowlisted static resource registry.
- [ ] Read Markdown resources from `Directory.GetCurrentDirectory()`.
- [ ] Build `frobby://scenarios/list` as Markdown from `tests/sdv/*.test.json`, with a useful empty state.

### Task 3: Static Prompts

**Files:**
- Create: `src/Runner.Mcp/McpPrompts.cs`
- Modify: `tests/Runner.Mcp.Tests/McpServerTests.cs`

- [ ] Add failing tests for prompt listing, prompt retrieval with arguments, and unknown prompt errors.
- [ ] Implement prompt descriptors with argument metadata.
- [ ] Implement prompt text generation using supplied optional arguments.

### Task 4: Docs And Verification

**Files:**
- Modify: `docs/mcp-quickstart.md`
- Modify: `docs/roadmap.md`

- [ ] Document resources and prompts in the MCP quickstart.
- [ ] Move the roadmap MCP resources/prompts item to completed.
- [ ] Run `dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj -v minimal`.
- [ ] Run `dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoader" -v minimal`.
- [ ] Run `dotnet build sdv-test-framework.slnx`.
- [ ] Run `git diff --check`.
