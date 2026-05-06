# C# DSL Quickstart

Write scenarios as xUnit test methods instead of hand-authoring `*.test.json`. The DSL
wraps the same JSON-RPC surface the CLI runner uses, so anything you can express in a
JSON scenario you can express in C#.

## 1. Install

In your mod's test project:

```bash
dotnet add package SdvTestFramework.Runner.Dsl
```

You also need the CLI tool (which provides SDV launch + harness deployment):

```bash
dotnet tool install -g SdvTestFramework.Cli
```

(For development against the source tree, you can still use a `<ProjectReference>` —
see `docs/developer-setup.md`.)

## 2. Write a test

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

The `SdvCollection` + `SdvFixture` types are provided by the DSL package — `[Collection("SDV")]` automatically picks up the fixture. No per-assembly boilerplate class needed unless you want to add your own collection members.

Note: you need both `[Fact]` and `[Scenario]`. `[Fact]` tells xUnit to run the method;
`[Scenario]` tells the DSL to wrap it in `scenario.begin`/`scenario.end`. A combined
`[ScenarioFact]` is on the roadmap.

## 3. Run

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

- `Player.Warp(location, x, y)` / `SetMoney(amount)` / `AddMail(id)` / `GiveItem(id, count)`
- `Time.Advance(minutes)`
- `World.SetWeather(type)`
- `Input.Key(key)` / `Text(text)` / `Click(x, y)` / `ClickText(text)` / `Hover(x, y)` / `HoverText(text)`
- `Fixture.Load(name)`
- `Freeze.Begin()` / `End()` / `Status()`
- `Draw.Arm()` / `Disarm()` / `Snapshot()` / `Find(filter)` / `AssertContains(filter)` / `AssertNotContains(filter)`
- `State.Player()` / `Time()` / `Location(name?)` / `Locations()` / `MapTile(location?, x?, y?, layers?)` / `Npc(name)` / `Menu()` / `Event()` / `Mods()`
- `Bitmap.Capture(region?)`
- `Screenshot.Capture(name)`
- `Wait.Ms(ms)`

Location/map helpers are useful for complex Content Patcher or code mods with custom
areas:

```csharp
var locations = await State.Locations();
Assert.Contains(locations.Locations, l => l.Name == "Custom_TownEast");

await Player.Warp("Custom_TownEast", 10, 20);
await Wait.Ms(500);

var location = await State.Location();
Assert.NotEqual(0, location.MapWidth);

var tile = await State.MapTile(layers: new[] { "Back" });
Assert.Contains(tile.Layers, l => l.Name == "Back");
```

JSON runner scenarios can observe cutscenes and other Stardew events with
`state.event` and event waits:

```json
{ "action": "event.start", "args": { "id": "520702", "location": "BusStop" } },
{ "action": "wait.event_active", "args": { "id": "520702", "timeout_ms": 10000 } },
{ "action": "state.assert", "args": { "expr": "state.event.actors contains name 'Krobus'" } },
{ "action": "screenshot.capture_next_frame", "args": { "name": "active-event" } },
{ "action": "event.skip", "args": {} },
{ "action": "wait.event_complete", "args": { "id": "520702", "timeout_ms": 30000 } }
```

Use `screenshot.capture_next_frame` for active events because `freeze.begin`
intentionally rejects cutscenes while `Game1.eventUp` is true.

JSON runner scenarios can validate final runtime content assets directly with
`content.asset` assertions. These load through Stardew's live content pipeline,
so the assertion sees the result after Content Patcher patches and conditions,
not just the content pack source files:

```json
{
  "type": "content.asset",
  "asset": "Maps/Custom_TownEast",
  "asset_type": "map",
  "expr": "asset.layers contains name 'Back'",
  "message": "Town East map should load with a Back layer"
},
{
  "type": "content.asset",
  "asset": "Data/Locations",
  "asset_type": "data",
  "entry_keys": ["Custom_TownEast"],
  "expr": "asset.entries.Custom_TownEast.value.display_name == 'Town East'"
},
{
  "type": "content.asset",
  "asset": "Data/Locations",
  "asset_type": "data",
  "entry_keys": ["Custom_EnchantedGrove"],
  "expr": "asset.entries.Custom_EnchantedGrove.value.create_on_load.map_path != ''"
}
```

Expression roots are `asset.<field>`. Top-level result fields such as
`asset.exists`, `asset.kind`, and `asset.runtime_type` are available; otherwise
paths resolve against the bounded `summary` object, so `asset.layers` means
`summary.layers`. Supported operators are `==`, `!=`, and array membership like
`asset.layers contains name 'Back'`. Selected `Data/*` entries include public
scalar fields/properties and bounded nested runtime data objects, with property
names converted to snake_case.

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

## HTML Run Reports

Every test run produces a directory at `./test-results/<run-id>/` containing:
- `index.html` — pass/fail dashboard, opens in any browser.
- `summary.json` — machine-readable run data (LLM-friendly).
- `scenarios/<name>/` — per-scenario page + step/assertion data + screenshots.

Auto-screenshots fire at `freeze.begin` and on assertion failure. Add explicit named
captures via `await Screenshot.Capture("after_my_action")` from the DSL or
`{ "action": "screenshot.capture", "args": { "name": "after_my_action" } }` in JSON.
When the screenshot should reflect a click, hover tooltip, or other input that changes the current
menu, prefer the JSON runner action
`{ "action": "screenshot.capture_next_frame", "args": { "name": "after_click", "timeout_ms": 3000 } }`;
it waits for the next rendered frame before copying the PNG into the report.

CLI flag: `sdv-test run --report-dir <path>` to override the default location, or
`--no-report` to skip generation.

Use `sdv-test run --headless` or `sdv-test run-suite --headless` on Linux to
launch SDV through `xvfb-run` so the game does not take over the active desktop
display or mouse cursor.

### Text-fit assertions

The CLI runner supports `draw.text_all_within` for UI layout guardrails. It snapshots
captured `SpriteBatch.DrawString` text, applies the optional `filter`, and fails if any
matching text bounds fall outside the required `region` rectangle.

```json
{
  "type": "draw.text_all_within",
  "filter": {
    "in_rect": [64, 78, 816, 566],
    "color_any": [[255, 214, 128, 255], [236, 229, 206, 255]]
  },
  "region": { "x": 64, "y": 78, "w": 816, "h": 566 },
  "message": "Main pane text should remain inside the menu body"
}
```

Use this for fixed UI panes, tables, button bars, and terminal/status areas where text
overflow is a regression. `min_count` defaults to `1`; set it higher when the assertion
should also prove that several expected visible text instances were captured. `draw.text_contains`
also accepts `max_count` for exact-occurrence checks, such as proving a persisted
headline was restored once rather than duplicated after reload. Text occurrence assertions
collapse repeated samples of the same nearby text, so multi-frame capture and
shadowed/multi-pass text rendering don't inflate the count.
The optional `color_any` filter is useful when the game HUD or another mod draws text
near the same screen area and the assertion should target a known UI palette.

**DSL caveat:** when running tests via `dotnet test` (the DSL path), only `summary.json` is
written today — the rich `index.html` + per-scenario reports come from the CLI runner
(`sdv-test run`). Per-test screenshots from `Screenshot.Capture` ARE saved into the run
directory in both modes.

## Diff-image on failure

When a `bitmap` assertion fails, the runner writes forensics PNGs into the per-run report
directory at `scenarios/<scenario>/diffs/assertion-NN-bitmap/`:

- `baseline.png` — the expected image.
- `capture.png` — what was actually rendered.
- `diff.png` — baseline with a bilinear-smoothed red heatmap overlaid where blocks fell
  below the SSIM tolerance.

Optional composite via `--diff-format=triptych` (CLI) or `"diff_format": "triptych"`
(per-assertion in the scenario JSON) writes a 4th `triptych.png` with all three side-by-side.
The HTML report's per-scenario page surfaces all of these in a "Failure forensics" section.

`--update-baselines` short-circuits diff generation — the capture overwrites the baseline
instead, so there's nothing to forensics.

### Bitmap diff methods

The `bitmap` assertion supports three methods via the `method` field:

- `"ssim"` (default) — perceptual structural similarity. `tolerance` is a float in (0, 1]; higher = stricter.
- `"pixel-exact"` — strict per-pixel RGB compare. `tolerance` is an integer; max per-channel delta allowed.
- `"dhash"` — perceptual difference hash. `tolerance` is an integer 0-64; max Hamming distance allowed.

Choose `pixel-exact` for UI elements that should be bit-stable; `dhash` for "vaguely the same scene" checks. SSIM is the right default for everything else.

### Tolerance tiers

`sdv-test run --tier=<generic|ci-ubuntu|self-hosted-nvidia>` selects per-method default tolerances. Useful when running the same suite across environments with different rendering determinism.

| method      | generic | ci-ubuntu | self-hosted-nvidia |
| ----------- | ------- | --------- | ------------------ |
| ssim        | 0.95    | 0.98      | 0.999              |
| pixel-exact | 5       | 2         | 0                  |
| dhash       | 5       | 3         | 1                  |

Per-assertion `tolerance` always overrides the tier default. Per-assertion `tier` field overrides the run-wide flag.

### `sdv-test baselines` subcommand

Manage bitmap baselines:
- `sdv-test baselines list [--scenarios <dir>]` — enumerate referenced baselines + presence.
- `sdv-test baselines update <path-or-glob> [--tier <n>]` — rerun with `--update-baselines`.
- `sdv-test baselines show <path>` — print PNG metadata.
- `sdv-test baselines delete <path> [--force]` — remove file (prompts unless `--force`).

### Capture cache cleanup

`sdv-test run` automatically sweeps `~/.cache/sdv-test-framework/captures/` at end of every successful invocation, deleting files older than 7 days OR outside the 5 most-recent run subdirs. Opt out with `--no-cache-cleanup`.

Manual: `sdv-test cache clean [--max-age <days>] [--keep-runs <n>] [--dry-run]`.
