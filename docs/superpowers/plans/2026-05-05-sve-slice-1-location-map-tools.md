# SVE Slice 1 Location and Map Tools

> Created: 2026-05-05
> Repo: `/home/fintan/stardewRepos/frobby/sdv-test-framework`
> Testbed: `/home/fintan/stardewRepos/StardewValleyExpanded`

## Goal

Use Stardew Valley Expanded as the first non-Starberg proof case for Frobby's neutral repo scaffold by adding location and map introspection tools that any Stardew mod can use.

Slice 1 should let a mod test answer these questions without hardcoded coordinates or Starberg-specific behavior:

- Did the mod's custom locations register in the running game?
- Can the harness warp to a custom location by name?
- What map dimensions and warp exits does a location expose?
- What tile/layer/property data exists at the farmer's current tile or a requested tile?
- Can scenario authors wait for a warp/location transition without relying on fixed sleeps?

This plan intentionally stops before executing `TouchAction` or map tile action commands. That is the next Slice 1 follow-up after introspection is stable.

## Current State

Frobby already has these building blocks:

- `state.location` snapshots the current or named location, but only returns NPCs, objects, furniture, and terrain.
- `player.warp` can warp by Stardew `GameLocation` name.
- `world.interact_tile` can activate furniture and placed object actions, but not map tile properties/actions.
- Scenario assertions can query state RPC methods such as `state.mods` and can assert array membership using paths like `state.mods.mods contains unique_id 'FlashShifter.SVECode'`.
- SVE has a working repo-local scaffold and a passing live smoke scenario:
  - `tests/sdv/01-sve-core-loads.test.json`

## Design

Add three neutral state RPC surfaces:

- `state.locations`
  - No args.
  - Returns all loaded Stardew locations as summaries.
  - Used by tests to prove custom locations are registered.

- Expanded `state.location`
  - Existing args still work.
  - Adds map dimensions and warp summaries.
  - Existing callers remain compatible because fields are additive.

- `state.map_tile`
  - Args are optional.
  - With no args, snapshots the tile under the current farmer in the current location. This makes it usable from existing scenario assertion syntax.
  - With args, snapshots an explicit `location`, `x`, `y`, and optional `layers`.

Add one runner-only scenario action:

- `wait.location`
  - Polls `state.player` until the player is in the requested location, with optional tile checks.
  - Replaces fixed sleeps after `player.warp` in live mod scenarios.

## Task 1: Protocol DTOs and Serialization Tests

Create the protocol shape before harness implementation.

Files:

- Add `src/Protocol/Models/LocationsState.cs`
- Add `src/Protocol/Models/MapTileRequest.cs`
- Add `src/Protocol/Models/MapTileState.cs`
- Modify `src/Protocol/Models/LocationState.cs`
- Add `tests/Protocol.Tests/LocationsStateSerializationTests.cs`
- Add `tests/Protocol.Tests/MapTileStateSerializationTests.cs`
- Extend `tests/Protocol.Tests/LocationStateSerializationTests.cs`

DTO shape:

```csharp
namespace StardewModdingAPI.Frobby.Protocol.Models;

public sealed class LocationsState
{
    public List<LocationSummary> Locations { get; set; } = new();
}

public sealed class LocationSummary
{
    public string Name { get; set; } = "";
    public string UniqueName { get; set; } = "";
    public bool IsOutdoors { get; set; }
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public int WarpCount { get; set; }
}

public sealed class WarpSummary
{
    public TilePoint Source { get; set; } = new();
    public string TargetLocation { get; set; } = "";
    public TilePoint Target { get; set; } = new();
}

public sealed class TilePoint
{
    public int X { get; set; }
    public int Y { get; set; }
}
```

Modify `LocationState` additively:

```csharp
public string UniqueName { get; set; } = "";
public int MapWidth { get; set; }
public int MapHeight { get; set; }
public List<WarpSummary> Warps { get; set; } = new();
```

Map tile request and response:

```csharp
public sealed class MapTileRequest
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public List<string>? Layers { get; set; }
}

public sealed class MapTileState
{
    public string Location { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public List<MapTileLayerState> Layers { get; set; } = new();
}

public sealed class MapTileLayerState
{
    public string Name { get; set; } = "";
    public int TileIndex { get; set; } = -1;
    public string TileSheet { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new();
}
```

Test expectations:

- `LocationsState` serializes as `locations`, `unique_name`, `map_width`, `map_height`, and `warp_count`.
- `LocationState` serializes added fields without changing existing fields.
- `MapTileRequest` serializes and deserializes nullable coordinates and layer filters.
- `MapTileState` serializes layer `properties` as a JSON object.

Run first and expect failures before implementation:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --configuration Debug --filter "LocationsStateSerializationTests|MapTileStateSerializationTests|LocationStateSerializationTests"
```

## Task 2: Neutral Location Projection in the Harness

Centralize Stardew object-to-protocol mapping so `state.location` and `state.locations` cannot drift.

Files:

- Add `src/Harness/Handlers/LocationStateProjector.cs`
- Modify `src/Harness/Handlers/StateLocationHandler.cs`
- Add `src/Harness/Handlers/StateLocationsHandler.cs`
- Add/extend harness tests:
  - `tests/Harness.Tests/StateLocationHandlerTests.cs`
  - `tests/Harness.Tests/StateLocationsHandlerTests.cs`

Projector responsibilities:

```csharp
internal static class LocationStateProjector
{
    public static LocationSummary ToSummary(GameLocation location)
    {
        var size = GetMapSize(location);

        return new LocationSummary
        {
            Name = location.Name ?? "",
            UniqueName = location.NameOrUniqueName ?? location.Name ?? "",
            IsOutdoors = location.IsOutdoors,
            MapWidth = size.Width,
            MapHeight = size.Height,
            WarpCount = location.warps?.Count ?? 0,
        };
    }

    public static LocationState ToState(GameLocation location)
    {
        var state = new LocationState
        {
            Name = location.Name ?? "",
            UniqueName = location.NameOrUniqueName ?? location.Name ?? "",
            IsOutdoors = location.IsOutdoors,
            MapWidth = GetMapSize(location).Width,
            MapHeight = GetMapSize(location).Height,
            Warps = (location.warps ?? new()).Select(ToWarpSummary).ToList(),
        };

        // Preserve existing NPC/object/furniture/terrain population here.
        return state;
    }

    private static WarpSummary ToWarpSummary(Warp warp)
    {
        return new WarpSummary
        {
            Source = new TilePoint { X = warp.X, Y = warp.Y },
            TargetLocation = warp.TargetName ?? "",
            Target = new TilePoint { X = warp.TargetX, Y = warp.TargetY },
        };
    }
}
```

Implementation details:

- Preserve every existing `state.location` field and behavior.
- Sort `state.locations.locations` by `Name`, then `UniqueName` for stable reports.
- Return `MutatorError` or RPC error consistently with existing handlers when a named location does not exist.
- Keep this generic. Do not reference SVE unique IDs, SVE location names, or Starberg names in Frobby source.

Focused tests:

- `state.location` serialization includes `map_width`, `map_height`, and `warps`.
- `state.locations` validates it takes no required params.
- Existing skipped live test for furniture remains valid.
- Add a skipped live test showing at least one location summary is returned in a live Stardew context.

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter "StateLocationHandlerTests|StateLocationsHandlerTests"
```

## Task 3: Add `state.map_tile`

Expose the map/layer/tile/property snapshot needed before tile-action testing.

Files:

- Add `src/Harness/Handlers/StateMapTileHandler.cs`
- Add `tests/Harness.Tests/StateMapTileHandlerTests.cs`
- Modify `src/Harness/ModEntry.cs`

Handler behavior:

- If `location` is omitted, use `Game1.currentLocation`.
- If `x` or `y` is omitted, use `Game1.player.TilePoint`.
- If `layers` is omitted or empty, include every layer in map order.
- If an explicit layer list is provided, include only those layers and preserve requested order.
- If location is unknown, return a clear harness error.
- If coordinates are outside any requested layer, return a clear harness error.
- For empty tiles, return `tile_index = -1`, empty `tile_sheet`, and empty `properties`.

Core handler shape:

```csharp
public sealed class StateMapTileHandler : IRpcHandler
{
    public string Method => "state.map_tile";

    public object Handle(JsonElement? args)
    {
        var request = args is null
            ? new MapTileRequest()
            : args.Value.Deserialize<MapTileRequest>(ProtocolJson.Options) ?? new MapTileRequest();

        var location = ResolveLocation(request.Location);
        var point = ResolvePoint(request, Game1.player.TilePoint);
        var layerNames = ResolveLayerNames(location.Map, request.Layers);

        return new MapTileState
        {
            Location = location.NameOrUniqueName ?? location.Name ?? "",
            X = point.X,
            Y = point.Y,
            Layers = layerNames.Select(name => SnapshotLayer(location.Map.GetLayer(name), point)).ToList(),
        };
    }
}
```

Tests:

- No-arg request deserializes successfully.
- Explicit request preserves layer order.
- Unknown location returns a failure with the missing location name.
- Out-of-bounds coordinates return a failure with the location and tile point.
- A skipped live test snapshots the current farmer tile and asserts at least one layer named `Back`.

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter "StateMapTileHandlerTests"
```

## Task 4: Register RPCs and Add DSL Helpers

Make the tools discoverable from C# tests and the in-game MCP server.

Files:

- Modify `src/Harness/ModEntry.cs`
- Modify `src/Runner.Dsl/State.cs`
- Add or extend DSL tests under `tests/Runner.Dsl.Tests`

Registration:

```csharp
Register(new StateLocationsHandler());
Register(new StateMapTileHandler());
```

Update the startup RPC list to include:

- `state.locations`
- `state.map_tile`

DSL helpers:

```csharp
public static Task<LocationsState> Locations(IRpcClient client, CancellationToken cancellationToken = default)
{
    return client.CallAsync<LocationsState>("state.locations", args: null, cancellationToken);
}

public static Task<MapTileState> MapTile(
    IRpcClient client,
    string? location = null,
    int? x = null,
    int? y = null,
    IEnumerable<string>? layers = null,
    CancellationToken cancellationToken = default)
{
    var args = new MapTileRequest
    {
        Location = location,
        X = x,
        Y = y,
        Layers = layers?.ToList(),
    };

    return client.CallAsync<MapTileState>("state.map_tile", args, cancellationToken);
}
```

Tests:

- `State.Locations` calls `state.locations` with null args.
- `State.MapTile` calls `state.map_tile` with snake_case JSON args.
- `State.MapTile` allows no-arg current-tile calls.

Run:

```bash
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --configuration Debug --filter State
```

## Task 5: Add `wait.location`

Remove fixed sleeps from location transition tests.

Files:

- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
- Add/extend `tests/Runner.Tests/ScenarioRunnerTests.cs`
- Document in `docs/scenario-format.md` if present, otherwise in `README.md` and `docs/rpc-schema.md` adjacent scenario action docs.

Action format:

```json
{
  "action": "wait.location",
  "args": {
    "location": "Custom_TownEast",
    "x": 10,
    "y": 10,
    "timeout_ms": 5000,
    "poll_ms": 100
  }
}
```

Required arg:

- `location`

Optional args:

- `x`
- `y`
- `timeout_ms`, default `5000`
- `poll_ms`, default `100`

Implementation:

- Poll `state.player`.
- Match `location` against the player's reported location string.
- If `x` and `y` are supplied, also match current tile.
- Return a timeout assertion/error message that includes expected location, optional tile, last observed location/tile, and timeout.

Tests:

- Succeeds when the first `state.player` result matches.
- Polls until a later `state.player` result matches.
- Times out with a meaningful error when location never matches.
- Times out with a meaningful error when location matches but tile does not.

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter ScenarioRunnerTests
```

## Task 6: Update Docs

Files:

- `docs/rpc-schema.md`
- `README.md`
- `docs/dsl-quickstart.md` if it exists in the repo
- `SVE_FROBBY_CAPABILITY_TODO.md`

Docs must include:

- `state.locations` request/response examples.
- Expanded `state.location` fields.
- `state.map_tile` request/response examples.
- `wait.location` scenario example.
- A neutral note that repo-local tests should prefer Frobby state tools over mod-specific coordinate scripts where possible.

`state.locations` docs example:

```json
{
  "locations": [
    {
      "name": "Town",
      "unique_name": "Town",
      "is_outdoors": true,
      "map_width": 120,
      "map_height": 100,
      "warp_count": 12
    }
  ]
}
```

`state.map_tile` docs example:

```json
{
  "location": "Town",
  "x": 10,
  "y": 20,
  "layers": [
    {
      "name": "Back",
      "tile_index": 471,
      "tile_sheet": "outdoors",
      "properties": {
        "Type": "Stone"
      }
    }
  ]
}
```

Run docs-adjacent checks:

```bash
dotnet test sdv-test-framework.slnx --configuration Debug --filter "FullyQualifiedName!~Live"
```

## Task 7: Add SVE Slice 1 Scenario 02

Use the new Frobby APIs from another mod repo without changing Frobby for SVE-specific behavior.

File:

- `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/02-sve-custom-locations-register.test.json`

Scenario outline:

```json
{
  "id": "02-sve-custom-locations-register",
  "name": "SVE custom locations register and expose map state",
  "description": "Verifies SVE registers representative custom locations and Frobby can inspect a custom map after a direct warp.",
  "steps": [
    {
      "label": "Open SVE save and wait for world readiness",
      "action": "wait.world_ready"
    },
    {
      "label": "Verify representative SVE custom locations are loaded",
      "assertions": [
        "state.locations.locations contains name 'Custom_TownEast'",
        "state.locations.locations contains name 'Custom_GrandpasShed'",
        "state.locations.locations contains name 'Custom_EnchantedGrove'"
      ]
    },
    {
      "label": "Warp to SVE Town East",
      "action": "player.warp",
      "args": {
        "location": "Custom_TownEast",
        "x": 10,
        "y": 10
      }
    },
    {
      "label": "Wait for custom location transition",
      "action": "wait.location",
      "args": {
        "location": "Custom_TownEast",
        "timeout_ms": 5000
      }
    },
    {
      "label": "Inspect current custom map",
      "assertions": [
        "state.player.location == 'Custom_TownEast'",
        "state.location.name == 'Custom_TownEast'",
        "state.location.map_width != 0",
        "state.location.map_height != 0",
        "state.map_tile.location == 'Custom_TownEast'",
        "state.map_tile.layers contains name 'Back'"
      ]
    }
  ]
}
```

If the SVE location names differ in the live mod package, update only the SVE scenario to use names discovered through `state.locations` output. Do not add aliases or SVE-specific fallback names to Frobby.

Run SVE verification:

```bash
env FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test tests/sdv/02-sve-custom-locations-register.test.json
```

Expected report root:

```text
/tmp/stardew-valley-expanded-frobby-results-0.1.0/02-sve-custom-locations-register/index.html
```

## Task 8: Regression Verification

Run targeted tests first:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --configuration Debug --filter "LocationsStateSerializationTests|MapTileStateSerializationTests|LocationStateSerializationTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter "StateLocationHandlerTests|StateLocationsHandlerTests|StateMapTileHandlerTests"
dotnet test tests/Runner.Dsl.Tests/Runner.Dsl.Tests.csproj --configuration Debug --filter State
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter ScenarioRunnerTests
```

Run full Frobby suite:

```bash
dotnet test sdv-test-framework.slnx --configuration Debug
```

Run SVE live scenario:

```bash
env FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test tests/sdv/02-sve-custom-locations-register.test.json
```

Run Starberg smoke to catch scaffold regressions:

```bash
env FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test tests/sdv/01-starberg-terminal-opens.test.json
```

## Completion Criteria

- Frobby has neutral `state.locations`, expanded `state.location`, and `state.map_tile` RPCs.
- C# DSL exposes the new state methods.
- Scenario runner supports `wait.location`.
- Docs describe every new RPC/action.
- SVE has scenario 02 proving custom location registration and custom map introspection.
- Frobby full suite passes.
- SVE scenario 02 passes live headless.
- Starberg smoke still passes.
- `SVE_FROBBY_CAPABILITY_TODO.md` marks Slice 1 introspection foundation complete and leaves tile action execution as the next Slice 1 follow-up.

## Follow-Up After This Plan

Completed 2026-05-07 as the Slice 1 follow-up:

- Added `state.tile_actions` to list nearby `Action` and `TouchAction` candidates.
- Added `world.interact_tile_action` for map action execution. `Action` uses
  Stardew's direct action path; `TouchAction` first moves the farmer onto the tile,
  then invokes Stardew's direct touch-action path so update/tick-driven mods receive
  a player-like tile-transition signal.
- Added SVE scenario 06 (`sve_tile_action_warp`) for Blue Moon Vineyard
  `TouchAction` discovery and warp execution.
