# [TBD] — Stardew Valley Mod Testing Framework

Automated testing framework for Stardew Valley mods using draw-call interception rather than pixel-diff visual regression.

**Status:** Design + Claude Code scaffolding phase. No production code yet. First work is the M0 determinism spike.

## Quick start for Claude Code

1. Clone the repo
2. Run `./install.sh`
3. Launch `claude` in the repo directory
4. Install Superpowers plugin inside Claude Code:
   ```
   /plugin marketplace add obra/superpowers-marketplace
   /plugin install superpowers@superpowers-marketplace
   ```
5. Restart Claude Code
6. Read `CLAUDE.md`, then start with: *"Begin the M0 determinism spike."*

## What's in here

```
.
├── CLAUDE.md                    # Project constitution (loaded at session start)
├── .claude/
│   ├── rules/                   # Modular convention docs, loaded on demand
│   ├── agents/                  # Specialized subagents (spike-runner, reviewer, sdv-expert)
│   └── commands/                # Custom slash commands (/spike, /harmony-patch, etc.)
├── .mcp.json                    # Project-level MCP server config
├── docs/
│   ├── spec.md                  # Full design spec (see sdv-test-framework-spec.md)
│   ├── milestones/              # M0 → M3 with deliverables and exit criteria
│   ├── rpc-schema.md            # JSON-RPC protocol reference
│   ├── patches.md               # Active Harmony patches registry
│   ├── open-questions.md        # Unresolved investigations
│   └── spikes/                  # Time-boxed investigation reports
└── install.sh                   # One-time setup helper
```

## Design premise in one paragraph

Stardew Valley renders through `SpriteBatch.Draw` calls with structured arguments (texture, source rect, dest rect, color, layer depth). By Harmony-patching these calls, we can capture rendering as a queryable event stream and assert semantically ("Abigail's happy portrait was drawn at tile X with tint Y") instead of diffing framebuffers. This dodges GPU nondeterminism, animation timing issues, and resolution coupling. Combined with direct state manipulation via SMAPI APIs and RNG/time pinning, scenarios become deterministic and reproducible. Pixel diffing survives as a 5% fallback for shader and procedural content.

## Milestones

- **M0** — Determinism spike (prove the foundation before building)
- **M1** — Core framework (RPC, runner, state API, draw API, scenario format)
- **M2** — Production polish (bitmap fallback, record mode, CI, docs)
- **M3** — Ecosystem (MCP server, C# DSL, community example suites)

Details: `docs/milestones/`.

## Contributing

Contributions welcome after M1 ships. For now, feedback on the spec is the most useful input.

## License

TBD (MIT or Apache 2.0 — decide before M1 public release).
