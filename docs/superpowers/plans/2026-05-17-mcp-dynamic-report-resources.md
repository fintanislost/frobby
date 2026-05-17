# MCP Dynamic Report Resources Implementation Plan

## Scope

Expose the most recent Frobby report through MCP resources so agents can debug runs
without scraping tool-call output. The feature is process-local and mod-neutral.

## Tasks

1. Add failing `Runner.Mcp.Tests` coverage for latest-report resource descriptors,
   no-report errors, file-backed report reads, malformed summaries, and
   `run_scenario` recording.
2. Add a small `McpReportRegistry` to hold the latest report directory and optional
   in-memory summary JSON.
3. Pass the registry through `McpServer` into `ToolInvocationContext`.
4. Update `RunScenarioTool` to record its successful JSON result in the registry.
5. Extend `McpResources` with dynamic latest-report readers.
6. Update MCP docs/roadmap notes.
7. Verify with `dotnet test tests/Runner.Mcp.Tests/Runner.Mcp.Tests.csproj -v minimal`,
   `dotnet build sdv-test-framework.slnx`, and `git diff --check`.

## TDD Notes

The first red bar should come from compile/runtime failures in the new tests because
the report registry and dynamic resource URIs do not exist yet. Implementation should
stay minimal and should not alter static doc or prompt behavior.
