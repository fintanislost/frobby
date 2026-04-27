# SdvTestFramework.Cli — `sdv-test`

Stardew Valley test-framework CLI: launch SDV, run scenarios, scaffold templates,
build texture manifests, run as MCP server for Claude Code.

## Install

```bash
dotnet tool install -g SdvTestFramework.Cli
```

After install, `sdv-test --help` lists all subcommands:

- `sdv-test run <path>` — execute scenario(s).
- `sdv-test list <path>` — enumerate `*.test.json` files.
- `sdv-test record <name>` — RPC-trace recorder.
- `sdv-test build-manifest` — generate texture-hash manifest.
- `sdv-test mcp` — run as MCP stdio server (for Claude Code).
- ... and more.

## Claude Code via MCP

Add to `.mcp.json` in your workspace:

```json
{
  "mcpServers": {
    "sdv-test": {
      "command": "sdv-test",
      "args": ["mcp"]
    }
  }
}
```

Quickstart: https://github.com/fintan/sdv-test-framework/blob/main/docs/mcp-quickstart.md
