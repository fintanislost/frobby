# SVE Slice 11 Fishing Tables Catch Sampling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby support for fishing context inspection, fishing table projection, and deterministic runtime catch sampling, then prove it against core SVE and Frontier Farm.

**Architecture:** Add additive protocol DTOs for fishing context, table, and sample results. Implement three harness RPCs behind small fake-friendly abstractions: `state.fishing_context`, `state.fishing_table`, and `fishing.sample_catch`. Keep SVE and Frontier Farm details in scenario JSON and repo-local config only; Frobby production code should know Stardew concepts, not mod-specific IDs.

**Tech Stack:** C#/.NET 10 and .NET 6 projects, xUnit, SMAPI/Stardew Valley runtime APIs, Frobby JSON-RPC protocol, Frobby JSON scenario runner, SVE repo-local `scripts/sdv-test`.

---

## Branch And Repo Notes

Frobby work starts from clean `main`:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
git status --short --branch
git worktree add .worktrees/sve-slice-11-fishing -b feature/sve-slice-11-fishing
cd .worktrees/sve-slice-11-fishing
```

Expected: `git status --short --branch` initially prints `## main` before the worktree is created.

SVE scenario work starts from the current SVE Frobby branch, not `master`:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
git status --short --branch
git switch -c feature/frobby-sve-slice-11-fishing
```

Do not merge SVE back to `master` unless the user explicitly asks. Frobby can merge to `main` after review and verification.

Use headless SVE runs with the feature worktree:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-11-fishing ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-11-core tests/sdv/16-sve-fishing-core.test.json
```

Frontier Farm verification uses a separate mod set added in this plan:

```bash
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-11-fishing ./scripts/sdv-test --headless --mod-set frontier-farm --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-11-frontier tests/sdv/17-sve-frontier-farm-fishing.test.json
```

## File Structure

Frobby:

- Create `src/Protocol/Models/FishingState.cs`
  - Request/response DTOs for `state.fishing_context`, `state.fishing_table`, and `fishing.sample_catch`.
- Create `src/Harness/Handlers/FishingProjection.cs`
  - Shared fake-friendly projection helpers and interfaces for fishing context, table candidates, and catch result items.
- Create `src/Harness/Handlers/StateFishingContextHandler.cs`
  - RPC handler and production adapter for tile fishability, map fish property, `NoFishing`, and fish-area metadata.
- Create `src/Harness/Handlers/StateFishingTableHandler.cs`
  - RPC handler and production adapter for live `Data/Locations`, `Data/Fish`, and map `Fish` candidate summaries.
- Create `src/Harness/Handlers/FishingSampleCatchHandler.cs`
  - RPC handler and production adapter for bounded live `GameLocation.getFish` sampling.
- Modify `src/Harness/ModEntry.cs`
  - Register the three RPCs and include them in the startup log.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs`
  - Add assertion handling for `state.fishing_context`, `state.fishing_table`, and `fishing.sample_catch` using existing `params` and `expr` fields.
- Modify `schemas/scenario.schema.json`
  - Document fishing assertion/action usage in descriptions while preserving the generic `type`/`action` shape.
- Modify docs after implementation:
  - `README.md`
  - `docs/rpc-schema.md`
  - `docs/dsl-quickstart.md`
  - `SVE_FROBBY_CAPABILITY_TODO.md`
- Tests:
  - Create `tests/Protocol.Tests/FishingStateSerializationTests.cs`
  - Create `tests/Harness.Tests/StateFishingContextHandlerTests.cs`
  - Create `tests/Harness.Tests/StateFishingTableHandlerTests.cs`
  - Create `tests/Harness.Tests/FishingSampleCatchHandlerTests.cs`
  - Create `tests/Runner.Tests/ScenarioRunnerFishingTests.cs`

SVE:

- Modify `sdv-test.config.json`
  - Add a `frontier-farm` mod set with core SVE mods plus Frontier Farm CP/FTM packs staged from `.cache/frobby-game-mods`.
- Add `tests/sdv/16-sve-fishing-core.test.json`
  - Core SVE fishing proof.
- Add `tests/sdv/17-sve-frontier-farm-fishing.test.json`
  - Frontier Farm fishing-area proof.

---

### Task 1: Fishing Protocol Models

**Files:**
- Create: `src/Protocol/Models/FishingState.cs`
- Test: `tests/Protocol.Tests/FishingStateSerializationTests.cs`

- [ ] **Step 1: Write the failing serialization test**

Create `tests/Protocol.Tests/FishingStateSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class FishingStateSerializationTests
{
    [Fact]
    public void Serialize_FishingContext_UsesSnakeCase()
    {
        var state = new FishingContextState
        {
            Location = "Custom_FerngillRepublicFrontier",
            LocationName = "Frontier Farm",
            LocationType = "GameLocation",
            Tile = new TilePoint { X = 12, Y = 144 },
            Season = "spring",
            TimeOfDay = 900,
            Weather = "sunny",
            DailyLuck = 0.025,
            IsWater = true,
            IsFishable = true,
            FishAreaId = "Ocean",
            MapFish = "128 .08 129 .2",
            HasNoFishing = false,
            TileProperties =
            {
                new FishingTileLayerProperties
                {
                    Layer = "Back",
                    Properties = { ["Water"] = "T" },
                },
            },
            LocationFishAreas =
            {
                new FishingAreaSummary
                {
                    Id = "Ocean",
                    DisplayName = "Ocean",
                    Position = new RectangleSummary { X = 0, Y = 140, Width = 155, Height = 15 },
                    CrabPotFishTypes = { "ocean" },
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"location_name\":\"Frontier Farm\"", json);
        Assert.Contains("\"time_of_day\":900", json);
        Assert.Contains("\"daily_luck\":0.025", json);
        Assert.Contains("\"is_fishable\":true", json);
        Assert.Contains("\"fish_area_id\":\"Ocean\"", json);
        Assert.Contains("\"has_no_fishing\":false", json);
        Assert.Contains("\"location_fish_areas\"", json);
        Assert.Contains("\"crab_pot_fish_types\":[\"ocean\"]", json);
    }

    [Fact]
    public void Serialize_FishingTable_IncludesCandidatesAndSources()
    {
        var table = new FishingTableState
        {
            Context = new FishingContextState { Location = "Beach", Tile = new TilePoint { X = 45, Y = 12 } },
            RawSources = { "map_fish", "data_fish" },
            Candidates =
            {
                new FishingCatchCandidate
                {
                    Id = "FlashShifter.StardewValleyExpandedCP_Starfish",
                    ItemId = "FlashShifter.StardewValleyExpandedCP_Starfish",
                    QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Starfish",
                    DisplayName = "Starfish",
                    Type = "fish",
                    FishAreaId = "Ocean",
                    Chance = 0.4,
                    Condition = "LOCATION_Season Here Spring Summer Fall",
                    Source = "data_locations",
                    Raw = "{\"ItemId\":\"(O)FlashShifter.StardewValleyExpandedCP_Starfish\"}",
                },
            },
        };

        var json = JsonSerializer.Serialize(table, ProtocolJson.Options);

        Assert.Contains("\"raw_sources\":[\"map_fish\",\"data_fish\"]", json);
        Assert.Contains("\"qualified_id\":\"(O)FlashShifter.StardewValleyExpandedCP_Starfish\"", json);
        Assert.Contains("\"fish_area_id\":\"Ocean\"", json);
        Assert.Contains("\"source\":\"data_locations\"", json);
    }

    [Fact]
    public void Serialize_FishingSample_IncludesResults()
    {
        var sample = new FishingSampleCatchResult
        {
            Context = new FishingContextState { Location = "Desert", Tile = new TilePoint { X = 28, Y = 6 } },
            Attempts = 2,
            StateRestored = true,
            Results =
            {
                new FishingCatchResult
                {
                    Attempt = 1,
                    ItemId = "2334",
                    QualifiedId = "(F)2334",
                    DisplayName = "Pyramid Decal",
                    Type = "furniture",
                    Stack = 1,
                    Quality = 0,
                    Category = 0,
                    RuntimeType = "Furniture",
                    Source = "runtime",
                    RawId = "2334",
                },
            },
        };

        var json = JsonSerializer.Serialize(sample, ProtocolJson.Options);

        Assert.Contains("\"state_restored\":true", json);
        Assert.Contains("\"attempts\":2", json);
        Assert.Contains("\"display_name\":\"Pyramid Decal\"", json);
        Assert.Contains("\"runtime_type\":\"Furniture\"", json);
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter FishingStateSerializationTests
```

Expected: FAIL because the fishing DTO types do not exist.

- [ ] **Step 3: Add the protocol DTOs**

Create `src/Protocol/Models/FishingState.cs`:

```csharp
using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

public sealed class FishingContextRequest
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? Season { get; set; }
    public int? TimeOfDay { get; set; }
    public string? Weather { get; set; }
    public double? Luck { get; set; }
    public bool IncludeTileLayers { get; set; } = true;
}

public sealed class FishingTableRequest : FishingContextRequest
{
    public bool IncludeRaw { get; set; }
    public int Limit { get; set; } = 100;
}

public sealed class FishingSampleCatchRequest : FishingContextRequest
{
    public int Attempts { get; set; } = 1;
    public int? Seed { get; set; }
    public int? PlayerFishingLevel { get; set; }
    public string? RodId { get; set; }
    public string? BaitId { get; set; }
    public string? TackleId { get; set; }
    public bool RestoreState { get; set; } = true;
}

public sealed class FishingContextState
{
    public string Location { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public string Season { get; set; } = string.Empty;
    public int TimeOfDay { get; set; }
    public string Weather { get; set; } = string.Empty;
    public double? DailyLuck { get; set; }
    public bool IsWater { get; set; }
    public bool IsFishable { get; set; }
    public string BlockedReason { get; set; } = string.Empty;
    public string FishAreaId { get; set; } = string.Empty;
    public string MapFish { get; set; } = string.Empty;
    public bool HasNoFishing { get; set; }
    public List<FishingTileLayerProperties> TileProperties { get; set; } = new();
    public List<FishingAreaSummary> LocationFishAreas { get; set; } = new();
}

public sealed class FishingTileLayerProperties
{
    public string Layer { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new();
}

public sealed class FishingAreaSummary
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public RectangleSummary? Position { get; set; }
    public List<string> CrabPotFishTypes { get; set; } = new();
}

public sealed class RectangleSummary
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class FishingTableState
{
    public FishingContextState Context { get; set; } = new();
    public List<FishingCatchCandidate> Candidates { get; set; } = new();
    public List<string> RawSources { get; set; } = new();
}

public sealed class FishingCatchCandidate
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string FishAreaId { get; set; } = string.Empty;
    public double? Chance { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public string Weather { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Raw { get; set; } = string.Empty;
}

public sealed class FishingSampleCatchResult
{
    public FishingContextState Context { get; set; } = new();
    public int Attempts { get; set; }
    public bool StateRestored { get; set; }
    public List<FishingCatchResult> Results { get; set; } = new();
}

public sealed class FishingCatchResult
{
    public int Attempt { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int? Quality { get; set; }
    public int? Category { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
    public bool IsNull { get; set; }
    public string Source { get; set; } = string.Empty;
    public string RawId { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Run the protocol tests and verify GREEN**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter FishingStateSerializationTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Protocol/Models/FishingState.cs tests/Protocol.Tests/FishingStateSerializationTests.cs
git commit -m "feat: add fishing protocol models"
```

### Task 2: Fishing Context And Table Projection

**Files:**
- Create: `src/Harness/Handlers/FishingProjection.cs`
- Create: `src/Harness/Handlers/StateFishingContextHandler.cs`
- Create: `src/Harness/Handlers/StateFishingTableHandler.cs`
- Test: `tests/Harness.Tests/StateFishingContextHandlerTests.cs`
- Test: `tests/Harness.Tests/StateFishingTableHandlerTests.cs`

- [ ] **Step 1: Write failing context handler tests**

Create `tests/Harness.Tests/StateFishingContextHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateFishingContextHandlerTests
{
    [Fact]
    public void Handle_ProjectsFishableTileWithFishAreaAndMapFish()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingContextRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
        });

        var result = StateFishingContextHandler.Handle(req, world);
        var state = JsonSerializer.Deserialize<FishingContextState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Equal("Custom_FerngillRepublicFrontier", state.Location);
        Assert.Equal("Ocean", state.FishAreaId);
        Assert.True(state.IsWater);
        Assert.True(state.IsFishable);
        Assert.False(state.HasNoFishing);
        Assert.Equal("128 .08 129 .2", state.MapFish);
        Assert.Collection(state.LocationFishAreas, area =>
        {
            Assert.Equal("Ocean", area.Id);
            Assert.Equal(0, area.Position!.X);
            Assert.Equal(140, area.Position.Y);
            Assert.Equal(155, area.Position.Width);
            Assert.Equal(15, area.Position.Height);
            Assert.Contains("ocean", area.CrabPotFishTypes);
        });
    }

    [Fact]
    public void Handle_ProjectsNoFishingBlockedReason()
    {
        var world = FakeFishingWorld.Sample();
        world.HasNoFishing = true;
        world.IsFishable = false;
        var req = ProtocolJson.ToElement(new FishingContextRequest { Location = "Mountain", X = 45, Y = 31 });

        var result = StateFishingContextHandler.Handle(req, world);
        var state = JsonSerializer.Deserialize<FishingContextState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.True(state.HasNoFishing);
        Assert.False(state.IsFishable);
        Assert.Equal("no_fishing", state.BlockedReason);
    }

    [Fact]
    public void Handle_RejectsNegativeTile()
    {
        var req = ProtocolJson.ToElement(new FishingContextRequest { Location = "Mountain", X = -1, Y = 0 });

        var ex = Assert.Throws<JsonRpcException>(() => StateFishingContextHandler.Handle(req, FakeFishingWorld.Sample()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("x", ex.Message);
    }
}
```

- [ ] **Step 2: Write failing table handler tests**

Create `tests/Harness.Tests/StateFishingTableHandlerTests.cs`:

```csharp
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateFishingTableHandlerTests
{
    [Fact]
    public void Handle_ProjectsDataLocationsAndMapFishCandidates()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingTableRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
            IncludeRaw = true,
            Limit = 20,
        });

        var result = StateFishingTableHandler.Handle(req, world);
        var table = JsonSerializer.Deserialize<FishingTableState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Contains("data_locations", table.RawSources);
        Assert.Contains("map_fish", table.RawSources);
        Assert.Contains(table.Candidates, c =>
            c.QualifiedId == "(O)FlashShifter.StardewValleyExpandedCP_Starfish"
            && c.FishAreaId == "Ocean"
            && c.Source == "data_locations");
        Assert.Contains(table.Candidates, c => c.ItemId == "128" && c.Source == "map_fish");
    }

    [Fact]
    public void Handle_AppliesCandidateLimit()
    {
        var world = FakeFishingWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingTableRequest
        {
            Location = "Custom_FerngillRepublicFrontier",
            X = 12,
            Y = 144,
            Limit = 1,
        });

        var result = StateFishingTableHandler.Handle(req, world);
        var table = JsonSerializer.Deserialize<FishingTableState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Single(table.Candidates);
    }

    [Fact]
    public void Handle_RejectsNonPositiveLimit()
    {
        var req = ProtocolJson.ToElement(new FishingTableRequest { Location = "Beach", X = 1, Y = 1, Limit = 0 });

        var ex = Assert.Throws<SdvTestFramework.Protocol.JsonRpcException>(() =>
            StateFishingTableHandler.Handle(req, FakeFishingWorld.Sample()));

        Assert.Contains("limit", ex.Message);
    }
}
```

- [ ] **Step 3: Add the shared fake-friendly projection interfaces**

Create `src/Harness/Handlers/FishingProjection.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public interface IFishingWorld
{
    IFishingLocation ResolveLocation(string? name);
    TilePoint ResolveTile(IFishingLocation location, int? x, int? y);
    string Season { get; }
    int TimeOfDay { get; }
    string Weather { get; }
    double? DailyLuck { get; }
}

public interface IFishingLocation
{
    string Location { get; }
    string LocationName { get; }
    string LocationType { get; }
    bool IsWater(TilePoint tile);
    bool IsFishable(TilePoint tile);
    bool HasNoFishing(TilePoint tile);
    string MapFish { get; }
    string ResolveFishAreaId(TilePoint tile);
    IReadOnlyList<FishingTileLayerProperties> TileProperties(TilePoint tile);
    IReadOnlyList<FishingAreaSummary> FishAreas { get; }
    IReadOnlyList<FishingCatchCandidate> DataLocationCandidates { get; }
    string DisplayNameForItem(string itemIdOrQualifiedId);
}

internal static class FishingProjection
{
    public static FishingContextState BuildContext(IFishingWorld world, FishingContextRequest req)
    {
        ValidateTile(req.X, req.Y);
        var location = world.ResolveLocation(req.Location);
        var tile = world.ResolveTile(location, req.X, req.Y);
        var hasNoFishing = location.HasNoFishing(tile);
        var isWater = location.IsWater(tile);
        var isFishable = location.IsFishable(tile);
        return new FishingContextState
        {
            Location = location.Location,
            LocationName = location.LocationName,
            LocationType = location.LocationType,
            Tile = tile,
            Season = req.Season ?? world.Season,
            TimeOfDay = req.TimeOfDay ?? world.TimeOfDay,
            Weather = req.Weather ?? world.Weather,
            DailyLuck = req.Luck ?? world.DailyLuck,
            IsWater = isWater,
            IsFishable = isFishable,
            BlockedReason = isFishable ? string.Empty : hasNoFishing ? "no_fishing" : isWater ? "not_fishable" : "not_water",
            FishAreaId = location.ResolveFishAreaId(tile),
            MapFish = location.MapFish,
            HasNoFishing = hasNoFishing,
            TileProperties = req.IncludeTileLayers ? location.TileProperties(tile).ToList() : new(),
            LocationFishAreas = location.FishAreas.ToList(),
        };
    }

    public static FishingTableState BuildTable(IFishingWorld world, FishingTableRequest req)
    {
        if (req.Limit <= 0)
            throw new SdvTestFramework.Protocol.JsonRpcException(
                SdvTestFramework.Protocol.JsonRpcErrorCode.InvalidParams,
                "params.limit must be > 0");

        var location = world.ResolveLocation(req.Location);
        var context = BuildContext(world, req);
        var candidates = new List<FishingCatchCandidate>();
        var sources = new List<string>();

        if (location.DataLocationCandidates.Count > 0)
        {
            sources.Add("data_locations");
            candidates.AddRange(location.DataLocationCandidates);
        }

        foreach (var candidate in ParseMapFish(location.MapFish, location))
            candidates.Add(candidate);
        if (!string.IsNullOrWhiteSpace(location.MapFish))
            sources.Add("map_fish");

        return new FishingTableState
        {
            Context = context,
            Candidates = candidates.Take(req.Limit).ToList(),
            RawSources = sources.Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    public static IReadOnlyList<FishingCatchCandidate> ParseMapFish(string raw, IFishingLocation location)
    {
        var result = new List<FishingCatchCandidate>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var id = parts[i];
            double? chance = double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
            result.Add(new FishingCatchCandidate
            {
                Id = id,
                ItemId = id,
                QualifiedId = id.StartsWith("(O)", StringComparison.Ordinal) ? id : $"(O){id}",
                DisplayName = location.DisplayNameForItem(id),
                Type = "fish",
                Chance = chance,
                Source = "map_fish",
                Raw = $"{id} {parts[i + 1]}",
            });
        }
        return result;
    }

    private static void ValidateTile(int? x, int? y)
    {
        if (x is < 0)
            throw new SdvTestFramework.Protocol.JsonRpcException(
                SdvTestFramework.Protocol.JsonRpcErrorCode.InvalidParams,
                "params.x must be >= 0");
        if (y is < 0)
            throw new SdvTestFramework.Protocol.JsonRpcException(
                SdvTestFramework.Protocol.JsonRpcErrorCode.InvalidParams,
                "params.y must be >= 0");
    }
}
```

- [ ] **Step 4: Add the context and table handlers**

Create `src/Harness/Handlers/StateFishingContextHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class StateFishingContextHandler
{
    public const string Method = "state.fishing_context";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvFishingWorld());

    public static JsonElement Handle(JsonElement? paramsElement, IFishingWorld world)
    {
        var req = RpcParams.Optional<FishingContextRequest>(paramsElement);
        return ProtocolJson.ToElement(FishingProjection.BuildContext(world, req));
    }
}
```

Create `src/Harness/Handlers/StateFishingTableHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class StateFishingTableHandler
{
    public const string Method = "state.fishing_table";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvFishingWorld());

    public static JsonElement Handle(JsonElement? paramsElement, IFishingWorld world)
    {
        var req = RpcParams.Optional<FishingTableRequest>(paramsElement);
        return ProtocolJson.ToElement(FishingProjection.BuildTable(world, req));
    }
}
```

In the same task, add `SdvFishingWorld` and its location adapter to `FishingProjection.cs` below the fake-friendly projection code. Keep Stardew references inside this adapter. Use these production rules:

```csharp
// Production adapter requirements:
// - ResolveLocation uses Game1.currentLocation when name is blank, otherwise Game1.getLocationFromName(name).
// - ResolveTile uses req x/y when supplied, otherwise Game1.player.TilePoint.
// - IsWater uses GameLocation.isOpenWater(x, y) when available.
// - IsFishable uses GameLocation.isTileFishable(tile) when available; fallback is IsWater && !HasNoFishing.
// - HasNoFishing checks Back, Buildings, and Front tile properties for key "NoFishing".
// - MapFish reads location.Map.Properties["Fish"] when present.
// - FishAreas and DataLocationCandidates read live Game1.content.Load<Dictionary<string, LocationData>>("Data/Locations")
//   and project the selected location entry when present.
// - DisplayNameForItem uses ItemRegistry.Create(idOrQualifiedId) best-effort and returns an empty string on failure.
```

- [ ] **Step 5: Add fake test helpers**

Append this helper to each harness test file, or put it once in `tests/Harness.Tests/FakeFishingWorld.cs` if both tests need it:

```csharp
using System.Collections.Generic;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Tests;

internal class FakeFishingWorld : IFishingWorld
{
    public bool IsWater { get; set; } = true;
    public bool IsFishable { get; set; } = true;
    public bool HasNoFishing { get; set; }
    public string Season { get; set; } = "spring";
    public int TimeOfDay { get; set; } = 900;
    public string Weather { get; set; } = "sunny";
    public double? DailyLuck { get; set; } = 0.025;
    public FakeFishingLocation Location { get; } = new();

    public static FakeFishingWorld Sample()
    {
        var world = new FakeFishingWorld();
        world.Location.World = world;
        return world;
    }

    public IFishingLocation ResolveLocation(string? name) => Location;
    public TilePoint ResolveTile(IFishingLocation location, int? x, int? y) => new() { X = x ?? 0, Y = y ?? 0 };
}

internal sealed class FakeFishingLocation : IFishingLocation
{
    public FakeFishingWorld World { get; set; } = null!;
    public string Location => "Custom_FerngillRepublicFrontier";
    public string LocationName => "Frontier Farm";
    public string LocationType => "GameLocation";
    public string MapFish => "128 .08 129 .2";
    public IReadOnlyList<FishingAreaSummary> FishAreas { get; } =
    [
        new FishingAreaSummary
        {
            Id = "Ocean",
            DisplayName = "Ocean",
            Position = new RectangleSummary { X = 0, Y = 140, Width = 155, Height = 15 },
            CrabPotFishTypes = { "ocean" },
        },
    ];
    public IReadOnlyList<FishingCatchCandidate> DataLocationCandidates { get; } =
    [
        new FishingCatchCandidate
        {
            Id = "FlashShifter.FrontierFarm_Starfish",
            ItemId = "(O)FlashShifter.StardewValleyExpandedCP_Starfish",
            QualifiedId = "(O)FlashShifter.StardewValleyExpandedCP_Starfish",
            DisplayName = "Starfish",
            Type = "fish",
            FishAreaId = "Ocean",
            Condition = "LOCATION_Season Here Spring Summer Fall",
            Source = "data_locations",
        },
    ];

    public bool IsWater(TilePoint tile) => World.IsWater;
    public bool IsFishable(TilePoint tile) => World.IsFishable;
    public bool HasNoFishing(TilePoint tile) => World.HasNoFishing;
    public string ResolveFishAreaId(TilePoint tile) => "Ocean";
    public IReadOnlyList<FishingTileLayerProperties> TileProperties(TilePoint tile) =>
    [
        new FishingTileLayerProperties { Layer = "Back", Properties = { ["Water"] = "T" } },
    ];
    public string DisplayNameForItem(string itemIdOrQualifiedId) => itemIdOrQualifiedId == "128" ? "Pufferfish" : "";
}
```

- [ ] **Step 6: Run harness tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "StateFishingContextHandlerTests|StateFishingTableHandlerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Harness/Handlers/FishingProjection.cs src/Harness/Handlers/StateFishingContextHandler.cs src/Harness/Handlers/StateFishingTableHandler.cs tests/Harness.Tests/StateFishingContextHandlerTests.cs tests/Harness.Tests/StateFishingTableHandlerTests.cs tests/Harness.Tests/FakeFishingWorld.cs
git commit -m "feat: add fishing context and table state"
```

### Task 3: Runtime Catch Sampling

**Files:**
- Create: `src/Harness/Handlers/FishingSampleCatchHandler.cs`
- Modify: `src/Harness/Handlers/FishingProjection.cs`
- Test: `tests/Harness.Tests/FishingSampleCatchHandlerTests.cs`

- [ ] **Step 1: Write failing sampler tests**

Create `tests/Harness.Tests/FishingSampleCatchHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FishingSampleCatchHandlerTests
{
    [Fact]
    public void Handle_ReturnsRuntimeResultsAndRestoresState()
    {
        var world = FakeFishingSamplerWorld.Sample();
        var req = ProtocolJson.ToElement(new FishingSampleCatchRequest
        {
            Location = "Desert",
            X = 28,
            Y = 6,
            Attempts = 2,
            Seed = 1234,
            RestoreState = true,
        });

        var result = FishingSampleCatchHandler.Handle(req, world);
        var sample = JsonSerializer.Deserialize<FishingSampleCatchResult>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Equal(2, sample.Attempts);
        Assert.True(sample.StateRestored);
        Assert.True(world.RestoreCalled);
        Assert.Collection(sample.Results,
            first => Assert.Equal("Pyramid Decal", first.DisplayName),
            second => Assert.Equal("Sandfish", second.DisplayName));
    }

    [Fact]
    public void Handle_RejectsNonPositiveAttempts()
    {
        var req = ProtocolJson.ToElement(new FishingSampleCatchRequest { Location = "Desert", X = 28, Y = 6, Attempts = 0 });

        var ex = Assert.Throws<JsonRpcException>(() => FishingSampleCatchHandler.Handle(req, FakeFishingSamplerWorld.Sample()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("attempts", ex.Message);
    }

    [Fact]
    public void Handle_RejectsLargeAttemptCount()
    {
        var req = ProtocolJson.ToElement(new FishingSampleCatchRequest { Location = "Desert", X = 28, Y = 6, Attempts = 101 });

        var ex = Assert.Throws<JsonRpcException>(() => FishingSampleCatchHandler.Handle(req, FakeFishingSamplerWorld.Sample()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("attempts", ex.Message);
    }
}
```

- [ ] **Step 2: Add sampler interfaces and handler**

Create `src/Harness/Handlers/FishingSampleCatchHandler.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public interface IFishingSamplerWorld : IFishingWorld
{
    IFishingSampleState Snapshot(FishingSampleCatchRequest request);
    FishingCatchResult SampleCatch(FishingSampleCatchRequest request, TilePoint tile, int attempt);
}

public interface IFishingSampleState
{
    void Restore();
}

public static class FishingSampleCatchHandler
{
    public const string Method = "fishing.sample_catch";
    private const int MaxAttempts = 100;

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvFishingWorld());

    public static JsonElement Handle(JsonElement? paramsElement, IFishingSamplerWorld world)
    {
        var req = RpcParams.Optional<FishingSampleCatchRequest>(paramsElement);
        if (req.Attempts <= 0)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.attempts must be > 0");
        if (req.Attempts > MaxAttempts)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"params.attempts must be <= {MaxAttempts}");

        var context = FishingProjection.BuildContext(world, req);
        var state = req.RestoreState ? world.Snapshot(req) : null;
        var results = new List<FishingCatchResult>();
        try
        {
            for (var i = 1; i <= req.Attempts; i++)
                results.Add(world.SampleCatch(req, context.Tile, i));
        }
        finally
        {
            state?.Restore();
        }

        return ProtocolJson.ToElement(new FishingSampleCatchResult
        {
            Context = context,
            Attempts = req.Attempts,
            StateRestored = state is not null,
            Results = results,
        });
    }
}
```

Extend `SdvFishingWorld` in `FishingProjection.cs` to implement `IFishingSamplerWorld`. Production requirements:

```csharp
// SdvFishingWorld sampling requirements:
// - Snapshot current player location, time, season, weather flags, Game1.random, fishing level,
//   current tool, bait, and tackle where accessible.
// - Apply request seed by assigning Game1.random = new Random(seed.Value) before attempts.
// - Resolve the location and bobber tile exactly as context projection does.
// - Call the live GameLocation.getFish path so Harmony patches such as SVE Desert rewards run.
// - Project returned StardewValley.Item into FishingCatchResult using ItemId, QualifiedItemId,
//   DisplayName/Name, Stack, Quality for Object, Category for Object, and GetType().Name.
// - Return IsNull = true with Type = "null" if getFish returns null.
// - Restore all snapshotted state when requested.
```

- [ ] **Step 3: Add fake sampler world**

Add this to `tests/Harness.Tests/FakeFishingSamplerWorld.cs`:

```csharp
using System.Collections.Generic;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Tests;

internal sealed class FakeFishingSamplerWorld : FakeFishingWorld, IFishingSamplerWorld
{
    public bool RestoreCalled { get; set; }

    public static new FakeFishingSamplerWorld Sample()
    {
        var world = new FakeFishingSamplerWorld();
        world.Location.World = world;
        return world;
    }

    public IFishingSampleState Snapshot(FishingSampleCatchRequest request) => new FakeSampleState(this);

    public FishingCatchResult SampleCatch(FishingSampleCatchRequest request, TilePoint tile, int attempt)
        => attempt == 1
            ? new FishingCatchResult
            {
                Attempt = attempt,
                ItemId = "2334",
                QualifiedId = "(F)2334",
                DisplayName = "Pyramid Decal",
                Type = "furniture",
                Stack = 1,
                RuntimeType = "Furniture",
                Source = "runtime",
                RawId = "2334",
            }
            : new FishingCatchResult
            {
                Attempt = attempt,
                ItemId = "164",
                QualifiedId = "(O)164",
                DisplayName = "Sandfish",
                Type = "fish",
                Stack = 1,
                RuntimeType = "Object",
                Source = "runtime",
                RawId = "164",
            };

    private sealed class FakeSampleState(FakeFishingSamplerWorld world) : IFishingSampleState
    {
        public void Restore() => world.RestoreCalled = true;
    }
}
```

- [ ] **Step 4: Run sampler tests and verify GREEN**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter FishingSampleCatchHandlerTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Harness/Handlers/FishingSampleCatchHandler.cs src/Harness/Handlers/FishingProjection.cs tests/Harness.Tests/FishingSampleCatchHandlerTests.cs tests/Harness.Tests/FakeFishingSamplerWorld.cs
git commit -m "feat: add deterministic fishing catch sampling"
```

### Task 4: RPC Registration And JSON Runner Assertions

**Files:**
- Modify: `src/Harness/ModEntry.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `schemas/scenario.schema.json`
- Test: `tests/Runner.Tests/ScenarioRunnerFishingTests.cs`

- [ ] **Step 1: Write failing runner assertion tests**

Create `tests/Runner.Tests/ScenarioRunnerFishingTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioRunnerFishingTests
{
    [Fact]
    public async Task FishingTableAssertion_EvaluatesContainsExpression()
    {
        var json = """
        {
          "context": { "location": "Beach", "tile": { "x": 45, "y": 12 }, "is_fishable": true },
          "raw_sources": ["map_fish"],
          "candidates": [
            { "qualified_id": "(O)FlashShifter.StardewValleyExpandedCP_Starfish", "display_name": "Starfish", "source": "data_locations" }
          ]
        }
        """;
        var (cts, server, client, calls) = await StartFakeHarness(SocketPath(), "state.fishing_table", json);
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "fishing_table_contains",
            Assertions =
            {
                new ScenarioAssertion
                {
                    Type = "state.fishing_table",
                    Params = JsonDocument.Parse("{\"location\":\"Beach\",\"x\":45,\"y\":12}").RootElement,
                    Expr = "result.candidates contains qualified_id '(O)FlashShifter.StardewValleyExpandedCP_Starfish'",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Contains("state.fishing_table", calls);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task FishingSampleAssertion_EvaluatesResultExpression()
    {
        var json = """
        {
          "context": { "location": "Desert", "tile": { "x": 28, "y": 6 }, "is_fishable": true },
          "attempts": 2,
          "state_restored": true,
          "results": [
            { "attempt": 1, "qualified_id": "(F)2334", "display_name": "Pyramid Decal", "type": "furniture" }
          ]
        }
        """;
        var (cts, server, client, calls) = await StartFakeHarness(SocketPath(), "fishing.sample_catch", json);
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "fishing_sample_contains",
            Assertions =
            {
                new ScenarioAssertion
                {
                    Type = "fishing.sample_catch",
                    Params = JsonDocument.Parse("{\"location\":\"Desert\",\"x\":28,\"y\":6,\"attempts\":2,\"seed\":1}").RootElement,
                    Expr = "result.results contains display_name 'Pyramid Decal'",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Contains("fishing.sample_catch", calls);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> StartFakeHarness(
        string socket,
        string fishingMethod,
        string fishingJson)
    {
        var calls = new List<string>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    calls.Add(req.Method);
                    JsonElement r = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        var method when method == fishingMethod => JsonDocument.Parse(fishingJson).RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
                };
                await session.SendNotificationAsync("ready", JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        for (var i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);
        return (cts, serverTask, client, calls);
    }
}
```

- [ ] **Step 2: Add generic result assertion evaluation**

Modify `src/Runner/Scenarios/ScenarioRunner.cs`:

```csharp
// Add cases in EvaluateAssertionAsync before "state":
case "state.fishing_context":
case "state.fishing_table":
case "fishing.sample_catch":
{
    var result = await EvaluateRpcResultAssertionAsync(a.Type, a, ct);
    if (!result.Passed)
        await TryCaptureAssertionFailureAsync(ct);
    return result;
}
```

Add helper methods near `EvaluateContentAssetAssertionAsync`:

```csharp
private async Task<(bool Passed, string? Detail)> EvaluateRpcResultAssertionAsync(
    string method,
    ScenarioAssertion assertion,
    CancellationToken ct)
{
    var resp = await _session.InvokeAsync(method, assertion.Params, ct);
    if (resp.Error is not null)
        return (false, resp.Error.Message);
    if (resp.Result is not { ValueKind: JsonValueKind.Object } root)
        return (false, $"{method} returned no result");
    if (string.IsNullOrWhiteSpace(assertion.Expr))
        return (true, null);
    return EvaluateResultExpression(root, assertion.Expr);
}

private static (bool Passed, string? Detail) EvaluateResultExpression(JsonElement root, string expr)
{
    var trimmed = expr.Trim();
    var containsMatch = System.Text.RegularExpressions.Regex.Match(
        trimmed,
        @"^result\.([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+contains(?:\s+([A-Za-z_][A-Za-z0-9_]*))?\s+(['""])(.*?)\3$");
    if (containsMatch.Success)
    {
        var path = "result." + containsMatch.Groups[1].Value;
        var objectField = containsMatch.Groups[2].Success ? containsMatch.Groups[2].Value : null;
        var literal = containsMatch.Groups[4].Value;
        if (!TryResolveResultPath(root, path, out var array))
            return (false, $"{path} was not found");
        if (array.ValueKind != JsonValueKind.Array)
            return (false, $"{path} was not an array");
        foreach (var element in array.EnumerateArray())
        {
            if (objectField is null && element.ValueKind == JsonValueKind.String
                && string.Equals(element.GetString(), literal, StringComparison.Ordinal))
                return (true, null);
            if (objectField is not null
                && element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(objectField, out var field)
                && field.ValueKind == JsonValueKind.String
                && string.Equals(field.GetString(), literal, StringComparison.Ordinal))
                return (true, null);
        }
        return (false, $"expected {path} to contain '{literal}'");
    }

    var eqIdx = trimmed.IndexOf("==", StringComparison.Ordinal);
    if (eqIdx < 0)
        return (false, $"unsupported result expression: {expr}");
    var lhs = trimmed.Substring(0, eqIdx).Trim();
    var rhs = trimmed.Substring(eqIdx + 2).Trim();
    if (!TryResolveResultPath(root, lhs, out var value))
        return (false, $"{lhs} was not found");
    var equal = JsonElementEqualsLiteral(value, rhs);
    return equal is null
        ? (false, $"unsupported literal in result expression: {rhs}")
        : (equal.Value, equal.Value ? null : $"{lhs} did not match {rhs}");
}

private static bool TryResolveResultPath(JsonElement root, string path, out JsonElement value)
{
    value = default;
    var tokens = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length == 0 || tokens[0] != "result")
        return false;
    value = root;
    for (var i = 1; i < tokens.Length; i++)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(tokens[i], out value))
            return false;
    }
    return true;
}
```

- [ ] **Step 3: Register harness RPCs**

Modify `src/Harness/ModEntry.cs` near other `state.*` registrations:

```csharp
_rpc.Register(StateFishingContextHandler.Method, p => StateFishingContextHandler.Handle(p));
_rpc.Register(StateFishingTableHandler.Method, p => StateFishingTableHandler.Handle(p));
_rpc.Register(FishingSampleCatchHandler.Method, p => FishingSampleCatchHandler.Handle(p));
```

Update the startup log string to include:

```text
Fishing: state.fishing_context, state.fishing_table, fishing.sample_catch.
```

- [ ] **Step 4: Update schema descriptions**

Modify `schemas/scenario.schema.json` so the `assertions.items.properties.type.description` includes:

```json
"description": "Assertion kind such as state, content.asset, state.fishing_context, state.fishing_table, fishing.sample_catch, draw.contains, or bitmap."
```

Modify `steps.items.oneOf[0].properties.action.description` to include:

```json
"description": "RPC method or runner action, such as player.warp, state.assert, fishing.sample_catch, screenshot.capture, or wait.ms."
```

- [ ] **Step 5: Run runner tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ScenarioRunnerFishingTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Harness/ModEntry.cs src/Runner/Scenarios/ScenarioRunner.cs schemas/scenario.schema.json tests/Runner.Tests/ScenarioRunnerFishingTests.cs
git commit -m "feat: wire fishing RPCs into runner"
```

### Task 5: Frobby Docs And Capability Backlog

**Files:**
- Modify: `README.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Document RPCs in `docs/rpc-schema.md`**

Add a fishing section:

````markdown
## Fishing

### `state.fishing_context`

Returns fishability and tile context for a location/bobber tile. Useful fields include
`is_fishable`, `blocked_reason`, `fish_area_id`, `map_fish`, `has_no_fishing`,
`tile_properties`, and `location_fish_areas`.

Example:

```json
{
  "location": "Beach",
  "x": 45,
  "y": 12,
  "season": "spring",
  "time_of_day": 900,
  "weather": "sunny"
}
```

### `state.fishing_table`

Returns projected candidate catches for the same context. Candidates can come from
legacy map `Fish` properties, `Data/Fish`, `Data/Locations`, or compact runtime
sources. The table is diagnostic; `fishing.sample_catch` is the authoritative runtime
proof.

### `fishing.sample_catch`

Runs bounded live Stardew catch sampling without the fishing minigame. The sampler
returns projected item results and should use `restore_state: true` for scenario
tests unless the scenario is isolated.
````

- [ ] **Step 2: Add JSON runner examples to `docs/dsl-quickstart.md`**

Add this near the runtime content assertions section:

````markdown
JSON scenarios can inspect fishing tables and sample live catches:

```json
{
  "type": "state.fishing_table",
  "params": { "location": "Beach", "x": 45, "y": 12, "season": "spring", "time_of_day": 900 },
  "expr": "result.candidates contains qualified_id '(O)128'",
  "message": "Beach fishing table should expose pufferfish as a candidate"
},
{
  "type": "fishing.sample_catch",
  "params": { "location": "Desert", "x": 28, "y": 6, "attempts": 10, "seed": 1234, "restore_state": true },
  "expr": "result.results contains display_name 'Pyramid Decal'",
  "message": "Runtime Desert sampling should exercise patched catch results"
}
```
````

- [ ] **Step 3: Update README**

Add one concise bullet to the capabilities list:

```markdown
- Fishing diagnostics: inspect fishable tile context, effective fishing candidates, and bounded runtime catch sampling through `state.fishing_context`, `state.fishing_table`, and `fishing.sample_catch`.
```

- [ ] **Step 4: Mark Slice 11 active in the capability backlog**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 11 entry to include:

```markdown
  - Design spec: `docs/superpowers/specs/2026-05-11-sve-slice-11-fishing-tables-catch-sampling-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-11-sve-slice-11-fishing-tables-catch-sampling.md`.
  - Active: adding `state.fishing_context`, `state.fishing_table`, `fishing.sample_catch`, and two SVE proof scenarios.
```

- [ ] **Step 5: Run docs-adjacent tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "ScenarioLoaderTests|RepoRunPlannerTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/rpc-schema.md docs/dsl-quickstart.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document fishing test support"
```

### Task 6: Core SVE Fishing Scenario

**Files:**
- Add: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/16-sve-fishing-core.test.json`

- [ ] **Step 1: Probe stable core SVE coordinates**

Use existing content inspection to confirm coordinates:

```bash
rg -n "name=\"Fish\"|NoFishing" "/home/fintan/stardewRepos/StardewValleyExpanded/Stardew Valley Expanded/[CP] Stardew Valley Expanded/assets/Maps/Locations/Mountain.tmx" "/home/fintan/stardewRepos/StardewValleyExpanded/Stardew Valley Expanded/[CP] Stardew Valley Expanded/assets/Maps/Locations/Beach.tmx"
```

Expected: Mountain has a map `Fish` property and several `NoFishing` tiles; Beach has a map `Fish` property.

- [ ] **Step 2: Add the scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/16-sve-fishing-core.test.json`:

```json
{
  "name": "sve_fishing_core",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.warp", "args": { "location": "Beach", "x": 45, "y": 12 } },
    { "action": "wait.location", "args": { "name": "Beach", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Data/Fish",
      "asset_type": "data",
      "include_keys": true,
      "keys_limit": 500,
      "expr": "asset.keys contains 'FlashShifter.StardewValleyExpandedCP_Starfish'",
      "message": "Core SVE should register custom fish data"
    },
    {
      "type": "state.fishing_table",
      "params": { "location": "Beach", "x": 45, "y": 12, "season": "spring", "time_of_day": 900, "limit": 50 },
      "expr": "result.raw_sources contains 'map_fish'",
      "message": "Core SVE Beach should expose a map Fish source"
    },
    {
      "type": "state.fishing_table",
      "params": { "location": "Beach", "x": 45, "y": 12, "season": "spring", "time_of_day": 900, "limit": 50 },
      "expr": "result.candidates contains qualified_id '(O)128'",
      "message": "Core SVE Beach map table should expose vanilla fish candidates"
    },
    {
      "type": "state.fishing_context",
      "params": { "location": "Mountain", "x": 1, "y": 1, "season": "spring", "time_of_day": 900 },
      "expr": "result.has_no_fishing == true",
      "message": "Known SVE Mountain blocked tile should expose NoFishing"
    },
    {
      "type": "fishing.sample_catch",
      "params": { "location": "Desert", "x": 28, "y": 6, "season": "spring", "time_of_day": 900, "attempts": 20, "seed": 1234, "restore_state": true },
      "expr": "result.state_restored == true",
      "message": "Fishing sampler should restore state after bounded Desert sampling"
    }
  ]
}
```

If the Mountain `NoFishing` coordinate is not tile `(1,1)` in the live map, replace only the coordinate after probing `state.map_tile`; keep the assertion generic.

- [ ] **Step 3: Dry-run scenario shape**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./tests/scripts/sdv-test-dry-run.sh tests/sdv/16-sve-fishing-core.test.json
```

Expected: PASS shape validation or an equivalent dry-run success message.

- [ ] **Step 4: Run core scenario headlessly**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-11-fishing ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-11-core tests/sdv/16-sve-fishing-core.test.json
```

Expected: PASS. If a coordinate assertion fails, use the report and `state.map_tile` to select a real SVE `NoFishing` tile and rerun.

- [ ] **Step 5: Commit SVE core scenario**

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
git add tests/sdv/16-sve-fishing-core.test.json
git commit -m "test: add core fishing frobby scenario"
```

### Task 7: Frontier Farm Fishing Scenario

**Files:**
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json`
- Add: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/17-sve-frontier-farm-fishing.test.json`

- [ ] **Step 1: Add Frontier Farm mod set**

Modify `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json` to add a second mod set:

```json
{
  "name": "frontier-farm",
  "deps": [
    { "id": "Pathoschild.ContentPatcher" },
    { "id": "Esca.FarmTypeManager" }
  ],
  "extraMods": [
    ".cache/frobby-game-mods/StardewValleyExpanded/StardewValleyExpanded",
    ".cache/frobby-game-mods/StardewValleyExpanded/[CP] Stardew Valley Expanded",
    ".cache/frobby-game-mods/StardewValleyExpanded/[FTM] Stardew Valley Expanded",
    ".cache/frobby-game-mods/StardewValleyExpanded/Frontier Farm/[CP] Frontier Farm",
    ".cache/frobby-game-mods/StardewValleyExpanded/Frontier Farm/[FTM] Frontier Farm"
  ]
}
```

Keep the existing `core` mod set as the default first entry.

- [ ] **Step 2: Dry-run the Frontier mod set plan**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --mod-set frontier-farm --dry-run tests/sdv/01-sve-core-loads.test.json
```

Expected: output includes the two Frontier Farm extra-mod paths and does not fail on config parsing.

- [ ] **Step 3: Add Frontier Farm scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/17-sve-frontier-farm-fishing.test.json`:

```json
{
  "name": "sve_frontier_farm_fishing",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.warp", "args": { "location": "Custom_FerngillRepublicFrontier", "x": 12, "y": 144 } },
    { "action": "wait.location", "args": { "name": "Custom_FerngillRepublicFrontier", "timeout_ms": 15000, "poll_ms": 100 } },
    { "action": "freeze.begin", "args": { "settle_timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state.fishing_context",
      "params": { "location": "Custom_FerngillRepublicFrontier", "x": 12, "y": 144, "season": "spring", "time_of_day": 900 },
      "expr": "result.location_fish_areas contains id 'Ocean'",
      "message": "Frontier Farm should expose the Ocean fish area"
    },
    {
      "type": "state.fishing_context",
      "params": { "location": "Custom_FerngillRepublicFrontier", "x": 12, "y": 144, "season": "spring", "time_of_day": 900 },
      "expr": "result.fish_area_id == 'Ocean'",
      "message": "Frontier Farm ocean tile should resolve to Ocean fish area"
    },
    {
      "type": "state.fishing_table",
      "params": { "location": "Custom_FerngillRepublicFrontier", "x": 12, "y": 144, "season": "spring", "time_of_day": 900, "limit": 100 },
      "expr": "result.candidates contains qualified_id '(O)FlashShifter.StardewValleyExpandedCP_Starfish'",
      "message": "Frontier Farm ocean table should include SVE Starfish"
    },
    {
      "type": "state.fishing_table",
      "params": { "location": "Custom_FerngillRepublicFrontier", "x": 12, "y": 144, "season": "spring", "time_of_day": 900, "limit": 100 },
      "expr": "result.candidates contains qualified_id '(O)128'",
      "message": "Frontier Farm ocean table should include vanilla fish candidates"
    },
    {
      "type": "fishing.sample_catch",
      "params": { "location": "Custom_FerngillRepublicFrontier", "x": 12, "y": 144, "season": "spring", "time_of_day": 900, "attempts": 5, "seed": 436515781, "restore_state": true },
      "expr": "result.state_restored == true",
      "message": "Frontier Farm fishing sample should complete and restore state"
    }
  ]
}
```

- [ ] **Step 4: Run Frontier scenario headlessly**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-11-fishing ./scripts/sdv-test --headless --mod-set frontier-farm --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-11-frontier tests/sdv/17-sve-frontier-farm-fishing.test.json
```

Expected: PASS. If the optional farm pack does not stage through the current build command, fix the SVE build/scaffold config instead of hardcoding Frontier paths in Frobby.

- [ ] **Step 5: Commit SVE Frontier work**

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
git add sdv-test.config.json tests/sdv/17-sve-frontier-farm-fishing.test.json
git commit -m "test: add frontier farm fishing frobby scenario"
```

### Task 8: Full Verification And Completion

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run targeted Frobby tests**

Run from the Frobby feature worktree:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter FishingStateSerializationTests
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "StateFishingContextHandlerTests|StateFishingTableHandlerTests|FishingSampleCatchHandlerTests"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ScenarioRunnerFishingTests
```

Expected: all PASS.

- [ ] **Step 2: Run broader Frobby tests touched by this slice**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "StateMapTileHandlerTests|ContentAssetHandlerTests|PlayerWarpHandlerTests|Fishing"
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "ScenarioRunnerContentAssetTests|ScenarioRunnerFishingTests|RepoRunPlannerTests|ScenarioLoaderTests"
```

Expected: all PASS.

- [ ] **Step 3: Run SVE smoke around new scenarios**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
env SDV_TEST_MOD_CACHE=/home/fintan/stardewRepos/frobby/sdv-test-framework/.cache/deps FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-11-fishing ./scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-11-smoke tests/sdv/01-sve-core-loads.test.json tests/sdv/02-sve-custom-locations-register.test.json tests/sdv/04-sve-content-assets-runtime.test.json tests/sdv/16-sve-fishing-core.test.json
```

Expected: all PASS.

- [ ] **Step 4: Mark Slice 11 complete**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 11 entry:

```markdown
- [x] Done: Slice 11, fishing tables and deterministic catch sampling.
  - Design spec: `docs/superpowers/specs/2026-05-11-sve-slice-11-fishing-tables-catch-sampling-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-11-sve-slice-11-fishing-tables-catch-sampling.md`.
  - SVE pressure: custom fish, custom fish areas, alternate farm fishing tables, and patched desert fishing rewards.
  - Frobby goal: query effective fish tables for a location/tile/time/weather context and sample deterministic catch outcomes without requiring the full fishing minigame.
  - Done: `state.fishing_context`, `state.fishing_table`, and `fishing.sample_catch` verify core SVE fishing data plus Frontier Farm fish-area coverage without mod-specific Frobby branches.
```

- [ ] **Step 5: Commit Frobby completion**

Run:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: mark sve fishing slice complete"
```

- [ ] **Step 6: Final git state check**

Run:

```bash
git status --short --branch
git log --oneline -5
cd /home/fintan/stardewRepos/StardewValleyExpanded
git status --short --branch
git log --oneline -5
```

Expected: both repos clean on their feature branches after commits. Frobby can be merged to `main` after review; SVE remains on its feature branch until the user explicitly says to merge or otherwise integrate.
