# [TBD] — Stardew Valley Mod Testing Framework

**Status:** Design spec, pre-implementation
**Intended consumer:** Claude Code (scaffolding) + human review
**Author context:** Solo dev, SvelteKit/Directus/C# background, Arch Linux primary, Proxmox homelab for CI targets

---

## 1. Problem

Stardew Valley mod development has no real testing story. The existing workflow is:

1. Edit code
2. Launch game (~15-30s cold)
3. Load save
4. Walk to the thing
5. Eyeball whether it works
6. Repeat

For Content Patcher mods the `patch reload` command helps, but you still manually verify visuals. For SMAPI C# mods it's worse — any logic bug means a full restart cycle. There's no regression safety net, no CI, no "did my change break the tooltip in the shop menu" check.

Existing primitives in the ecosystem:
- SMAPI provides event hooks, console commands, reflection helpers
- Harmony enables runtime patching
- `ConsoleCommands` mod offers state manipulation (`debug` commands)
- TASMod provides input recording (niche, TAS-focused)

What's missing: a cohesive framework that composes these into something resembling Playwright's ergonomics — scenarios, assertions, fixtures, a runner, CI integration.

## 2. Core insight

SDV's rendering pipeline is semantic before it becomes pixels. Every frame is a sequence of `SpriteBatch.Draw` calls with structured arguments (texture reference, source rect, dest rect, color, layer depth). We can intercept those calls and assert against the **draw intent** rather than the resulting bitmap.

This dodges the hard parts of traditional visual regression:
- No GPU/driver nondeterminism
- No animation-frame timing issues
- No resolution/zoom coupling
- Failures are legible ("expected sprite Abigail_Happy, got Abigail_Neutral" vs "47 pixels differ")

Combined with direct game-state manipulation via SMAPI APIs, this gives us two orthogonal assertion surfaces:
- **State assertions** — the model (inventory, money, NPC relationships, quest flags, tile contents)
- **Render assertions** — the view (draw calls, UI composition, sprite selection)

Pixel diffing is retained as a **fallback** for shader-based and procedurally-rendered content where draw-call inspection is insufficient (~5% of cases).

## 3. Architecture overview

```
┌─────────────────────────────────────────────────────────────┐
│  Test Runner (CLI, external process)                         │
│  - Discovers *.test.json scenarios                           │
│  - Launches SDV with test harness mod + mod-under-test       │
│  - Communicates via local socket (JSON-RPC)                  │
│  - Reports results (TAP / JUnit XML / console)               │
└─────────────────────────────────────────────────────────────┘
                            │
                            │  JSON-RPC over Unix socket / named pipe
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  Test Harness Mod (SMAPI mod, loaded by SDV)                 │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Scenario Executor                                     │  │
│  │  - Steps: load save, warp, addItem, advance-time, ... │  │
│  │  - Assertions: state queries + draw-call queries      │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  State Inspector (reflection + SMAPI APIs)            │  │
│  │  - Query Game1.* and farmer state                     │  │
│  │  - Manipulate via helpers (give item, set friendship) │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Draw-Call Recorder (Harmony patch on SpriteBatch)    │  │
│  │  - Captures every Draw() call for a given tick        │  │
│  │  - Resolves texture to asset path via reverse lookup  │  │
│  │  - Structured events: { texture, src, dst, color, z } │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Bitmap Capture (fallback)                            │  │
│  │  - GraphicsDevice.GetBackBufferData() on demand       │  │
│  │  - Pause + freeze animation counters before capture   │  │
│  │  - SSIM diff against baseline                         │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Determinism Controller                               │  │
│  │  - Pin Game1.random seed                              │  │
│  │  - Freeze Game1.currentGameTime advancement on demand │  │
│  │  - Suppress weather particles / critters              │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

Two processes by design. The runner being external means:
- Test code lives in normal C#/JSON files, not inside the mod
- The runner can manage SDV lifecycle (spawn, kill, restart between suites)
- CI can run it headlessly via Xvfb
- Future language bindings (Python, TypeScript) are trivial — it's just JSON-RPC

## 4. Component detail

### 4.1 Test Harness Mod

Standard SMAPI mod. Dependencies: SMAPI >= 4.x, Harmony (bundled with SMAPI). Loads early (`UpdateOrder` low) so its Harmony patches apply before other mods draw.

**Entry point behavior:**
- On startup, check for env var `SDV_TEST_SOCKET`. If present, open that socket and enter test mode. If absent, mod is inert (no overhead for normal play).
- In test mode: register Harmony patches, install event hooks, send `ready` event to runner, then loop on incoming commands.

**Command loop** runs on the SMAPI game thread via `GameLoop.UpdateTicked`. The socket reader runs on a background thread and enqueues commands; the tick handler drains the queue. This preserves thread safety with XNA/MonoGame which is emphatically not thread-safe.

### 4.2 Draw-Call Recorder

The key piece. Harmony-patch all `SpriteBatch.Draw` overloads (there are ~7 of them) with a prefix that records the call into a ring buffer when recording is armed.

**Structured draw event:**
```json
{
  "tick": 84231,
  "texture_asset": "Characters/Abigail",
  "texture_ref_id": "0x7f2a...",
  "source_rect": { "x": 0, "y": 64, "w": 16, "h": 32 },
  "dest_rect": { "x": 640, "y": 384, "w": 64, "h": 128 },
  "color": { "r": 255, "g": 255, "b": 255, "a": 255 },
  "rotation": 0.0,
  "origin": { "x": 0, "y": 0 },
  "effects": "None",
  "layer_depth": 0.8,
  "call_index": 142
}
```

**Texture → asset path resolution.** `SpriteBatch.Draw` receives a `Texture2D` reference, not a path. We need the path for useful assertions. Strategy:
1. Hook `IAssetLoader` / `IContentHelper.Load<Texture2D>` and maintain a weak-reference map `Texture2D → asset_path`
2. For textures loaded outside SMAPI's content pipeline (rare; mostly vanilla XNB loads), fall back to hashing the texture data and matching against a pre-built hash → asset map on first miss
3. Cache aggressively; texture identity is stable within a session

**Query API** (exposed via RPC):
```
draw.snapshot(tick=current) -> DrawEvent[]
draw.find(filter) -> DrawEvent[]
  filter: { texture_asset?, in_rect?, layer_depth_range?, color? }
draw.assert_contains(filter, min_count=1) -> AssertResult
draw.assert_not_contains(filter) -> AssertResult
```

### 4.3 State Inspector & Manipulator

Thin wrappers around SMAPI's `IReflectionHelper` plus direct access to `Game1.*` statics. Exposes a curated API covering the 95% case so tests don't hand-roll reflection:

**Queries:**
- `state.player` — farmer snapshot (money, stamina, health, level, skills, inventory)
- `state.location(name?)` — current location, or named; objects/furniture/TerrainFeatures/NPCs
- `state.npc(name)` — position, schedule point, portrait, dialogue state
- `state.friendship(npc)` — points, hearts, gift-given-today flag
- `state.time` — date, season, year, time-of-day
- `state.quests` — active/completed quest IDs
- `state.flags` — mail flags, event flags, world state flags

**Manipulators:**
- `player.give_item(id, count)`, `player.set_money(n)`, `player.warp(location, x, y)`
- `time.advance(minutes)`, `time.set(day, season, year)`, `time.sleep()`
- `npc.set_position(name, x, y)`, `npc.trigger_event(id)`
- `world.set_flag(key, value)`, `world.set_weather(type)`

All manipulators emit a structured log line so failing tests have a reproducible action trace.

### 4.4 Determinism Controller

The nondeterminism sources, ranked by impact:
1. `Game1.random` — pin seed at scenario start
2. Per-location RNG (some locations have their own) — pin via reflection
3. `Game1.currentGameTime` — frozen during assertion phase via patch on `Game1.Update`
4. NPC idle movement — set all NPCs to `Halt()` during assertion phase
5. Weather particles, critters, ambient grass sway — toggled via `Game1.eventUp = true` as a blunt-but-effective suppressor, or targeted patches
6. Cursor position — force to (0,0) during capture

**Scenario lifecycle:**
```
1. RESET       — load fixture save, apply scenario preconditions
2. ARRANGE     — run manipulator steps (give items, warp, etc.)
3. ACT         — trigger the behavior under test
4. FREEZE      — enter deterministic mode
5. ASSERT      — run state + draw-call assertions
6. THAW        — exit deterministic mode
7. TEARDOWN    — optional; usually just proceed to next scenario
```

### 4.5 Bitmap Fallback

For the minority case (shader effects, procedural rendering, full-screen compositing checks):

```
bitmap.capture(region?) -> PNG bytes
bitmap.diff(baseline_path, tolerance=0.02, method="ssim") -> DiffResult
```

Must only run during FREEZE phase. Diff algorithms: SSIM (default, perceptual), pixel-exact (for strict cases), dHash (for rough similarity). Baselines stored in `tests/baselines/<scenario>/<assertion_name>.png`, diffs written to `tests/diffs/` on failure.

### 4.6 Scenario Format

JSON (not YAML — Claude Code handles JSON schemas better, and the tooling is simpler). Each scenario is a file:

```json
{
  "name": "shop_menu_shows_custom_item",
  "fixture": "fixtures/spring_day_5_clean.sav",
  "mods": ["MyCustomShopMod"],
  "config": {
    "seed": 42,
    "zoom": 1.0,
    "resolution": [1280, 720]
  },
  "steps": [
    { "action": "player.warp", "args": { "location": "SeedShop", "x": 4, "y": 19 } },
    { "action": "player.set_money", "args": { "amount": 5000 } },
    { "action": "world.interact_npc", "args": { "name": "Pierre" } },
    { "action": "wait.for_menu", "args": { "type": "ShopMenu", "timeout_ms": 2000 } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.menu.type == 'ShopMenu'"
    },
    {
      "type": "draw.contains",
      "filter": { "texture_asset": "Mods/MyCustomShopMod/sprites", "source_rect": { "x": 0, "y": 0, "w": 16, "h": 16 } },
      "min_count": 1,
      "message": "Custom shop item sprite should render in Pierre's shop"
    }
  ]
}
```

An alternate C# DSL can wrap this for modders who prefer typed fluent APIs; both compile to the same JSON-RPC calls.

### 4.7 Test Runner (CLI)

Single binary, .NET 8. Commands:

```
[tool] run <path>                  # run scenarios matching path
[tool] run --filter <pattern>      # filter by name
[tool] run --watch                 # rerun on file change (dev loop)
[tool] record <scenario>           # interactive mode: play manually, framework records steps
[tool] baseline update <scenario>  # regenerate bitmap baselines
[tool] doctor                      # verify SDV install, SMAPI version, harness mod present
```

**Lifecycle per scenario batch:**
1. `doctor` checks once at startup
2. Launch SDV subprocess with env `SDV_TEST_SOCKET=/tmp/sdv-test-<pid>.sock`
3. Wait for `ready` event (timeout 60s)
4. Stream scenarios over RPC
5. On batch complete, send `shutdown`, wait 5s, SIGTERM if needed

**Reporters:** console (default, Playwright-style), TAP (for CI composition), JUnit XML (for GitLab/GitHub CI visualization).

### 4.8 Fixture Management

Saves stored in `tests/fixtures/*.sav`. A fixture is a deliberately-constructed save at a known state (e.g., "Spring Day 5, 1000g, Pierre's shop open, all NPCs at dialogue reset").

**Fixture builder tool:** `[tool] fixture create <name>` launches SDV in record mode; user plays to desired state; save is copied into fixtures dir with metadata (game version, SMAPI version, mods present).

Fixtures are version-controlled. Git LFS recommended for repos with many.

## 5. Content Patcher mod testing

CP mods don't execute C# code — they declaratively patch game assets. Testing them means:

1. Load the CP mod alongside the harness
2. Navigate to a context where the patch should apply (e.g., specific season/weather/NPC state)
3. Assert either:
   - **State**: the asset was patched (check `Game1.content.Load<T>(path)` returns expected data)
   - **Draw**: the patched sprite actually renders (draw-call assertion)
   - **Bitmap**: the patched visual matches baseline (fallback)

The framework's `cp` namespace provides CP-specific helpers:
- `cp.assert_patched(target, when?)` — verify a Content Patcher target is patched
- `cp.list_active_patches()` — introspect CP's state
- `cp.reload()` — trigger `patch reload`, useful for hot-reload scenarios

## 6. CI integration

Target environments:
- Linux (primary): Xvfb + software rendering (Mesa llvmpipe) or actual GPU passthrough on self-hosted runners
- Windows: headless via VM or dedicated runner
- macOS: not prioritized initially

**Reference GitHub Actions workflow** ships in the repo:
```yaml
- uses: actions/checkout@v4
- uses: [tool]/setup-sdv-test-env@v1
  with:
    sdv-version: "1.6.15"
    smapi-version: "4.1.10"
- run: [tool] run tests/ --reporter junit --output test-results.xml
- uses: actions/upload-artifact@v4
  if: failure()
  with:
    name: test-diffs
    path: tests/diffs/
```

Proxmox homelab consideration: a dedicated runner VM with GPU passthrough eliminates software-rendering pixel variance in bitmap-diff tests. For Finn specifically, this fits existing pve1 infra.

## 7. Concrete implementation plan

### Phase 0 — Spike (1 week)
- Harmony-patch `SpriteBatch.Draw`, dump events to console
- Verify texture → asset path resolution works for both vanilla and modded textures
- Prove determinism controller can produce bit-identical frames across runs
- **Success criterion:** same input, same seed, same draw-call sequence twice in a row

### Phase 1 — Core framework (3-4 weeks)
- Harness mod with full command loop
- External runner CLI with scenario execution
- State inspector/manipulator (the curated 95% API)
- JSON scenario format + schema
- Console reporter
- **Success criterion:** author 10 sample scenarios covering one real mod (pick a small existing CP mod), all pass reproducibly

### Phase 2 — Production polish (2-3 weeks)
- Bitmap fallback with SSIM diffing
- Record mode (scenario authoring by playing)
- Watch mode
- Fixture builder tool
- TAP + JUnit reporters
- **Success criterion:** full test suite runs in CI (GitHub Actions + self-hosted Linux runner), results visible in PR

### Phase 3 — Ecosystem (ongoing)
- C# fluent DSL wrapper
- MCP server wrapping the RPC interface
- Documentation site (probably SvelteKit, given the author)
- Example suites for 3-5 popular community mods (with maintainer buy-in)
- NuGet package for C# DSL

## 8. Risks & open questions

**Texture resolution edge cases.** Some textures are generated procedurally at runtime (e.g., lightmaps, dynamic tinting buffers). These won't have asset paths. Mitigation: treat them as anonymous and allow assertions on texture size + content hash instead.

**SMAPI version coupling.** Every SMAPI major bump may break Harmony patch sites. Mitigation: pin supported SMAPI version range, CI-test against SMAPI beta channel, document upgrade playbook.

**Game version coupling.** SDV 1.6 → 1.7 could shift `SpriteBatch` usage patterns or `Game1` internals. Mitigation: version-gated patches, fixture format includes game version, runner's `doctor` command validates compatibility.

**Performance overhead.** Draw-call recording at 60fps on complex scenes could be expensive. Mitigation: recording is opt-in per assertion; default is disarmed. Ring buffer size configurable.

**Save fixture rot.** Game updates can invalidate saves. Mitigation: fixtures include game version, runner warns on mismatch, fixture builder can regenerate.

**Mod conflict detection.** If two mods both Harmony-patch the same method, test results may depend on load order. Mitigation: harness logs all Harmony patches active during a test; failure reports include this.

**Open question: Windows vs Linux parity.** Draw-call assertions should be identical across OSes. Bitmap assertions may not be. Acceptable to require bitmap baselines be OS-specific initially?

**Open question: MCP server scope.** Should the MCP server expose the full RPC interface, or a curated subset optimized for LLM-driven test authoring? Likely the latter, with "write scenario" being a higher-level tool than "send individual step."

## 9. What this is not

- Not a replacement for manual QA on release candidates — game feel and balance can't be automated
- Not a performance profiler — measuring FPS impact of mods is a separate concern
- Not a fuzzer — deterministic scenarios only; property-based testing is a future extension
- Not a multiplayer test framework — SDV multiplayer testing is its own beast; single-player only for v1

## 10. Naming

[TBD]. Candidates to consider later: descriptive (StardewTestKit, SDV.Testing, ValleyTest), playful (Junimo, Grange, Pelican, Harvest), or abstract (Seedling, Sprout). Ecosystem precedent leans playful — SMAPI, PyTK, Content Patcher — but descriptive aids discoverability on NuGet.

---

## Appendix A — Example test in C# DSL (future)

```csharp
[Scenario(fixture: "spring_day_5_clean")]
public async Task ShopMenu_ShowsCustomItem()
{
    await Player.Warp("SeedShop", 4, 19);
    await Player.SetMoney(5000);
    await World.InteractNpc("Pierre");
    await Wait.ForMenu<ShopMenu>(timeout: 2.Seconds());

    State.CurrentMenu.Should().BeOfType<ShopMenu>();
    Draw.Should().Contain(d =>
        d.TextureAsset == "Mods/MyCustomShopMod/sprites" &&
        d.SourceRect == new Rect(0, 0, 16, 16));
}
```

## Appendix B — JSON-RPC protocol sketch

```
→ { "id": 1, "method": "scenario.begin", "params": { "name": "...", "seed": 42 } }
← { "id": 1, "result": { "session_id": "abc123" } }

→ { "id": 2, "method": "player.warp", "params": { "location": "SeedShop", "x": 4, "y": 19 } }
← { "id": 2, "result": { "ok": true, "tick": 84200 } }

→ { "id": 3, "method": "draw.find", "params": { "filter": { "texture_asset": "Mods/..." } } }
← { "id": 3, "result": { "events": [...] } }

→ { "id": 4, "method": "scenario.end" }
← { "id": 4, "result": { "passed": true, "assertions": 5, "duration_ms": 342 } }
```
