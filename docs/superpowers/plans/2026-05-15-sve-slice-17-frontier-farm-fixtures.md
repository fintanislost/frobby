# SVE Slice 17 Frontier Farm Fixtures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral Frobby save-fixture farm-type overrides and prove them against SVE Frontier Farm profile and instant shortcut coverage.

**Architecture:** Frobby will parse scenario-level `save_overrides`, stage overridden fixtures under deterministic derived save names, mutate only staged save XML, and keep source fixtures untouched. SVE will add Frontier Farm profiles, config overlays, and two scenarios that verify Frontier content and instant shortcut map changes through runtime state and content assets.

**Tech Stack:** .NET 6 C#, System.Text.Json, Json.Schema, System.Xml.Linq, Frobby JSON scenarios, Stardew/SMAPI headless runner, Content Patcher profile overlays.

---

## File Map

Frobby files:

- Modify: `schemas/scenario.schema.json` — accept top-level `save_overrides`.
- Modify: `src/Protocol/Models/ScenarioSpec.cs` — add DTOs for save overrides.
- Create: `src/Runner/Fixtures/FarmTypeSaveOverrideApplier.cs` — mutate staged save XML `whichFarm`.
- Create: `src/Runner/Fixtures/ScenarioFixtureStageName.cs` — derive stable staged fixture names for overridden saves.
- Modify: `src/Runner/Fixtures/FixtureStager.cs` — allow staging as a derived name and applying overrides after copy.
- Create: `src/Runner/Fixtures/ScenarioFixtureVariantStager.cs` — stage every `(fixture, save_overrides)` variant and substitute effective fixture names during run discovery.
- Modify: `src/Runner/Commands/RunCommand.cs` — call the variant stager instead of staging only by fixture name.
- Test: `tests/Runner.Tests/ScenarioLoaderTests.cs`
- Test: `tests/Runner.Tests/FixtureStagerTests.cs`
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

SVE files:

- Modify: `sdv-test.config.json` — add Frontier Farm profiles.
- Create: `tests/config/frontier-farm/instant-unlocks.json` — profile overlay for CP config.
- Create: `tests/sdv/24-sve-frontier-farm-profile.test.json`
- Create: `tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json`
- Modify: `docs/FROBBY.md`

Known SDV details from local reflection:

- `StardewValley.Farm.mod_layout` is `7` in SDV 1.6.15.
- `Game1.whichModFarm` is `StardewValley.GameData.ModFarmType`.
- `SaveGame.whichFarm` is serialized as a string field. For mod farms, the staged save should write the mod farm id string, e.g. `<whichFarm>FrontierFarm</whichFarm>`. SDV's `SaveGame.LoadFarmType` maps that id to `Game1.whichFarm = Farm.mod_layout` and `Game1.whichModFarm`.

## Task 1: Parse Scenario Save Overrides

**Files:**
- Modify: `schemas/scenario.schema.json`
- Modify: `src/Protocol/Models/ScenarioSpec.cs`
- Test: `tests/Runner.Tests/ScenarioLoaderTests.cs`

- [ ] **Step 1: Write failing loader tests**

Add these tests to `tests/Runner.Tests/ScenarioLoaderTests.cs`:

```csharp
[Fact]
public void Load_WithSaveOverrides_RoundTripsFarmTypeOverride()
{
    var path = WriteTemp("""
{
  "name": "frontier_fixture",
  "fixture": "m0spike_436515781",
  "save_overrides": {
    "farm_type": {
      "which_farm": "mod",
      "mod_farm_id": "FrontierFarm"
    }
  },
  "steps": []
}
""");

    var spec = ScenarioLoader.Load(path);

    Assert.NotNull(spec.SaveOverrides);
    Assert.NotNull(spec.SaveOverrides!.FarmType);
    Assert.Equal("mod", spec.SaveOverrides.FarmType!.WhichFarm);
    Assert.Equal("FrontierFarm", spec.SaveOverrides.FarmType.ModFarmId);
}

[Fact]
public void Load_SaveOverrideFarmTypeWithoutModFarmId_Throws()
{
    var path = WriteTemp("""
{
  "name": "bad_frontier_fixture",
  "fixture": "m0spike_436515781",
  "save_overrides": {
    "farm_type": {
      "which_farm": "mod"
    }
  },
  "steps": []
}
""");

    var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));

    Assert.Contains("schema validation", ex.Message);
    Assert.Contains("mod_farm_id", ex.Message);
}

[Fact]
public void Load_SaveOverrideUnsupportedFarmKind_Throws()
{
    var path = WriteTemp("""
{
  "name": "bad_frontier_fixture",
  "fixture": "m0spike_436515781",
  "save_overrides": {
    "farm_type": {
      "which_farm": "standard",
      "mod_farm_id": "FrontierFarm"
    }
  },
  "steps": []
}
""");

    var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));

    Assert.Contains("schema validation", ex.Message);
    Assert.Contains("which_farm", ex.Message);
}
```

- [ ] **Step 2: Run loader tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests" -v minimal
```

Expected: the new round-trip test fails because `ScenarioSpec.SaveOverrides` does not exist, or the schema rejects `save_overrides`.

- [ ] **Step 3: Add scenario DTOs**

In `src/Protocol/Models/ScenarioSpec.cs`, add a property to `ScenarioSpec`:

```csharp
/// <summary>Optional save-file mutations applied only to staged fixture copies.</summary>
public ScenarioSaveOverrides? SaveOverrides { get; set; }
```

Add DTOs after `ScenarioConfig`:

```csharp
/// <summary>Scenario-level staged save overrides. Source fixtures are never modified.</summary>
public sealed class ScenarioSaveOverrides
{
    /// <summary>Optional farm-type override for additional/modded farm layouts.</summary>
    public ScenarioFarmTypeSaveOverride? FarmType { get; set; }
}

/// <summary>Farm metadata override for a staged save copy.</summary>
public sealed class ScenarioFarmTypeSaveOverride
{
    /// <summary>Farm kind. Currently only "mod" is supported.</summary>
    public string? WhichFarm { get; set; }

    /// <summary>Opaque additional-farm id, such as "FrontierFarm".</summary>
    public string? ModFarmId { get; set; }
}
```

- [ ] **Step 4: Update scenario schema**

In `schemas/scenario.schema.json`, add top-level property `save_overrides`:

```json
"save_overrides": {
  "type": "object",
  "properties": {
    "farm_type": {
      "type": "object",
      "required": ["which_farm", "mod_farm_id"],
      "properties": {
        "which_farm": {
          "type": "string",
          "enum": ["mod"]
        },
        "mod_farm_id": {
          "type": "string",
          "minLength": 1
        }
      },
      "additionalProperties": false
    }
  },
  "additionalProperties": false
}
```

Place it next to `fixture` and `profile`, keeping the existing schema style.

- [ ] **Step 5: Run loader tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests" -v minimal
```

Expected: `ScenarioLoaderTests` pass.

- [ ] **Step 6: Commit Task 1**

```bash
git add schemas/scenario.schema.json src/Protocol/Models/ScenarioSpec.cs tests/Runner.Tests/ScenarioLoaderTests.cs
git commit -m "feat: parse scenario save overrides"
```

## Task 2: Apply Farm-Type Overrides To Staged Saves

**Files:**
- Create: `src/Runner/Fixtures/FarmTypeSaveOverrideApplier.cs`
- Create: `src/Runner/Fixtures/ScenarioFixtureStageName.cs`
- Modify: `src/Runner/Fixtures/FixtureStager.cs`
- Test: `tests/Runner.Tests/FixtureStagerTests.cs`

- [ ] **Step 1: Write failing save override tests**

Add these helpers and tests to `tests/Runner.Tests/FixtureStagerTests.cs`:

```csharp
private static string MinimalSave(string whichFarm)
    => $"""<?xml version="1.0" encoding="utf-8"?><SaveGame><player><name>Tester</name></player><whichFarm>{whichFarm}</whichFarm><gameVersion>1.6.15</gameVersion></SaveGame>""";

private static ScenarioSaveOverrides FrontierOverride()
    => new()
    {
        FarmType = new ScenarioFarmTypeSaveOverride
        {
            WhichFarm = "mod",
            ModFarmId = "FrontierFarm",
        },
    };

[Fact]
public void Stage_WithFarmTypeOverride_MutatesOnlyStagedCopy()
{
    var fixturesRoot = MakeTempDir();
    var sdvSaves = MakeTempDir();
    try
    {
        var src = Path.Combine(fixturesRoot, "myfix", "save");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "<info/>");
        File.WriteAllText(Path.Combine(src, "myfix"), MinimalSave("0"));

        var stagedName = FixtureStager.Stage(
            "myfix",
            fixturesRoot,
            sdvSaves,
            FrontierOverride(),
            stagedName: "myfix__frontier");

        Assert.Equal("myfix__frontier", stagedName);
        Assert.Equal(MinimalSave("0"), File.ReadAllText(Path.Combine(src, "myfix")));
        Assert.Contains("<whichFarm>FrontierFarm</whichFarm>",
            File.ReadAllText(Path.Combine(sdvSaves, "myfix__frontier", "myfix__frontier")));
        Assert.False(File.Exists(Path.Combine(sdvSaves, "myfix__frontier", "myfix")));
        Assert.Equal("<info/>", File.ReadAllText(Path.Combine(sdvSaves, "myfix__frontier", "SaveGameInfo")));
    }
    finally
    {
        Directory.Delete(fixturesRoot, recursive: true);
        Directory.Delete(sdvSaves, recursive: true);
    }
}

[Fact]
public void Stage_WithFarmTypeOverrideMissingWhichFarm_ThrowsClearError()
{
    var fixturesRoot = MakeTempDir();
    var sdvSaves = MakeTempDir();
    try
    {
        var src = Path.Combine(fixturesRoot, "myfix", "save");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "<info/>");
        File.WriteAllText(Path.Combine(src, "myfix"), "<SaveGame><gameVersion>1.6.15</gameVersion></SaveGame>");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixtureStager.Stage("myfix", fixturesRoot, sdvSaves, FrontierOverride(), stagedName: "myfix__frontier"));

        Assert.Contains("whichFarm", ex.Message);
    }
    finally
    {
        Directory.Delete(fixturesRoot, recursive: true);
        Directory.Delete(sdvSaves, recursive: true);
    }
}

[Fact]
public void ScenarioFixtureStageName_DerivesStableNameForOverrides()
{
    var one = ScenarioFixtureStageName.For("m0spike_436515781", FrontierOverride());
    var two = ScenarioFixtureStageName.For("m0spike_436515781", FrontierOverride());

    Assert.Equal(two, one);
    Assert.StartsWith("m0spike_436515781__frobby_", one);
    Assert.NotEqual("m0spike_436515781", one);
}

[Fact]
public void ScenarioFixtureStageName_UsesOriginalNameWithoutOverrides()
{
    Assert.Equal("m0spike_436515781", ScenarioFixtureStageName.For("m0spike_436515781", null));
    Assert.Equal("m0spike_436515781", ScenarioFixtureStageName.For("m0spike_436515781", new ScenarioSaveOverrides()));
}
```

Add `using SdvTestFramework.Protocol.Models;` at the top of the test file.

- [ ] **Step 2: Run fixture stager tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~FixtureStagerTests" -v minimal
```

Expected: compile/test failure because `ScenarioFixtureStageName` and the new `FixtureStager.Stage` overload do not exist.

- [ ] **Step 3: Create farm override applier**

Create `src/Runner/Fixtures/FarmTypeSaveOverrideApplier.cs`:

```csharp
using System;
using System.IO;
using System.Xml.Linq;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

public static class FarmTypeSaveOverrideApplier
{
    public static void Apply(string saveFilePath, ScenarioFarmTypeSaveOverride? farmType)
    {
        if (farmType is null)
            return;

        if (!string.Equals(farmType.WhichFarm, "mod", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"unsupported farm_type.which_farm '{farmType.WhichFarm}'");
        }

        if (string.IsNullOrWhiteSpace(farmType.ModFarmId))
            throw new InvalidOperationException("farm_type.mod_farm_id is required for mod farm overrides");

        if (!File.Exists(saveFilePath))
            throw new FileNotFoundException($"save file not found: {saveFilePath}", saveFilePath);

        var doc = XDocument.Load(saveFilePath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException($"save file has no root element: {saveFilePath}");
        var whichFarm = root.Element("whichFarm")
            ?? throw new InvalidOperationException($"save file is missing whichFarm: {saveFilePath}");

        whichFarm.Value = farmType.ModFarmId!;
        doc.Save(saveFilePath, SaveOptions.DisableFormatting);
    }
}
```

- [ ] **Step 4: Create deterministic staged-name helper**

Create `src/Runner/Fixtures/ScenarioFixtureStageName.cs`:

```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

public static class ScenarioFixtureStageName
{
    public static string For(string fixtureName, ScenarioSaveOverrides? overrides)
    {
        if (string.IsNullOrWhiteSpace(fixtureName))
            throw new ArgumentException("fixture name required", nameof(fixtureName));

        if (overrides?.FarmType is null)
            return fixtureName;

        var json = JsonSerializer.Serialize(overrides, ProtocolJson.Options);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .Substring(0, 10)
            .ToLowerInvariant();
        return $"{fixtureName}__frobby_{hash}";
    }
}
```

- [ ] **Step 5: Extend fixture staging**

Modify `src/Runner/Fixtures/FixtureStager.cs`:

```csharp
using SdvTestFramework.Protocol.Models;
```

Change `Stage` to return the staged fixture name and accept optional overrides:

```csharp
public static string Stage(
    string name,
    string fixturesRoot,
    string sdvSavesDir,
    ScenarioSaveOverrides? saveOverrides = null,
    string? stagedName = null)
{
    var src = Path.Combine(fixturesRoot, name, "save");
    if (!Directory.Exists(src))
        throw new DirectoryNotFoundException(
            $"fixture save directory not found: {src}");

    var destinationName = string.IsNullOrWhiteSpace(stagedName) ? name : stagedName!;
    var dst = Path.Combine(sdvSavesDir, destinationName);
    if (Directory.Exists(dst))
        Directory.Delete(dst, recursive: true);
    CopyRecursive(src, dst);

    RenameMainSaveFile(dst, sourceName: name, destinationName);
    ApplySaveOverrides(dst, destinationName, saveOverrides);
    return destinationName;
}
```

Add helpers below `CaptureFromPath`:

```csharp
private static void RenameMainSaveFile(string saveDir, string sourceName, string destinationName)
{
    var srcFile = Path.Combine(saveDir, sourceName);
    var dstFile = Path.Combine(saveDir, destinationName);
    if (File.Exists(srcFile) && !string.Equals(srcFile, dstFile, System.StringComparison.Ordinal))
        File.Move(srcFile, dstFile);
}

private static void ApplySaveOverrides(
    string saveDir,
    string stagedName,
    ScenarioSaveOverrides? saveOverrides)
{
    if (saveOverrides is null)
        return;

    var saveFile = Path.Combine(saveDir, stagedName);
    FarmTypeSaveOverrideApplier.Apply(saveFile, saveOverrides.FarmType);
}
```

Update `CaptureFromPath` to call `RenameMainSaveFile(dst, sourceFolderName, name)` instead of duplicating the rename logic.

- [ ] **Step 6: Run fixture stager tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~FixtureStagerTests" -v minimal
```

Expected: `FixtureStagerTests` pass.

- [ ] **Step 7: Commit Task 2**

```bash
git add src/Runner/Fixtures/FarmTypeSaveOverrideApplier.cs src/Runner/Fixtures/ScenarioFixtureStageName.cs src/Runner/Fixtures/FixtureStager.cs tests/Runner.Tests/FixtureStagerTests.cs
git commit -m "feat: stage fixture save overrides"
```

## Task 3: Wire Overridden Fixture Variants Into RunCommand

**Files:**
- Create: `src/Runner/Fixtures/ScenarioFixtureVariantStager.cs`
- Modify: `src/Runner/Commands/RunCommand.cs`
- Test: `tests/Runner.Tests/FixtureStagerTests.cs`

- [ ] **Step 1: Write failing variant staging tests**

Add these tests to `tests/Runner.Tests/FixtureStagerTests.cs`:

```csharp
[Fact]
public void VariantStager_StagesBaseAndOverriddenFixtureWithoutReplacingBaseFixture()
{
    var repoRoot = MakeTempDir();
    var sdvSaves = MakeTempDir();
    try
    {
        var fixtures = Path.Combine(repoRoot, "tests", "fixtures", "m0spike_436515781", "save");
        Directory.CreateDirectory(fixtures);
        File.WriteAllText(Path.Combine(fixtures, "SaveGameInfo"), "<info/>");
        File.WriteAllText(Path.Combine(fixtures, "m0spike_436515781"), MinimalSave("0"));

        var standard = new ScenarioSpec { Name = "standard", Fixture = "m0spike_436515781" };
        var frontier = new ScenarioSpec
        {
            Name = "frontier",
            Fixture = "m0spike_436515781",
            SaveOverrides = FrontierOverride(),
        };
        var scenarios = new List<(string Path, ScenarioSpec Spec)>
        {
            ("standard.test.json", standard),
            ("frontier.test.json", frontier),
        };

        var result = ScenarioFixtureVariantStager.StageAll(repoRoot, sdvSaves, scenarios, Console.Error);

        Assert.Equal(0, result);
        Assert.True(File.Exists(Path.Combine(sdvSaves, "m0spike_436515781", "m0spike_436515781")));

        var derived = Directory.GetDirectories(sdvSaves, "m0spike_436515781__frobby_*").Single();
        var derivedName = Path.GetFileName(derived);
        Assert.Contains("<whichFarm>FrontierFarm</whichFarm>", File.ReadAllText(Path.Combine(derived, derivedName)));
        Assert.Contains("<whichFarm>0</whichFarm>",
            File.ReadAllText(Path.Combine(sdvSaves, "m0spike_436515781", "m0spike_436515781")));
    }
    finally
    {
        Directory.Delete(repoRoot, recursive: true);
        Directory.Delete(sdvSaves, recursive: true);
    }
}

[Fact]
public void VariantStager_ApplyEffectiveFixtureNames_UsesDerivedNameForOverridesOnly()
{
    var standard = new ScenarioSpec { Name = "standard", Fixture = "m0spike_436515781" };
    var frontier = new ScenarioSpec
    {
        Name = "frontier",
        Fixture = "m0spike_436515781",
        SaveOverrides = FrontierOverride(),
    };
    var scenarios = new List<(string Path, ScenarioSpec Spec)>
    {
        ("standard.test.json", standard),
        ("frontier.test.json", frontier),
    };

    ScenarioFixtureVariantStager.ApplyEffectiveFixtureNames(scenarios);

    Assert.Equal("m0spike_436515781", standard.Fixture);
    Assert.StartsWith("m0spike_436515781__frobby_", frontier.Fixture);
}
```

- [ ] **Step 2: Run variant staging tests and verify RED**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~FixtureStagerTests" -v minimal
```

Expected: compile/test failure because `ScenarioFixtureVariantStager` does not exist.

- [ ] **Step 3: Add fixture variant staging helper**

Create `src/Runner/Fixtures/ScenarioFixtureVariantStager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

public static class ScenarioFixtureVariantStager
{
    public static int StageAll(
        string repoRoot,
        string sdvSavesDir,
        IReadOnlyList<(string Path, ScenarioSpec Spec)> scenarios,
        TextWriter error)
    {
        var fixturesRoot = Path.Combine(repoRoot, "tests", "fixtures");
        Directory.CreateDirectory(sdvSavesDir);

        if (!Directory.Exists(fixturesRoot))
            return 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, spec) in scenarios)
        {
            if (string.IsNullOrEmpty(spec.Fixture))
                continue;

            var stagedName = ScenarioFixtureStageName.For(spec.Fixture, spec.SaveOverrides);
            var key = $"{spec.Fixture}\n{stagedName}";
            if (!seen.Add(key))
                continue;

            var src = Path.Combine(fixturesRoot, spec.Fixture, "save");
            if (!Directory.Exists(src))
                continue;

            try
            {
                FixtureStager.Stage(
                    spec.Fixture,
                    fixturesRoot,
                    sdvSavesDir,
                    spec.SaveOverrides,
                    stagedName);
            }
            catch (Exception ex)
            {
                error.WriteLine($"[stage-error] fixture '{spec.Fixture}': {ex.Message}");
                return 2;
            }
        }

        return 0;
    }

    public static void ApplyEffectiveFixtureNames(List<(string Path, ScenarioSpec Spec)> scenarios)
    {
        for (var i = 0; i < scenarios.Count; i++)
        {
            var (path, spec) = scenarios[i];
            if (string.IsNullOrEmpty(spec.Fixture))
                continue;

            spec.Fixture = ScenarioFixtureStageName.For(
                spec.Fixture,
                spec.SaveOverrides);
            scenarios[i] = (path, spec);
        }
    }
}
```

- [ ] **Step 4: Replace pre-launch staging block in RunCommand**

In `src/Runner/Commands/RunCommand.cs`, add:

```csharp
using SdvTestFramework.Runner.Fixtures;
```

Replace the existing pre-launch fixture-staging loop with:

```csharp
var sdvSavesDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".config", "StardewValley", "Saves");
var stageExit = ScenarioFixtureVariantStager.StageAll(
    Directory.GetCurrentDirectory(),
    sdvSavesDir,
    scenarios,
    Console.Error);
if (stageExit != 0)
    return stageExit;
```

- [ ] **Step 5: Apply effective names in RunOnceAsync**

In `RunOnceAsync`, after filtering and before `if (scenarios.Count == 0)`, add:

```csharp
ScenarioFixtureVariantStager.ApplyEffectiveFixtureNames(scenarios);
```

Keep the `scenarios.Count == 0` behavior unchanged after the effective-name application.

- [ ] **Step 6: Update watch-mode comment**

Update the `RunOnceAsync` remarks to say fixture variants are staged before launch and effective fixture names are recomputed per run. Keep the existing caveat that watch mode does not restage changed fixture files.

- [ ] **Step 7: Run variant staging tests and verify GREEN**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~FixtureStagerTests" -v minimal
```

Expected: `FixtureStagerTests` pass.

- [ ] **Step 8: Run focused runner suite**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests|FullyQualifiedName~FixtureStagerTests|FullyQualifiedName~RunCommandTests" -v minimal
```

Expected: all focused tests pass.

- [ ] **Step 9: Commit Task 3**

```bash
git add src/Runner/Fixtures/ScenarioFixtureVariantStager.cs src/Runner/Commands/RunCommand.cs tests/Runner.Tests/FixtureStagerTests.cs
git commit -m "feat: stage scenario fixture variants"
```

## Task 4: Document Neutral Save Overrides In Frobby

**Files:**
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Add README guidance**

In `README.md`, under `Authoring Guidance`, add:

```markdown
- Use scenario `save_overrides.farm_type` when a repo scenario needs the same
  base fixture staged as an additional/modded farm type. The override mutates
  only the staged save copy and writes a derived save folder name, so standard
  and modded variants of the same fixture can run in the same suite. Keep mod
  farm ids, such as `FrontierFarm`, in repo scenarios rather than in Frobby
  code.
```

Also add a compact JSON example near the repo profile section:

```json
{
  "name": "alternate_farm_profile",
  "profile": "alternate-farm",
  "fixture": "spring_day_1",
  "save_overrides": {
    "farm_type": {
      "which_farm": "mod",
      "mod_farm_id": "ExampleFarm"
    }
  },
  "steps": []
}
```

- [ ] **Step 2: Add Slice 17 TODO entry as active**

In `SVE_FROBBY_CAPABILITY_TODO.md`, add a Slice 17 entry after Slice 16:

```markdown
- [ ] Active: Slice 17, alternate farm fixtures and Frontier Farm shortcut coverage.
  - SVE pressure: Frontier Farm profile coverage requires the active save to resolve
    as an additional farm type before Content Patcher `FarmType: FrontierFarm`
    conditions and instant shortcut config patches can be proven.
  - Frobby goal: stage neutral save overrides for modded farm types without mutating
    source fixtures or assuming SVE ids.
  - Design spec: `docs/superpowers/specs/2026-05-15-sve-slice-17-frontier-farm-fixtures-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-15-sve-slice-17-frontier-farm-fixtures.md`.
```

Keep final "Done" wording for the implementation task after live verification.

- [ ] **Step 3: Run docs-neutral checks**

Run:

```bash
rg "FrontierFarm|Stardew Valley Expanded|SVE" src schemas README.md
```

Expected: `FrontierFarm` should only appear in README example text if used there. It must not appear in `src` or `schemas`.

- [ ] **Step 4: Commit Task 4**

```bash
git add README.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document fixture save overrides"
```

## Task 5: Add SVE Frontier Farm Profiles And Scenarios

**Files:**
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json`
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/config/frontier-farm/instant-unlocks.json`
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/24-sve-frontier-farm-profile.test.json`
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Confirm SVE branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: clean feature branch. Do not merge SVE into `master`; the user explicitly requires SVE branches not be merged to master unless they say so.

- [ ] **Step 2: Add Frontier Farm profiles**

Modify `sdv-test.config.json` profiles to add:

```json
"sve-frontier-farm": {
  "inherits": "sve-core",
  "extraMods": [
    "Frontier Farm/[CP] Frontier Farm",
    "Frontier Farm/[FTM] Frontier Farm"
  ],
  "cacheNamespace": "sve-frontier-farm"
},
"sve-frontier-farm-instant-unlocks": {
  "inherits": "sve-frontier-farm",
  "configOverlays": [
    {
      "source": "tests/config/frontier-farm/instant-unlocks.json",
      "targetMod": "flashshifter.FrontierFarm",
      "targetPath": "config.json"
    }
  ],
  "cacheNamespace": "sve-frontier-farm-instant-unlocks"
}
```

Keep JSON commas valid around the existing `sve-grandpas-farm` profile.

- [ ] **Step 3: Add Frontier instant unlock overlay**

Create `tests/config/frontier-farm/instant-unlocks.json`:

```json
{
  "InstantlyUnlockBridge": true,
  "InstantlyUnlockDesertShortcut": true
}
```

- [ ] **Step 4: Add Frontier Farm profile scenario**

Create `tests/sdv/24-sve-frontier-farm-profile.test.json`:

```json
{
  "name": "sve_frontier_farm_profile",
  "profile": "sve-frontier-farm",
  "fixture": "m0spike_436515781",
  "save_overrides": {
    "farm_type": {
      "which_farm": "mod",
      "mod_farm_id": "FrontierFarm"
    }
  },
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.warp", "args": { "location": "Farm", "x": 118, "y": 28 } },
    { "action": "wait.location", "args": { "location": "Farm", "timeout_ms": 10000, "poll_ms": 100 } },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.location.map_width == 156",
        "message": "Farm should resolve to Frontier Farm width when the staged save uses mod farm id FrontierFarm"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.location.map_height == 65",
        "message": "Farm should resolve to Frontier Farm height when the staged save uses mod farm id FrontierFarm"
      }
    },
    {
      "action": "state.assert",
      "args": {
        "expr": "state.location.warps contains target_location 'Custom_FrontierFarm_UndergroundTunnel'",
        "message": "Frontier Farm should expose its underground tunnel warp on the runtime Farm location"
      }
    },
    { "action": "freeze.begin", "args": {} },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'flashshifter.FrontierFarm'",
      "message": "Frontier Farm Content Patcher pack should be loaded by the profile"
    },
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'FlashShifter.FrontierFarmFTM'",
      "message": "Frontier Farm FTM pack should be loaded by the profile"
    },
    {
      "type": "content.asset",
      "asset": "Data/AdditionalFarms",
      "asset_type": "data",
      "include_keys": true,
      "keys_limit": 200,
      "expr": "asset.keys contains 'FlashShifter.FrontierFarm/FrontierFarm'",
      "message": "Frontier Farm profile should register the additional farm data entry"
    },
    {
      "type": "content.asset",
      "asset": "Maps/Farm_FrontierFarm",
      "asset_type": "map",
      "expr": "asset.width == 156",
      "message": "Frontier Farm profile should load the Frontier Farm map asset"
    }
  ]
}
```

Use `include_keys` for `Data/AdditionalFarms` because additional farm ids contain `/`, and Frobby's expression dot-path parser treats `/` as part of the path text rather than a quoted key.

- [ ] **Step 5: Add instant unlock scenario**

Create `tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json`:

```json
{
  "name": "sve_frontier_farm_instant_unlocks",
  "profile": "sve-frontier-farm-instant-unlocks",
  "fixture": "m0spike_436515781",
  "save_overrides": {
    "farm_type": {
      "which_farm": "mod",
      "mod_farm_id": "FrontierFarm"
    }
  },
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.warp", "args": { "location": "Farm", "x": 103, "y": 6 } },
    { "action": "wait.location", "args": { "location": "Farm", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "freeze.begin", "args": {} },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Maps/Custom_FrontierFarm_UndergroundTunnel",
      "asset_type": "map",
      "expr": "asset.properties.Warp == '59 -1 Desert 1 27 60 36 Farm 103 6'",
      "message": "Instant desert shortcut config should retarget the underground tunnel warp to the unlocked Desert entrance"
    },
    {
      "type": "content.asset",
      "asset": "Maps/Custom_FerngillRepublicFrontier",
      "asset_type": "map",
      "expr": "asset.properties.CanBuildHere == 'T'",
      "message": "Instant bridge config should expose Frontier build permission on the runtime frontier map"
    }
  ]
}
```

- [ ] **Step 6: Update SVE FROBBY docs**

In `docs/FROBBY.md`, add a Slice 17 paragraph after Slice 16:

```markdown
Scenarios `tests/sdv/24-sve-frontier-farm-profile.test.json` and
`tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json` cover Frontier Farm
profile testing. They use Frobby's neutral `save_overrides.farm_type` fixture
staging so the shared `m0spike_436515781` base save is loaded as the additional
farm id `FrontierFarm` without changing the source fixture. The instant-unlock
profile overlays Frontier Farm's Content Patcher `config.json` in the isolated
test Mods cache and proves bridge/desert shortcut map changes through runtime
asset properties.
```

- [ ] **Step 7: Dry-run SVE config**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --no-build --filter definitely-not-a-real-scenario tests/sdv
```

Expected: command exits `0` with `no scenarios matched` after loading all scenario JSON.

- [ ] **Step 8: Commit SVE scenario/config work**

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add sdv-test.config.json tests/config/frontier-farm/instant-unlocks.json tests/sdv/24-sve-frontier-farm-profile.test.json tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add Frontier Farm Frobby profile coverage"
```

## Task 6: Verify Headless SVE And Finish Frobby TODO

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run Frobby focused tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests|FullyQualifiedName~FixtureStagerTests|FullyQualifiedName~RunCommandTests" -v minimal
```

Expected: all focused tests pass.

- [ ] **Step 2: Run SVE Slice 17 scenarios headlessly**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-17-frontier-farm tests/sdv/24-sve-frontier-farm-profile.test.json tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json
```

Expected: both scenarios pass and reports include frozen final screenshots.

- [ ] **Step 3: Run SVE smoke subset**

Run:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework scripts/sdv-test --headless --no-build --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-17-smoke tests/sdv/01-sve-core-loads.test.json tests/sdv/20-sve-grandpas-farm-profile.test.json tests/sdv/24-sve-frontier-farm-profile.test.json tests/sdv/25-sve-frontier-farm-instant-unlocks.test.json
```

Expected: all four scenarios pass. This specifically proves the standard shared fixture and the overridden derived fixture can coexist in one suite.

- [ ] **Step 4: Investigate live runtime mismatch if needed**

If either live SVE run fails after JSON/schema validation passes, inspect the generated report summary and captured screenshots under the requested `/tmp/stardew-valley-expanded-frobby-results-0.1.0/...` directory. Only change assertions when the observed runtime value can be traced back to current SVE content files:

```bash
rg "FrontierFarm|Custom_FrontierFarm_UndergroundTunnel|Custom_FerngillRepublicFrontier|CanBuildHere|Warp" "/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-17-frontier-farm"
```

Re-run Slice 17 and the smoke subset after any assertion correction.

- [ ] **Step 5: Mark Slice 17 done in Frobby TODO**

Update the Slice 17 entry in `SVE_FROBBY_CAPABILITY_TODO.md`:

```markdown
- [x] Done: Slice 17, alternate farm fixtures and Frontier Farm shortcut coverage.
  - SVE pressure: Frontier Farm profile coverage requires the active save to resolve
    as an additional farm type before Content Patcher `FarmType: FrontierFarm`
    conditions and instant shortcut config patches can be proven.
  - Frobby goal: stage neutral save overrides for modded farm types without mutating
    source fixtures or assuming SVE ids.
  - Design spec: `docs/superpowers/specs/2026-05-15-sve-slice-17-frontier-farm-fixtures-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-15-sve-slice-17-frontier-farm-fixtures.md`.
  - Done: scenario `save_overrides.farm_type` stages derived fixture copies, source fixtures stay unchanged, and SVE scenarios 24-25 prove Frontier Farm profile loading plus instant bridge/desert shortcut runtime map changes.
```

- [ ] **Step 6: Commit Frobby TODO completion**

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: mark SVE Slice 17 complete"
```

- [ ] **Step 7: Final status checks**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: both repos clean on feature branches. Do not merge SVE to `master` unless the user explicitly asks.

## Self-Review

Spec coverage:

- Neutral fixture save override: Tasks 1-3.
- Farm-type XML mutation without source fixture changes: Task 2.
- Same base fixture can run as standard and modded in one suite: Task 3 and Task 6 smoke.
- SVE Frontier Farm profile and instant unlock proof: Task 5 and Task 6.
- Documentation and TODO status: Tasks 4 and 6.

No placeholders remain. Implementation uses Frobby's existing snake_case scenario JSON style (`save_overrides`) while preserving the approved design intent.
