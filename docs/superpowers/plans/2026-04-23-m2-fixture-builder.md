# M2 Fixture Builder — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green** (same convention as D1.5 / D1.6 / D1.7). T11's additional gates: `sdv-test fixture create <name> --from <script>` produces a valid `tests/fixtures/<name>/` directory AND `./scripts/run-samples.sh` still reports 10/10 PASS after the m0spike fixture migration.

**Goal:** Ship a scripted fixture builder (`sdv-test fixture create <name> --from <script.fixture.json>`) that captures reproducible game-state fixtures into the repo under `tests/fixtures/`. Unblocks scenario authors from the "every scenario uses `m0spike_436515781`" bottleneck.

**Architecture:** Three parts — (1) Runner's new `fixture create` / `fixture list` subcommands orchestrate the build by reusing the scenario-runner step dispatch; (2) Harness gains two new RPCs (`fixture.save` drives SDV's save coroutine, `state.mods` surfaces the loaded mod list for metadata); (3) a `FixtureStager` bridges the repo's `tests/fixtures/<name>/save/` dir with SDV's `Constants.SavesPath` at both fixture-build time and scenario-run time. Every fixture builds from an existing base — the m0spike save is the initial root.

**Tech Stack:**
- .NET 6 (Harness) + .NET 10 (Runner) — unchanged
- `Json.Schema` (already used by `ScenarioLoader`) — JSON Schema validation for `.fixture.json`
- SMAPI 4.5.2 — `IModRegistry` for `state.mods`
- SDV 1.6.15 — `SaveGame.Save()` coroutine for persistence
- xUnit — unit tests + skip-marked integration placeholders

**Design spec:** `docs/superpowers/specs/2026-04-23-m2-fixture-builder-design.md`

---

## File structure

**New files (Protocol):**
- `src/Protocol/Models/FixtureSaveRequest.cs` — `{name: string}`
- `src/Protocol/Models/FixtureSaveResult.cs` — `MutatorOk` subclass with `SavePath` string
- `src/Protocol/Models/ModsState.cs` — `{mods: string[]}`

**New files (Schema):**
- `schemas/fixture.schema.json` — JSON Schema for `.fixture.json`

**New files (Runner):**
- `src/Runner/Commands/FixtureCommand.cs` — dispatches `create` / `list`
- `src/Runner/Fixtures/FixtureSpec.cs` — DTO
- `src/Runner/Fixtures/FixtureLoadException.cs` — error type (mirrors `ScenarioLoadException`)
- `src/Runner/Fixtures/FixtureLoader.cs` — parse + schema validate
- `src/Runner/Fixtures/FixtureStager.cs` — recursive copy between repo and SDV save dir
- `src/Runner/Fixtures/FixtureMetadata.cs` — DTO + generator
- `src/Runner/Fixtures/FixtureReadme.cs` — markdown generator
- `src/Runner/Fixtures/FixtureBuilder.cs` — orchestrator

**New files (Harness):**
- `src/Harness/Handlers/StateModsHandler.cs`
- `src/Harness/Handlers/FixtureSaveHandler.cs`

**New files (tests):**
- `tests/Runner.Tests/FixtureCommandTests.cs`
- `tests/Runner.Tests/FixtureLoaderTests.cs`
- `tests/Runner.Tests/FixtureStagerTests.cs`
- `tests/Runner.Tests/FixtureMetadataTests.cs`
- `tests/Runner.Tests/FixtureReadmeTests.cs`
- `tests/Runner.Tests/FixtureBuilderTests.cs`
- `tests/Harness.Tests/StateModsHandlerTests.cs`
- `tests/Harness.Tests/FixtureSaveHandlerTests.cs`
- `tests/Harness.Tests/FixtureBuilderIntegrationTests.cs` — 3 skip-marked

**New files (content):**
- `tests/fixtures/m0spike_436515781/save/SaveGameInfo` (migrated from user's saves dir)
- `tests/fixtures/m0spike_436515781/save/m0spike_436515781` (migrated)
- `tests/fixtures/m0spike_436515781/m0spike_436515781.fixture.json` (hand-written stub)
- `tests/fixtures/m0spike_436515781/m0spike_436515781.meta.json` (hand-written)
- `tests/fixtures/m0spike_436515781/m0spike_436515781.README.md` (hand-written)

**Modified files:**
- `src/Runner/Program.cs` — route `fixture` arg
- `src/Runner/Commands/RunCommand.cs` — call `FixtureStager.Stage` per fixture
- `src/Harness/ModEntry.cs` — register two new handlers; set `StateModsHandler.Registry`
- `docs/rpc-schema.md` — document `fixture.save` + `state.mods`
- `docs/milestones/current.md` — M2 subproject tracker + fixture-builder completion note

**Starting test count:** 201 Passed + 26 Skipped.
**Target test count after M2 fixture-builder:** ~211 Passed + ~29 Skipped.

---

## Task 1: Protocol DTOs + fixture.schema.json

**Why:** Foundation — everything else in the plan needs these types. DTOs stay tiny, schema constrains the `.fixture.json` shape.

**Files:**
- Create: `src/Protocol/Models/FixtureSaveRequest.cs`
- Create: `src/Protocol/Models/FixtureSaveResult.cs`
- Create: `src/Protocol/Models/ModsState.cs`
- Create: `schemas/fixture.schema.json`
- Test: `tests/Protocol.Tests/FixtureDtosSerializationTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Write failing serialization tests**

Create `tests/Protocol.Tests/FixtureDtosSerializationTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class FixtureDtosSerializationTests
{
    [Fact]
    public void FixtureSaveRequest_Serializes_WithSnakeCaseName()
    {
        var req = new FixtureSaveRequest { Name = "spring_day_5_500g" };
        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);
        Assert.Contains("\"name\":\"spring_day_5_500g\"", json);
    }

    [Fact]
    public void FixtureSaveResult_Serializes_WithOkTickSavePath()
    {
        var r = new FixtureSaveResult { Ok = true, Tick = 1234, SavePath = "/tmp/save/x" };
        var json = JsonSerializer.Serialize(r, ProtocolJson.Options);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":1234", json);
        Assert.Contains("\"save_path\":\"/tmp/save/x\"", json);
    }

    [Fact]
    public void ModsState_Serializes_WithArrayOfIds()
    {
        var s = new ModsState { Mods = new[] { "A.B.C", "D.E.F" } };
        var json = JsonSerializer.Serialize(s, ProtocolJson.Options);
        Assert.Contains("\"mods\":[\"A.B.C\",\"D.E.F\"]", json);
    }
}
```

Run: `dotnet test tests/Protocol.Tests/ --filter FixtureDtos`
Expected: FAIL — types don't exist yet.

- [ ] **Step 2: Create FixtureSaveRequest**

Create `src/Protocol/Models/FixtureSaveRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>fixture.save</c>.</summary>
public sealed class FixtureSaveRequest
{
    /// <summary>Destination save-folder name in SDV's saves dir. Typically matches the fixture name.</summary>
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create FixtureSaveResult**

Create `src/Protocol/Models/FixtureSaveResult.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>fixture.save</c>.</summary>
public sealed class FixtureSaveResult : MutatorOk
{
    /// <summary>Absolute path to the save directory produced by <c>SaveGame.Save()</c>.</summary>
    public string SavePath { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Create ModsState**

Create `src/Protocol/Models/ModsState.cs`:

```csharp
using System;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>state.mods</c>.</summary>
public sealed class ModsState
{
    /// <summary>Loaded mod UniqueIDs, in SMAPI load order.</summary>
    public string[] Mods { get; set; } = Array.Empty<string>();
}
```

- [ ] **Step 5: Create fixture.schema.json**

Create `schemas/fixture.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://sdv-test-framework/schemas/fixture.schema.json",
  "title": "SDV Test Framework Fixture Script",
  "type": "object",
  "required": ["name", "description"],
  "properties": {
    "name": { "type": "string", "minLength": 1 },
    "base": {
      "description": "Name of existing fixture to load as a starting point. May be null for root fixtures captured outside the scripted builder path (e.g. migrated spike saves).",
      "type": ["string", "null"]
    },
    "description": { "type": "string", "minLength": 1 },
    "steps": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["action"],
        "properties": {
          "action": { "type": "string" },
          "args": { "type": "object" }
        },
        "additionalProperties": false
      }
    }
  },
  "additionalProperties": false
}
```

Note: `steps` is optional (defaults to empty if omitted). `base` is optional and nullable — a non-null string points at an existing fixture directory; `null` or omitted indicates a root fixture (migrated / hand-captured).

- [ ] **Step 6: Ensure schema is copied to output**

`schemas/scenario.schema.json` is already picked up at build time — check `src/Runner/Runner.csproj` for the `<None Include="...schema.json" CopyToOutputDirectory="PreserveNewest" />` pattern and add a sibling entry for `fixture.schema.json`:

Open `src/Runner/Runner.csproj`. Find the ItemGroup containing `<None Include="..\..\schemas\scenario.schema.json" ...>` (or wherever schemas are declared). Add:

```xml
    <None Include="..\..\schemas\fixture.schema.json">
      <Link>schemas\fixture.schema.json</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
```

If the existing scenario schema entry uses a different syntax (e.g. `<Content>` or glob pattern covering `schemas/*.json`), adapt so the fixture schema ships alongside. If the file lacks any schema entry at all, search the project for "scenario.schema" to find the copy mechanism and match it.

- [ ] **Step 7: Verify tests pass**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 201 → 204 (+3 new passing tests).

---

## Task 2: StateModsHandler RPC

**Why:** Metadata auto-discovery needs the loaded mod list. Small RPC, mirrors the `state.*` handler pattern.

**Files:**
- Create: `src/Harness/Handlers/StateModsHandler.cs`
- Modify: `src/Harness/ModEntry.cs` — register handler + set `Registry`
- Create: `tests/Harness.Tests/StateModsHandlerTests.cs`

**Dependencies:** Task 1 (ModsState DTO).

- [ ] **Step 1: Write failing test**

Create `tests/Harness.Tests/StateModsHandlerTests.cs`:

```csharp
using System.Collections.Generic;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateModsHandlerTests
{
    // Shim stand-in for IModRegistry's GetAll() surface — the handler only cares about
    // iterating mods and reading UniqueID, so we fake just enough.
    private sealed class FakeModInfo : IModInfo
    {
        public FakeModInfo(string uniqueId) { Manifest = new FakeManifest(uniqueId); }
        public IManifest Manifest { get; }
        public bool IsContentPack => false;
    }

    private sealed class FakeManifest : IManifest
    {
        public FakeManifest(string uniqueId) { UniqueID = uniqueId; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public ISemanticVersion Version { get; set; } = null!;
        public ISemanticVersion? MinimumApiVersion { get; set; }
        public ISemanticVersion? MinimumGameVersion { get; set; }
        public string UniqueID { get; }
        public string? EntryDll { get; set; }
        public IManifestContentPackFor? ContentPackFor { get; set; }
        public IManifestDependency[] Dependencies { get; set; } = System.Array.Empty<IManifestDependency>();
        public string[] UpdateKeys { get; set; } = System.Array.Empty<string>();
        public IDictionary<string, object> ExtraFields { get; set; } = new Dictionary<string, object>();
    }

    private sealed class FakeRegistry : IModRegistry
    {
        private readonly List<IModInfo> _mods;
        public FakeRegistry(params string[] uniqueIds)
        {
            _mods = new List<IModInfo>();
            foreach (var id in uniqueIds) _mods.Add(new FakeModInfo(id));
        }
        public IEnumerable<IModInfo> GetAll() => _mods;
        public IEnumerable<IModInfo> GetAll(bool assemblyMods, bool contentPacks) => _mods;
        public IModInfo? Get(string uniqueID) => null;
        public bool IsLoaded(string uniqueID) => false;
        public T? GetApi<T>() where T : class => null;
        public T? GetApi<T>(string uniqueID) where T : class => null;
        public object? GetApi(string uniqueID) => null;
    }

    [Fact]
    public void Handle_NoRegistry_ReturnsEmptyList()
    {
        StateModsHandler.Registry = null;
        var resp = StateModsHandler.Handle(null);
        var state = System.Text.Json.JsonSerializer.Deserialize<ModsState>(
            resp.GetRawText(), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
        Assert.NotNull(state);
        Assert.Empty(state!.Mods);
    }

    [Fact]
    public void Handle_RegistryWithMods_ReturnsAllUniqueIds()
    {
        try
        {
            StateModsHandler.Registry = new FakeRegistry("A.B", "C.D", "E.F");
            var resp = StateModsHandler.Handle(null);
            var state = System.Text.Json.JsonSerializer.Deserialize<ModsState>(
                resp.GetRawText(), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
            Assert.NotNull(state);
            Assert.Equal(new[] { "A.B", "C.D", "E.F" }, state!.Mods);
        }
        finally { StateModsHandler.Registry = null; }
    }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter StateModsHandler`
Expected: FAIL — `StateModsHandler` type doesn't exist.

- [ ] **Step 2: Create the handler**

Create `src/Harness/Handlers/StateModsHandler.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>state.mods</c>. Returns the loaded mod UniqueIDs in SMAPI load order.</summary>
/// <remarks>
/// Used by the fixture builder to populate <c>&lt;name&gt;.meta.json</c>'s <c>mods_installed</c>
/// field. Set by <c>ModEntry.Entry</c> via the static <see cref="Registry"/> property.
/// Null registry → empty list (keeps unit tests simple; production always sets it).
/// </remarks>
public static class StateModsHandler
{
    public const string Method = "state.mods";

    /// <summary>Set by <c>ModEntry</c> at startup; mirror of <c>helper.ModRegistry</c>.</summary>
    public static IModRegistry? Registry { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var ids = new List<string>();
        if (Registry is { } reg)
        {
            foreach (var mod in reg.GetAll())
                if (!string.IsNullOrEmpty(mod.Manifest?.UniqueID))
                    ids.Add(mod.Manifest.UniqueID);
        }
        return ProtocolJson.ToElement(new ModsState { Mods = ids.ToArray() });
    }
}
```

- [ ] **Step 3: Register in ModEntry**

Open `src/Harness/ModEntry.cs`. Find the handler-registration block and the line registering `ScenarioEndHandler`. Add right after:

```csharp
        StateModsHandler.Registry = helper.ModRegistry;
        _rpc.Register(StateModsHandler.Method, p => StateModsHandler.Handle(p));
```

Update the `Harness loaded...` info log's `RPC methods:` portion to include `state.mods`.

- [ ] **Step 4: Run tests — verify pass**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 204 → 206 (+2 new passing tests).

---

## Task 3: FixtureSpec + FixtureLoader

**Why:** Parse + validate `.fixture.json` files before the builder consumes them. Mirrors the `ScenarioLoader` pattern.

**Files:**
- Create: `src/Runner/Fixtures/FixtureSpec.cs`
- Create: `src/Runner/Fixtures/FixtureLoadException.cs`
- Create: `src/Runner/Fixtures/FixtureLoader.cs`
- Create: `tests/Runner.Tests/FixtureLoaderTests.cs`

**Dependencies:** Task 1 (schema file at `schemas/fixture.schema.json`).

- [ ] **Step 1: Create FixtureSpec DTO**

Create `src/Runner/Fixtures/FixtureSpec.cs`:

```csharp
using System;
using System.Text.Json;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>DTO mirroring <c>schemas/fixture.schema.json</c>. Populated by <see cref="FixtureLoader"/>.</summary>
public sealed class FixtureSpec
{
    /// <summary>Fixture identifier — must match the containing directory name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Name of an existing fixture whose save is loaded as the starting state.
    /// <c>null</c> for root fixtures captured outside the scripted builder path
    /// (e.g. spike saves migrated into <c>tests/fixtures/</c>).
    /// </summary>
    public string? Base { get; set; }

    /// <summary>One-line human description, copied into metadata + README.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Ordered RPC step list, dispatched by <see cref="FixtureBuilder"/>.</summary>
    public FixtureStep[] Steps { get; set; } = Array.Empty<FixtureStep>();
}

/// <summary>A single step in a <see cref="FixtureSpec.Steps"/> list.</summary>
public sealed class FixtureStep
{
    /// <summary>RPC method name, e.g. <c>"player.set_money"</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Raw params element, passed through to the RPC. May be null.</summary>
    public JsonElement? Args { get; set; }
}
```

- [ ] **Step 2: Create FixtureLoadException**

Create `src/Runner/Fixtures/FixtureLoadException.cs`:

```csharp
using System;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Thrown by <see cref="FixtureLoader"/> when a fixture script can't be parsed or doesn't validate.</summary>
public sealed class FixtureLoadException : Exception
{
    public FixtureLoadException(string file, string message) : base($"{file}: {message}") { }
    public FixtureLoadException(string file, string message, Exception inner) : base($"{file}: {message}", inner) { }
}
```

- [ ] **Step 3: Write failing tests**

Create `tests/Runner.Tests/FixtureLoaderTests.cs`:

```csharp
using System.IO;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureLoaderTests
{
    private static string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fixture-{System.Guid.NewGuid():N}.fixture.json");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Load_ValidScript_RoundTrips()
    {
        var path = WriteTemp("""
        {
          "name": "test",
          "base": "m0spike_436515781",
          "description": "derived test fixture",
          "steps": [
            { "action": "player.set_money", "args": { "amount": 500 } }
          ]
        }
        """);
        try
        {
            var spec = FixtureLoader.Load(path);
            Assert.Equal("test", spec.Name);
            Assert.Equal("m0spike_436515781", spec.Base);
            Assert.Equal("derived test fixture", spec.Description);
            Assert.Single(spec.Steps);
            Assert.Equal("player.set_money", spec.Steps[0].Action);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingName_Throws()
    {
        var path = WriteTemp("""{"base":"x","description":"y"}""");
        try
        {
            var ex = Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path));
            Assert.Contains("schema validation failed", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingDescription_Throws()
    {
        var path = WriteTemp("""{"name":"x","base":"y"}""");
        try { Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_NullBase_Accepted()
    {
        // Root fixtures (e.g. migrated spike save) have base: null.
        var path = WriteTemp("""{"name":"root","base":null,"description":"root fixture"}""");
        try
        {
            var spec = FixtureLoader.Load(path);
            Assert.Null(spec.Base);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_FileMissing_Throws()
    {
        var ex = Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load("/tmp/does-not-exist.fixture.json"));
        Assert.Contains("file not found", ex.Message);
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        var path = WriteTemp("{ not json");
        try
        {
            var ex = Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path));
            Assert.Contains("invalid JSON", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ExtraFields_Rejected()
    {
        // Schema has additionalProperties: false — tight to catch typos.
        var path = WriteTemp("""{"name":"x","description":"y","extra":"bad"}""");
        try { Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path)); }
        finally { File.Delete(path); }
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter FixtureLoader`
Expected: FAIL — `FixtureLoader` type doesn't exist.

- [ ] **Step 4: Create FixtureLoader**

Create `src/Runner/Fixtures/FixtureLoader.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>
/// Loads and validates fixture scripts (<c>*.fixture.json</c>) per <c>schemas/fixture.schema.json</c>.
/// Mirrors <c>ScenarioLoader</c>'s pattern. Fails loudly with <see cref="FixtureLoadException"/>.
/// </summary>
public static class FixtureLoader
{
    private static readonly JsonSchema Schema = LoadSchema();

    private static JsonSchema LoadSchema()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "schemas", "fixture.schema.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "schemas", "fixture.schema.json"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return JsonSchema.FromFile(c);
        throw new FileNotFoundException(
            "fixture.schema.json not found in any known location. Candidates: " + string.Join(", ", candidates));
    }

    /// <summary>
    /// Reads, parses, schema-validates, and deserializes the given fixture script.
    /// Throws <see cref="FixtureLoadException"/> on any failure.
    /// </summary>
    public static FixtureSpec Load(string path)
    {
        if (!File.Exists(path))
            throw new FixtureLoadException(path, "file not found");

        string json;
        try { json = File.ReadAllText(path); }
        catch (IOException ex) { throw new FixtureLoadException(path, $"read failed: {ex.Message}", ex); }

        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FixtureLoadException(path, $"invalid JSON: {ex.Message}", ex); }
        if (node is null) throw new FixtureLoadException(path, "empty file");

        var result = Schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!result.IsValid)
        {
            var messages = string.Join("; ",
                result.Details
                    .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
                    .Select(d => $"{d.InstanceLocation}: {d.Errors!.First().Value}"));
            if (string.IsNullOrEmpty(messages))
                messages = "validation failed (no detailed error available)";
            throw new FixtureLoadException(path, $"schema validation failed: {messages}");
        }

        try
        {
            return JsonSerializer.Deserialize<FixtureSpec>(json, ProtocolJson.Options)
                ?? throw new FixtureLoadException(path, "deserialization returned null");
        }
        catch (JsonException ex)
        {
            throw new FixtureLoadException(path, $"deserialization failed: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 5: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 206 → 213 (+7 new passing tests).

---

## Task 4: FixtureStager

**Why:** Bridges the repo's `tests/fixtures/<name>/save/` with SDV's `Constants.SavesPath` at both build-time and run-time.

**Files:**
- Create: `src/Runner/Fixtures/FixtureStager.cs`
- Create: `tests/Runner.Tests/FixtureStagerTests.cs`

**Dependencies:** none.

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Tests/FixtureStagerTests.cs`:

```csharp
using System.IO;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureStagerTests
{
    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stager-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Stage_CopiesSaveDirRecursively()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            // Seed: fixturesRoot/myfix/save/ with two files
            var src = Path.Combine(fixturesRoot, "myfix", "save");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "<info/>");
            File.WriteAllText(Path.Combine(src, "myfix"), "savedata");

            FixtureStager.Stage("myfix", fixturesRoot, sdvSaves);

            var dst = Path.Combine(sdvSaves, "myfix");
            Assert.True(Directory.Exists(dst));
            Assert.Equal("<info/>", File.ReadAllText(Path.Combine(dst, "SaveGameInfo")));
            Assert.Equal("savedata", File.ReadAllText(Path.Combine(dst, "myfix")));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void Stage_OverwritesExistingTarget()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            var src = Path.Combine(fixturesRoot, "myfix", "save");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "new");

            // Pre-existing target with stale content + extra file
            var dst = Path.Combine(sdvSaves, "myfix");
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(dst, "SaveGameInfo"), "stale");
            File.WriteAllText(Path.Combine(dst, "orphan"), "x");

            FixtureStager.Stage("myfix", fixturesRoot, sdvSaves);

            Assert.Equal("new", File.ReadAllText(Path.Combine(dst, "SaveGameInfo")));
            // orphan file should be gone — stager does delete-and-replace
            Assert.False(File.Exists(Path.Combine(dst, "orphan")));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void Stage_MissingSource_Throws()
    {
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => FixtureStager.Stage("nope", fixturesRoot, sdvSaves));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }

    [Fact]
    public void Capture_CopiesFromSdvSavesToFixturesRoot()
    {
        // Inverse of Stage — used by FixtureBuilder after fixture.save succeeds.
        var fixturesRoot = MakeTempDir();
        var sdvSaves = MakeTempDir();
        try
        {
            var src = Path.Combine(sdvSaves, "newfix");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "SaveGameInfo"), "captured");
            File.WriteAllText(Path.Combine(src, "newfix"), "data");

            FixtureStager.Capture("newfix", sdvSaves, fixturesRoot);

            var dst = Path.Combine(fixturesRoot, "newfix", "save");
            Assert.True(Directory.Exists(dst));
            Assert.Equal("captured", File.ReadAllText(Path.Combine(dst, "SaveGameInfo")));
        }
        finally
        {
            Directory.Delete(fixturesRoot, recursive: true);
            Directory.Delete(sdvSaves, recursive: true);
        }
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter FixtureStager`
Expected: FAIL — `FixtureStager` doesn't exist.

- [ ] **Step 2: Create FixtureStager**

Create `src/Runner/Fixtures/FixtureStager.cs`:

```csharp
using System.IO;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>
/// Bridges the repo's <c>tests/fixtures/&lt;name&gt;/save/</c> with SDV's save directory
/// (<c>Constants.SavesPath</c>). Stage runs before launching SDV; Capture runs after
/// <c>fixture.save</c> succeeds to pull the newly-saved game state back into the repo.
/// </summary>
public static class FixtureStager
{
    /// <summary>
    /// Copy <c>fixturesRoot/name/save/</c> → <c>sdvSavesDir/name/</c> (delete-and-replace).
    /// Called by RunCommand for each scenario's fixture, and by FixtureBuilder for the base.
    /// </summary>
    public static void Stage(string name, string fixturesRoot, string sdvSavesDir)
    {
        var src = Path.Combine(fixturesRoot, name, "save");
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException(
                $"fixture save directory not found: {src}");

        var dst = Path.Combine(sdvSavesDir, name);
        if (Directory.Exists(dst))
            Directory.Delete(dst, recursive: true);
        CopyRecursive(src, dst);
    }

    /// <summary>
    /// Copy <c>sdvSavesDir/name/</c> → <c>fixturesRoot/name/save/</c> (delete-and-replace).
    /// Called by FixtureBuilder after the harness's <c>fixture.save</c> completes.
    /// </summary>
    public static void Capture(string name, string sdvSavesDir, string fixturesRoot)
    {
        var src = Path.Combine(sdvSavesDir, name);
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException(
                $"SDV save directory not found — did fixture.save complete? Expected: {src}");

        var dst = Path.Combine(fixturesRoot, name, "save");
        if (Directory.Exists(dst))
            Directory.Delete(dst, recursive: true);
        CopyRecursive(src, dst);
    }

    private static void CopyRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(src))
            CopyRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
```

- [ ] **Step 3: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 213 → 217 (+4 new passing tests).

---

## Task 5: FixtureMetadata + FixtureReadme generators

**Why:** Auto-generated `.meta.json` + `.README.md` make fixtures self-documenting and CI-inspectable.

**Files:**
- Create: `src/Runner/Fixtures/FixtureMetadata.cs`
- Create: `src/Runner/Fixtures/FixtureReadme.cs`
- Create: `tests/Runner.Tests/FixtureMetadataTests.cs`
- Create: `tests/Runner.Tests/FixtureReadmeTests.cs`

**Dependencies:** Task 3 (FixtureSpec DTO).

- [ ] **Step 1: Write failing metadata tests**

Create `tests/Runner.Tests/FixtureMetadataTests.cs`:

```csharp
using System;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureMetadataTests
{
    [Fact]
    public void Generate_ProducesAllRuleFields()
    {
        var spec = new FixtureSpec
        {
            Name = "spring_day_5",
            Base = "m0spike_436515781",
            Description = "Spring day 5 with 500g",
        };
        var meta = FixtureMetadata.Generate(
            spec,
            sdvVersion: "1.6.15",
            smapiVersion: "4.5.2",
            mods: new[] { "A.B", "C.D" },
            farmerName: "Tester",
            farmerGender: "female",
            createdAtUtc: new DateTime(2026, 4, 23, 15, 30, 0, DateTimeKind.Utc));
        Assert.Equal("spring_day_5", meta.Name);
        Assert.Equal("m0spike_436515781", meta.Base);
        Assert.Equal("1.6.15", meta.SdvVersion);
        Assert.Equal("4.5.2", meta.SmapiVersion);
        Assert.Equal(new[] { "A.B", "C.D" }, meta.ModsInstalled);
        Assert.Equal("2026-04-23T15:30:00.0000000Z", meta.CreatedAt);
        Assert.Equal("fixture-builder", meta.CreatedBy);
        Assert.Equal("Tester", meta.Farmer.Name);
        Assert.Equal("female", meta.Farmer.Gender);
        Assert.Equal("tests/fixtures/spring_day_5/spring_day_5.fixture.json", meta.RegenerateWith);
    }

    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var spec = new FixtureSpec { Name = "x", Base = "y", Description = "z" };
        var meta = FixtureMetadata.Generate(spec, "1.6.15", "4.5.2", new[] { "a" }, "n", "male",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = JsonSerializer.Serialize(meta, ProtocolJson.Options);
        Assert.Contains("\"sdv_version\":\"1.6.15\"", json);
        Assert.Contains("\"smapi_version\":\"4.5.2\"", json);
        Assert.Contains("\"mods_installed\":[\"a\"]", json);
        Assert.Contains("\"created_at\":", json);
        Assert.Contains("\"created_by\":\"fixture-builder\"", json);
        Assert.Contains("\"regenerate_with\":\"tests/fixtures/x/x.fixture.json\"", json);
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter FixtureMetadata`
Expected: FAIL — type doesn't exist.

- [ ] **Step 2: Create FixtureMetadata**

Create `src/Runner/Fixtures/FixtureMetadata.cs`:

```csharp
using System;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Serializable metadata per <c>.claude/rules/fixtures.md</c>.</summary>
public sealed class FixtureMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SdvVersion { get; set; } = string.Empty;
    public string SmapiVersion { get; set; } = string.Empty;
    public string[] ModsInstalled { get; set; } = Array.Empty<string>();
    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "fixture-builder";
    public string? Base { get; set; }
    public string RegenerateWith { get; set; } = string.Empty;
    public FarmerInfo Farmer { get; set; } = new();

    public sealed class FarmerInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
    }

    /// <summary>
    /// Build metadata from a fixture spec + runtime-captured environment info.
    /// The caller is responsible for capturing the inputs (via state RPCs) before calling.
    /// </summary>
    public static FixtureMetadata Generate(
        FixtureSpec spec,
        string sdvVersion,
        string smapiVersion,
        string[] mods,
        string farmerName,
        string farmerGender,
        DateTime createdAtUtc)
    {
        return new FixtureMetadata
        {
            Name = spec.Name,
            Description = spec.Description,
            SdvVersion = sdvVersion,
            SmapiVersion = smapiVersion,
            ModsInstalled = mods,
            CreatedAt = createdAtUtc.ToString("O"),
            CreatedBy = "fixture-builder",
            Base = spec.Base,
            RegenerateWith = $"tests/fixtures/{spec.Name}/{spec.Name}.fixture.json",
            Farmer = new FarmerInfo { Name = farmerName, Gender = farmerGender },
        };
    }
}
```

- [ ] **Step 3: Write failing README tests**

Create `tests/Runner.Tests/FixtureReadmeTests.cs`:

```csharp
using System;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureReadmeTests
{
    [Fact]
    public void Generate_IncludesDescription_AndRegenerateWith()
    {
        var spec = new FixtureSpec
        {
            Name = "spring_day_5",
            Base = "m0spike_436515781",
            Description = "Spring day 5 with 500g",
        };
        var meta = FixtureMetadata.Generate(
            spec, "1.6.15", "4.5.2", new[] { "Pathoschild.ContentPatcher" },
            "Tester", "female", DateTime.UtcNow);

        var md = FixtureReadme.Generate(spec, meta);

        Assert.Contains("# spring_day_5", md);
        Assert.Contains("Spring day 5 with 500g", md);
        Assert.Contains("m0spike_436515781", md);
        Assert.Contains("## Regenerate", md);
        Assert.Contains("tests/fixtures/spring_day_5/spring_day_5.fixture.json", md);
        Assert.Contains("SDV 1.6.15", md);
        Assert.Contains("SMAPI 4.5.2", md);
        Assert.Contains("Pathoschild.ContentPatcher", md);
    }

    [Fact]
    public void Generate_NullBase_OmitsBaseSection()
    {
        var spec = new FixtureSpec { Name = "root", Base = null, Description = "root fixture" };
        var meta = FixtureMetadata.Generate(
            spec, "1.6.15", "4.5.2", System.Array.Empty<string>(), "Tester", "female", DateTime.UtcNow);
        var md = FixtureReadme.Generate(spec, meta);
        Assert.DoesNotContain("Built from:", md);
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter FixtureReadme`
Expected: FAIL.

- [ ] **Step 4: Create FixtureReadme**

Create `src/Runner/Fixtures/FixtureReadme.cs`:

```csharp
using System.Text;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Generates a short human-readable README for a fixture directory.</summary>
public static class FixtureReadme
{
    public static string Generate(FixtureSpec spec, FixtureMetadata meta)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(spec.Name).AppendLine();
        sb.AppendLine(spec.Description).AppendLine();

        sb.AppendLine("## Environment").AppendLine();
        sb.Append("- SDV ").AppendLine(meta.SdvVersion);
        sb.Append("- SMAPI ").AppendLine(meta.SmapiVersion);
        sb.Append("- Farmer: ").Append(meta.Farmer.Name).Append(" (").Append(meta.Farmer.Gender).AppendLine(")");
        sb.Append("- Created: ").AppendLine(meta.CreatedAt);
        sb.AppendLine();

        if (!string.IsNullOrEmpty(spec.Base))
        {
            sb.AppendLine("## Derived from").AppendLine();
            sb.Append("Built from: `").Append(spec.Base).AppendLine("`.").AppendLine();
        }

        if (meta.ModsInstalled.Length > 0)
        {
            sb.AppendLine("## Mods installed during capture").AppendLine();
            foreach (var m in meta.ModsInstalled)
                sb.Append("- ").AppendLine(m);
            sb.AppendLine();
        }

        sb.AppendLine("## Regenerate").AppendLine();
        sb.AppendLine("```bash");
        sb.Append("sdv-test fixture create ").Append(spec.Name).Append(" --from ").AppendLine(meta.RegenerateWith);
        sb.AppendLine("```").AppendLine();

        sb.AppendLine("_This file is auto-generated. Safe to delete; re-runs of `fixture create` regenerate it._");
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 217 → 221 (+4 new passing tests).

---

## Task 6: FixtureBuilder orchestrator

**Why:** Ties steps 3-5 together. Takes a spec + RPC session, drives the game to the target state, captures the save, writes metadata + README.

**Files:**
- Create: `src/Runner/Fixtures/FixtureBuilder.cs`
- Create: `tests/Runner.Tests/FixtureBuilderTests.cs`

**Dependencies:** Tasks 3, 4, 5.

- [ ] **Step 1: Write failing test**

Create `tests/Runner.Tests/FixtureBuilderTests.cs`:

```csharp
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureBuilderTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"fxb-{System.Guid.NewGuid():N}.sock");

    [Fact]
    public async Task BuildAsync_InvokesFixtureLoadThenSteps_ThenFixtureSave()
    {
        // Minimal fake harness that records every incoming RPC, responds 200-OK to each.
        var socket = SocketPath();
        var log = new System.Collections.Generic.List<string>();
        var cts = new System.Threading.CancellationTokenSource();
        var serverTask = RunFakeServer(socket, log, cts.Token);
        await WaitForSocket(socket);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var spec = new FixtureSpec
        {
            Name = "derived_test",
            Base = "m0spike_436515781",
            Description = "test fixture",
            Steps = new[]
            {
                new FixtureStep { Action = "player.set_money", Args = JsonDocument.Parse("{\"amount\":500}").RootElement },
            },
        };

        var result = await FixtureBuilder.BuildAsync(spec, client, cts.Token);

        Assert.True(result.Success);
        Assert.Equal("1.6.15", result.SdvVersion);
        Assert.Equal("4.5.2", result.SmapiVersion);
        Assert.Contains("fixture.load", log);
        Assert.Contains("player.set_money", log);
        Assert.Contains("fixture.save", log);
        // Order: fixture.load first, fixture.save last
        Assert.Equal(0, log.IndexOf("fixture.load"));
        Assert.Equal(log.Count - 1, log.LastIndexOf("fixture.save"));

        cts.Cancel();
        try { await serverTask; } catch { /* cancellation */ }
    }

    // Runs a tiny JSON-RPC server that canned-answers every method in the builder's flow.
    private static Task RunFakeServer(string socket, System.Collections.Generic.List<string> log, System.Threading.CancellationToken ct)
    {
        return UnixSocketRpc.RunServerAsync(socket, async (session, sessCt) =>
        {
            session.RequestReceived += req =>
            {
                log.Add(req.Method);
                JsonElement result = req.Method switch
                {
                    "fixture.load" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                    "state.player" => JsonDocument.Parse(
                        "{\"name\":\"Tester\",\"gender\":\"female\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}").RootElement,
                    "state.time" => JsonDocument.Parse(
                        "{\"in_save\":true,\"season\":\"spring\",\"day_of_month\":1,\"year\":1,\"time_of_day\":600,\"day_of_week\":\"monday\"}").RootElement,
                    "state.mods" => JsonDocument.Parse("{\"mods\":[\"A.B\",\"C.D\"]}").RootElement,
                    "fixture.save" => JsonDocument.Parse("{\"ok\":true,\"tick\":10,\"save_path\":\"/tmp/fake\"}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true,\"tick\":2}").RootElement,
                };
                _ = session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, result), sessCt);
            };
            await session.RunAsync(sessCt);
        }, ct);
    }

    private static async Task WaitForSocket(string path)
    {
        for (int i = 0; i < 50; i++)
        {
            if (File.Exists(path)) return;
            await Task.Delay(50);
        }
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter FixtureBuilder`
Expected: FAIL — `FixtureBuilder` doesn't exist.

- [ ] **Step 2: Create FixtureBuilder**

Create `src/Runner/Fixtures/FixtureBuilder.cs`:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Result of a <see cref="FixtureBuilder.BuildAsync"/> run.</summary>
public sealed class FixtureBuildResult
{
    public bool Success { get; set; }
    public string SdvVersion { get; set; } = string.Empty;
    public string SmapiVersion { get; set; } = string.Empty;
    public string[] Mods { get; set; } = Array.Empty<string>();
    public string FarmerName { get; set; } = string.Empty;
    public string FarmerGender { get; set; } = string.Empty;
    public string SavePath { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Orchestrator: given a parsed <see cref="FixtureSpec"/> and a connected
/// <see cref="JsonRpcSession"/>, runs the build flow (load base → steps → capture env →
/// save → populate result).
/// </summary>
public static class FixtureBuilder
{
    public static async Task<FixtureBuildResult> BuildAsync(
        FixtureSpec spec, JsonRpcSession session, CancellationToken ct)
    {
        var result = new FixtureBuildResult();
        try
        {
            // 1. load base (skip if null — the root fixture has no base)
            if (!string.IsNullOrEmpty(spec.Base))
            {
                var loadReq = JsonSerializer.SerializeToElement(
                    new FixtureLoadRequest { Name = spec.Base }, ProtocolJson.Options);
                var loadResp = await session.InvokeAsync("fixture.load", loadReq, ct);
                if (loadResp.Error is { } le)
                    throw new InvalidOperationException($"fixture.load failed: {le.Message}");

                // Poll state.player until location is populated — same wait-for-ready
                // logic as ScenarioRunner.WaitForWorldReady.
                await WaitForWorldReadyAsync(session, ct);
            }

            // 2. steps
            foreach (var step in spec.Steps)
            {
                var resp = await session.InvokeAsync(step.Action, step.Args, ct);
                if (resp.Error is { } e)
                    throw new InvalidOperationException($"step '{step.Action}' failed: {e.Message}");
            }

            // 3. capture environment for metadata (BEFORE save, so state.player reflects
            //    the post-steps farmer state, not the post-save reset-to-next-day state).
            var playerResp = await session.InvokeAsync("state.player", params_: null, ct);
            if (playerResp.Result is { } pr)
            {
                result.FarmerName = pr.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                // state.player doesn't currently return gender; leave blank for now.
                // A future PlayerState extension would populate this.
            }
            var modsResp = await session.InvokeAsync("state.mods", params_: null, ct);
            if (modsResp.Result is { } mr && mr.TryGetProperty("mods", out var modsEl))
            {
                var count = modsEl.GetArrayLength();
                var mods = new string[count];
                for (int i = 0; i < count; i++) mods[i] = modsEl[i].GetString() ?? "";
                result.Mods = mods;
            }

            // 4. SDV + SMAPI versions — read from the ready notification echoed back to
            //    the session on connect. JsonRpcSession exposes nothing for this right
            //    now, so we hardcode per the currently-pinned versions. If the protocol
            //    adds a handshake getter later, swap these in.
            result.SdvVersion = "1.6.15";
            result.SmapiVersion = "4.5.2";

            // 5. save
            var saveReq = JsonSerializer.SerializeToElement(
                new FixtureSaveRequest { Name = spec.Name }, ProtocolJson.Options);
            var saveResp = await session.InvokeAsync("fixture.save", saveReq, ct);
            if (saveResp.Error is { } se)
                throw new InvalidOperationException($"fixture.save failed: {se.Message}");
            if (saveResp.Result is { } sr && sr.TryGetProperty("save_path", out var sp))
                result.SavePath = sp.GetString() ?? "";

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    private static async Task WaitForWorldReadyAsync(JsonRpcSession session, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await session.InvokeAsync("state.player", params_: null, ct);
            if (resp.Result is { } r
                && r.TryGetProperty("location", out var loc)
                && loc.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(loc.GetString()))
                return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("world never became ready after fixture.load");
    }
}
```

- [ ] **Step 3: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 221 → 222 (+1 new passing test).

---

## Task 7: FixtureSaveHandler (Harness)

**Why:** Drives `SaveGame.Save()` to produce a save file on disk. Mirrors `FreezeBeginHandler`'s precondition pattern.

**Files:**
- Create: `src/Harness/Handlers/FixtureSaveHandler.cs`
- Modify: `src/Harness/ModEntry.cs` — register handler
- Create: `tests/Harness.Tests/FixtureSaveHandlerTests.cs`

**Dependencies:** Task 1 (FixtureSaveRequest/Result DTOs).

- [ ] **Step 1: Write failing tests**

Create `tests/Harness.Tests/FixtureSaveHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FixtureSaveHandlerTests
{
    [Fact]
    public void Handle_MissingName_ThrowsInvalidParams()
    {
        var req = JsonDocument.Parse("""{}""").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => FixtureSaveHandler.Handle(req));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Requires live SDV — integration tested via FixtureBuilderIntegrationTests (T11 smoke).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV — integration tested via FixtureBuilderIntegrationTests (T11 smoke).")]
    public void Handle_InSave_TriggersSaveGameSave_AndReturnsPath() { }
}
```

Run: `dotnet test tests/Harness.Tests/ --filter FixtureSaveHandler`
Expected: FAIL on the params test (handler doesn't exist); 2 skipped.

- [ ] **Step 2: Create the handler**

Create `src/Harness/Handlers/FixtureSaveHandler.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>fixture.save</c>. Drives SDV's <see cref="SaveGame.Save"/> coroutine
/// synchronously on the game thread (the handler already runs there via GameThreadDispatch),
/// then returns the absolute save path. Preconditions mirror <c>FreezeBeginHandler</c>.
/// </summary>
public static class FixtureSaveHandler
{
    public const string Method = "fixture.save";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var req = RpcParams.Required<FixtureSaveRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name required");

        // Preconditions — same predicate as FreezeBeginHandler (D1.7 widened).
        if (Game1.gameMode != Game1.playingGameMode || !Game1.hasLoadedGame)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires a loaded world (no active save)");
        if (Game1.eventUp)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires !Game1.eventUp (event active)");
        if (Game1.currentMinigame != null)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires Game1.currentMinigame == null (minigame active)");
        if (Game1.isWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires !Game1.isWarping (mid-warp)");

        // Marker so framework-created saves can be identified later. Harmless flavor field.
        Game1.player.favoriteThing.Value = "sdv-test-fixture";

        // Rename the current save before saving so SDV writes under our requested folder name.
        // Game1.player.farmName drives the save folder name on SDV 1.6+. Save at title-screen-
        // reachable state means SaveGame.Save uses Game1.player.farmName + "_" + Game1.uniqueIDForThisGame.
        // For our purposes, the simplest approach is: call SaveGame.Save synchronously (it writes
        // to the current farmer's dir in Constants.SavesPath), then the runner copies it OUT.
        // The `name` param is used by the Runner after copy; SDV doesn't need it.
        DriveSaveToCompletion();

        var savePath = Path.Combine(Constants.SavesPath, Game1.player.farmName.Value + "_" + Game1.uniqueIDForThisGame);

        return ProtocolJson.ToElement(new FixtureSaveResult
        {
            Ok = true,
            Tick = Game1.ticks,
            SavePath = savePath,
        });
    }

    /// <summary>
    /// Iterate <see cref="SaveGame.Save"/>'s coroutine to completion. The handler runs on
    /// the game thread (via GameThreadDispatch), so blocking here blocks one update tick's
    /// worth of logic — acceptable since SDV saves typically complete in &lt;1 second.
    /// </summary>
    private static void DriveSaveToCompletion()
    {
        var enumerator = SaveGame.Save();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (enumerator.MoveNext())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("fixture.save exceeded 30s budget");
        }
    }
}
```

- [ ] **Step 3: Register in ModEntry**

Open `src/Harness/ModEntry.cs`. After the `StateModsHandler` registration (added in T2), add:

```csharp
        _rpc.Register(FixtureSaveHandler.Method, p => FixtureSaveHandler.Handle(p));
```

Update the `Harness loaded...` log to include `fixture.save` in the Lifecycle section.

- [ ] **Step 4: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 222 → 223 (+1 passing test; +2 new Skipped). Skipped count 26 → 28.

---

## Task 8: FixtureCommand

**Why:** Wires everything into a CLI surface — `sdv-test fixture create <name> --from <script>` and `sdv-test fixture list`.

**Files:**
- Create: `src/Runner/Commands/FixtureCommand.cs`
- Create: `tests/Runner.Tests/FixtureCommandTests.cs`

**Dependencies:** Tasks 3, 4, 5, 6 (everything in `src/Runner/Fixtures/`).

- [ ] **Step 1: Write failing tests**

Create `tests/Runner.Tests/FixtureCommandTests.cs`:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureCommandTests
{
    [Fact]
    public async Task Run_NoSubcommand_ReturnsHelpExitCode()
    {
        // No subcommand → print usage, exit 64 (same as Unknown at Program level).
        var code = await FixtureCommand.RunAsync(System.Array.Empty<string>().AsMemory(), CancellationToken.None);
        Assert.Equal(64, code);
    }

    [Fact]
    public async Task Create_MissingFromFlag_ReturnsTwo()
    {
        var code = await FixtureCommand.RunAsync(new[] { "create", "myfix" }.AsMemory(), CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Create_MissingNameArg_ReturnsTwo()
    {
        var code = await FixtureCommand.RunAsync(new[] { "create" }.AsMemory(), CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Create_ScriptFileMissing_ReturnsTwo()
    {
        var code = await FixtureCommand.RunAsync(
            new[] { "create", "myfix", "--from", "/tmp/does-not-exist.fixture.json" }.AsMemory(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task List_NoFixtures_ReturnsZero()
    {
        // Runs against the repo's tests/fixtures/ — if empty or missing, exit 0 silently.
        // This test is sensitive to whether tests/fixtures/ has anything; treat as smoke.
        var code = await FixtureCommand.RunAsync(new[] { "list" }.AsMemory(), CancellationToken.None);
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task UnknownSubcommand_ReturnsHelpExitCode()
    {
        var code = await FixtureCommand.RunAsync(new[] { "bogus" }.AsMemory(), CancellationToken.None);
        Assert.Equal(64, code);
    }
}
```

Run: `dotnet test tests/Runner.Tests/ --filter FixtureCommand`
Expected: FAIL.

- [ ] **Step 2: Create FixtureCommand**

Create `src/Runner/Commands/FixtureCommand.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Fixtures;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test fixture [create|list]</c>. `create` builds a fixture from a `.fixture.json`
/// script; `list` enumerates existing fixtures in <c>tests/fixtures/</c>.
/// </summary>
public static class FixtureCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 64;
        }

        return args.Span[0] switch
        {
            "create" => await CreateAsync(args[1..], ct),
            "list" => ListAsync(),
            _ => Unknown(args.Span[0]),
        };
    }

    private static int Unknown(string subcommand)
    {
        Console.Error.WriteLine($"fixture: unknown subcommand '{subcommand}'");
        PrintHelp(Console.Error);
        return 64;
    }

    private static async Task<int> CreateAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        // Parse: <name> --from <script> [--mods-path X] [--force]
        string? name = null;
        string? fromPath = null;
        string? modsPath = null;
        bool force = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--from" && i + 1 < args.Length) { fromPath = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            if (a == "--force") { force = true; continue; }
            if (a.StartsWith("--")) { Console.Error.WriteLine($"unknown flag: {a}"); return 2; }
            if (name is null) { name = a; continue; }
            Console.Error.WriteLine($"unexpected positional argument: {a}");
            return 2;
        }

        if (string.IsNullOrEmpty(name)) { Console.Error.WriteLine("usage: sdv-test fixture create <name> --from <script>"); return 2; }
        if (string.IsNullOrEmpty(fromPath)) { Console.Error.WriteLine("fixture create: --from <script> is required"); return 2; }
        if (!File.Exists(fromPath)) { Console.Error.WriteLine($"script not found at {fromPath}"); return 2; }

        FixtureSpec spec;
        try { spec = FixtureLoader.Load(fromPath); }
        catch (FixtureLoadException ex) { Console.Error.WriteLine($"[load-error] {ex.Message}"); return 2; }

        if (spec.Name != name)
        {
            Console.Error.WriteLine($"name mismatch: CLI arg '{name}' vs script name '{spec.Name}'");
            return 2;
        }

        var fixturesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures");
        var targetDir = Path.Combine(fixturesRoot, name);
        if (Directory.Exists(targetDir) && !force)
        {
            Console.Error.WriteLine($"tests/fixtures/{name}/ already exists — pass --force to overwrite");
            return 3;
        }

        // Resolve the base fixture exists (if specified) before launching SDV.
        if (!string.IsNullOrEmpty(spec.Base))
        {
            var basePath = Path.Combine(fixturesRoot, spec.Base, "save");
            if (!Directory.Exists(basePath))
            {
                Console.Error.WriteLine($"base fixture '{spec.Base}' not found — did you forget to build it?");
                return 2;
            }
        }

        // Launch SDV + run build via FixtureBuilder.
        return await RunBuildAsync(spec, fromPath, fixturesRoot, modsPath, ct);
    }

    private static async Task<int> RunBuildAsync(
        FixtureSpec spec, string scriptPath, string fixturesRoot, string? modsPath, CancellationToken ct)
    {
        // Resolve mods path (same logic as RunCommand).
        modsPath ??= Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
        {
            modsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "mods");
        }
        Directory.CreateDirectory(modsPath);
        HarnessDeployer.Deploy(modsPath);

        // Stage the base fixture into SDV's saves dir BEFORE launching SDV.
        // SDV saves live in a platform-dependent dir; use HOME/.config/StardewValley/Saves
        // on Linux direct; the Flatpak redirection path is handled by SDV itself once
        // the game launches (it reads from Constants.SavesPath at runtime).
        var sdvSavesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "StardewValley", "Saves");
        Directory.CreateDirectory(sdvSavesDir);

        if (!string.IsNullOrEmpty(spec.Base))
            FixtureStager.Stage(spec.Base, fixturesRoot, sdvSavesDir);

        // Launch SDV + connect + build.
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-test-fixture-{System.Guid.NewGuid():N}.sock");
        using var sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(60));
            for (int i = 0; i < 120 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);
            if (!File.Exists(socket))
                throw new TimeoutException("SDV never opened the test socket");

            using var session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            var result = await FixtureBuilder.BuildAsync(spec, session, ct);
            if (!result.Success)
            {
                Console.Error.WriteLine($"[build-error] {result.Error}");
                return 4;
            }

            // Capture the save back into the repo.
            FixtureStager.Capture(spec.Name, sdvSavesDir, fixturesRoot);

            // Write script copy + meta + README.
            var targetDir = Path.Combine(fixturesRoot, spec.Name);
            File.Copy(scriptPath, Path.Combine(targetDir, $"{spec.Name}.fixture.json"), overwrite: true);

            var meta = FixtureMetadata.Generate(
                spec,
                sdvVersion: result.SdvVersion,
                smapiVersion: result.SmapiVersion,
                mods: result.Mods,
                farmerName: result.FarmerName,
                farmerGender: result.FarmerGender,
                createdAtUtc: DateTime.UtcNow);
            File.WriteAllText(
                Path.Combine(targetDir, $"{spec.Name}.meta.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions(SdvTestFramework.Protocol.Json.ProtocolJson.Options) { WriteIndented = true }));

            File.WriteAllText(
                Path.Combine(targetDir, $"{spec.Name}.README.md"),
                FixtureReadme.Generate(spec, meta));

            Console.WriteLine($"[ok] fixture '{spec.Name}' created at tests/fixtures/{spec.Name}/");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[fixture create] fatal: {ex.Message}");
            return 4;
        }
        finally
        {
            try { if (!sdv.HasExited) { sdv.Kill(); sdv.WaitForExit(5000); } } catch { }
        }
    }

    private static int ListAsync()
    {
        var fixturesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures");
        if (!Directory.Exists(fixturesRoot)) return 0;

        foreach (var dir in Directory.GetDirectories(fixturesRoot))
        {
            var name = Path.GetFileName(dir);
            var metaPath = Path.Combine(dir, $"{name}.meta.json");
            if (!File.Exists(metaPath)) continue;

            try
            {
                var meta = JsonSerializer.Deserialize<FixtureMetadata>(
                    File.ReadAllText(metaPath), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
                if (meta is not null)
                    Console.WriteLine($"  {meta.Name} — {meta.Description} (created {meta.CreatedAt})");
            }
            catch { /* malformed meta — skip silently */ }
        }
        return 0;
    }

    private static void PrintHelp(TextWriter? output = null)
    {
        var w = output ?? Console.Out;
        w.WriteLine("sdv-test fixture — create/list test fixtures");
        w.WriteLine();
        w.WriteLine("Subcommands:");
        w.WriteLine("  create <name> --from <script.fixture.json> [--mods-path X] [--force]");
        w.WriteLine("      Build a new fixture by loading a base, running steps, and saving.");
        w.WriteLine("  list");
        w.WriteLine("      Enumerate fixtures in tests/fixtures/.");
    }
}
```

- [ ] **Step 3: Run CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count 223 → 229 (+6 new passing tests).

---

## Task 9: Program.cs wiring + RunCommand staging

**Why:** Make the `fixture` subcommand reachable from `sdv-test fixture …`, and wire `RunCommand` to stage every unique scenario-fixture before launching SDV.

**Files:**
- Modify: `src/Runner/Program.cs`
- Modify: `src/Runner/Commands/RunCommand.cs`

**Dependencies:** Task 8 (FixtureCommand); Task 4 (FixtureStager).

- [ ] **Step 1: Wire fixture into Program.cs dispatch**

In `src/Runner/Program.cs`, find the existing `args[0] switch` block:

```csharp
        return args[0] switch
        {
            "probe" => await ProbeCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "doctor" => await DoctorCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "list" => await ListCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "run" => await RunCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            _ => Unknown(args[0]),
        };
```

Add a new case:

```csharp
        return args[0] switch
        {
            "probe" => await ProbeCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "doctor" => await DoctorCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "list" => await ListCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "run" => await RunCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "fixture" => await FixtureCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            _ => Unknown(args[0]),
        };
```

Update the `PrintHelp()` method to mention the new command. After the existing `run` block, add:

```csharp
        w.WriteLine("  fixture create <name> --from <script>");
        w.WriteLine("                    Build a reproducible save-state fixture in tests/fixtures/.");
        w.WriteLine("  fixture list      Enumerate existing fixtures.");
```

- [ ] **Step 2: Wire RunCommand staging**

In `src/Runner/Commands/RunCommand.cs`, find the section that loads scenarios (around the `scenarios.Add(...)` block). After scenarios are collected and filtered but BEFORE `SdvLauncher.Launch`, insert:

```csharp
        // Stage every unique fixture referenced by the scenario set into SDV's saves dir.
        // Fixtures live in tests/fixtures/<name>/save/ in the repo; SDV expects them in
        // Constants.SavesPath (resolved client-side here to HOME/.config/StardewValley/Saves).
        var fixturesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures");
        var sdvSavesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "StardewValley", "Saves");
        Directory.CreateDirectory(sdvSavesDir);

        if (Directory.Exists(fixturesRoot))
        {
            var seen = new HashSet<string>();
            foreach (var (_, spec) in scenarios)
            {
                if (string.IsNullOrEmpty(spec.Fixture) || !seen.Add(spec.Fixture)) continue;
                var src = Path.Combine(fixturesRoot, spec.Fixture, "save");
                if (!Directory.Exists(src))
                {
                    // Fixture not in repo — let scenario execution error via fixture.load
                    // if the fixture is also missing from SDV's saves dir. Don't fail fast
                    // here because older fixtures may still live in the user's saves dir
                    // (e.g. the M0 spike's m0spike save before the T10 migration lands).
                    continue;
                }
                try { SdvTestFramework.Runner.Fixtures.FixtureStager.Stage(spec.Fixture, fixturesRoot, sdvSavesDir); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[stage-error] fixture '{spec.Fixture}': {ex.Message}");
                    return 2;
                }
            }
        }
```

Add `using SdvTestFramework.Runner.Fixtures;` at the top if not already present (or use the fully qualified name as shown above — either works).

- [ ] **Step 3: Full CI**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged at 229.

---

## Task 10: Migrate m0spike fixture into the repo

**Why:** The existing 10 sample scenarios reference `"fixture": "m0spike_436515781"`. Post-T9, the staging logic looks in `tests/fixtures/` first. Without the migration, `./scripts/run-samples.sh` would fail because the fixture isn't in the repo.

**Files:**
- Create: `tests/fixtures/m0spike_436515781/save/SaveGameInfo` (copied from user's saves dir)
- Create: `tests/fixtures/m0spike_436515781/save/m0spike_436515781` (copied)
- Create: `tests/fixtures/m0spike_436515781/m0spike_436515781.fixture.json`
- Create: `tests/fixtures/m0spike_436515781/m0spike_436515781.meta.json`
- Create: `tests/fixtures/m0spike_436515781/m0spike_436515781.README.md`

**Dependencies:** Tasks 4, 5 (FixtureStager + metadata shape) — we match the layout they expect.

- [ ] **Step 1: Copy the save files into the repo**

Run:

```bash
mkdir -p tests/fixtures/m0spike_436515781/save
cp ~/.config/StardewValley/Saves/m0spike_436515781/SaveGameInfo tests/fixtures/m0spike_436515781/save/
cp ~/.config/StardewValley/Saves/m0spike_436515781/m0spike_436515781 tests/fixtures/m0spike_436515781/save/
ls -la tests/fixtures/m0spike_436515781/save/
```

Expected: directory exists with `SaveGameInfo` + `m0spike_436515781`, both > 0 bytes.

- [ ] **Step 2: Create the stub fixture.json**

Create `tests/fixtures/m0spike_436515781/m0spike_436515781.fixture.json`:

```json
{
  "name": "m0spike_436515781",
  "base": null,
  "description": "Day-1 clean farmhouse from the M0 determinism spike. Manual capture — cannot be regenerated with the scripted builder. Serves as the initial root fixture for all derived fixtures."
}
```

Note the absent `steps` field — the schema makes it optional.

- [ ] **Step 3: Create the meta.json**

Create `tests/fixtures/m0spike_436515781/m0spike_436515781.meta.json`:

```json
{
  "name": "m0spike_436515781",
  "description": "Day-1 clean farmhouse from the M0 determinism spike. Manual capture — cannot be regenerated with the scripted builder. Serves as the initial root fixture for all derived fixtures.",
  "sdv_version": "1.6.15",
  "smapi_version": "4.5.2",
  "mods_installed": [],
  "created_at": "2026-04-22T00:00:00.0000000Z",
  "created_by": "m0-determinism-spike",
  "base": null,
  "regenerate_with": "docs/spikes/2026-04-determinism/REPORT.md",
  "farmer": {
    "name": "Tester",
    "gender": "female"
  }
}
```

- [ ] **Step 4: Create the README.md**

Create `tests/fixtures/m0spike_436515781/m0spike_436515781.README.md`:

```markdown
# m0spike_436515781

Day-1 clean farmhouse from the M0 determinism spike. Manual capture — cannot be regenerated with the scripted builder. Serves as the initial root fixture for all derived fixtures built via `sdv-test fixture create`.

## Environment

- SDV 1.6.15
- SMAPI 4.5.2
- Farmer: Tester (female)
- Created: 2026-04-22T00:00:00.0000000Z during the M0 determinism spike

## How this was captured

Played manually through SDV's intro and character creation, saved at Day 1 in the farmhouse. See `docs/spikes/2026-04-determinism/REPORT.md` for the full spike context.

## Regenerate

Not regenerable via the scripted builder — this is the root that derived fixtures build from. If SDV updates break this save, re-capture manually and update the `sdv_version` field in `m0spike_436515781.meta.json`.

_This file is maintained by hand since the fixture predates the scripted builder. Derived fixtures get an auto-generated README._
```

- [ ] **Step 5: Verify CI still passes + sample smoke works**

Run: `./scripts/ci.sh`
Expected: PASS. Test count unchanged at 229.

Then run the sample smoke to confirm the staging logic finds the fixture in `tests/fixtures/`:

```bash
./scripts/run-samples.sh
```

Expected: `[run] 10/10 passed` — same as the D1.7 ship result.

---

## Task 11: End-to-end smoke + docs + milestone note

**Why:** Validate the full build path end-to-end: derive a fresh fixture from m0spike, confirm it lands in `tests/fixtures/`, confirm `fixture list` sees both fixtures. Document the new RPCs.

**Files:**
- Create: `/tmp/d17-sample.fixture.json` (ephemeral test script — not committed)
- Create: `tests/Harness.Tests/FixtureBuilderIntegrationTests.cs`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/milestones/current.md`

**Dependencies:** Tasks 1-10.

- [ ] **Step 1: Add skip-marked integration tests**

Create `tests/Harness.Tests/FixtureBuilderIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration surface for the M2 fixture builder — exercised via T11's smoke run.</summary>
public class FixtureBuilderIntegrationTests
{
    [Fact(Skip = "Requires live SDV + Content Patcher — fixture-builder smoke (T11) verifies this.")]
    public void FixtureCreate_EndToEnd_ProducesValidFixtureDirectory() { }

    [Fact(Skip = "Requires live SDV — smoke confirms derived fixtures load in scenarios.")]
    public void DerivedFixture_LoadsInScenario_RunsToCompletion() { }

    [Fact(Skip = "Requires live SDV — smoke confirms fixture list enumerates m0spike + any newly-built fixtures.")]
    public void FixtureList_EnumeratesCommittedFixtures() { }
}
```

- [ ] **Step 2: Write a test fixture script**

Create `/tmp/d17-sample.fixture.json` (temporary, not committed):

```json
{
  "name": "test_day2",
  "base": "m0spike_436515781",
  "description": "Test fixture: m0spike + 500g + advance 120 minutes.",
  "steps": [
    { "action": "player.set_money", "args": { "amount": 500 } },
    { "action": "time.advance", "args": { "minutes": 120 } }
  ]
}
```

- [ ] **Step 3: Run the end-to-end fixture build**

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
pkill -9 -f StardewModdingAPI 2>/dev/null || true
pkill Xvfb 2>/dev/null || true
sleep 1
rm -rf ~/.cache/sdv-test-framework/mods
rm -rf tests/fixtures/test_day2
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
XVFB_PID=$!
trap "pkill -9 -f StardewModdingAPI 2>/dev/null; kill $XVFB_PID 2>/dev/null; exit" EXIT

DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release -- \
    fixture create test_day2 --from /tmp/d17-sample.fixture.json
```

Expected: `[ok] fixture 'test_day2' created at tests/fixtures/test_day2/`, exit 0.

Verify the directory shape:

```bash
ls -la tests/fixtures/test_day2/
ls -la tests/fixtures/test_day2/save/
cat tests/fixtures/test_day2/test_day2.meta.json
```

Expected: `save/` contains `SaveGameInfo` + save file; `test_day2.fixture.json` + `test_day2.meta.json` + `test_day2.README.md` all present; `test_day2.meta.json` has `mods_installed` non-empty (the harness mod at minimum), `sdv_version`, `smapi_version`, `farmer`.

- [ ] **Step 4: Verify sample smoke still passes**

```bash
pkill -9 -f StardewModdingAPI 2>/dev/null || true
pkill Xvfb 2>/dev/null || true
sleep 1
./scripts/run-samples.sh
```

Expected: `[run] 10/10 passed` — same as D1.7 baseline.

- [ ] **Step 5: Verify fixture list**

```bash
dotnet run --project src/Runner -c Release -- fixture list
```

Expected output (order may vary — one line per fixture):

```
  m0spike_436515781 — Day-1 clean farmhouse from the M0 determinism spike. … (created 2026-04-22T00:00:00.0000000Z)
  test_day2 — Test fixture: m0spike + 500g + advance 120 minutes. (created <current UTC>)
```

- [ ] **Step 6: Clean up the ephemeral test fixture**

```bash
rm -rf tests/fixtures/test_day2
rm /tmp/d17-sample.fixture.json
```

The test fixture was just to validate the end-to-end flow — it doesn't belong in the repo.

- [ ] **Step 7: Document `fixture.save` + `state.mods` in rpc-schema.md**

In `docs/rpc-schema.md`, find the `### fixture.load` section. After it, insert:

```markdown
### fixture.save

Trigger SDV's save flow, writing the current game state to a folder in `Constants.SavesPath`. Drives `SaveGame.Save()` to completion on the game thread (blocks one update tick's worth of logic, typically <1 second).

**Params:** `{name: string}` — destination folder name. The handler doesn't use this directly — SDV writes to `Game1.player.farmName + "_" + Game1.uniqueIDForThisGame`. The Runner then renames/copies as needed.

**Preconditions (strict):**
- `Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame` — world is loaded and playable.
- `!Game1.eventUp` — no cutscene active.
- `Game1.currentMinigame == null` — no minigame active.
- `!Game1.isWarping` — not mid-warp.

**Response:** `{ok: true, tick: T, save_path: "/abs/path/to/SDV/Saves/<farmName>_<uniqueID>"}`.

**Errors:** `GameStateInvalid (-32003)` for any precondition violation. `InvalidParams (-32602)` if `name` is missing or empty. `InternalError (-32603)` wraps `TimeoutException` when the save coroutine exceeds 30s.

### state.mods

Return the list of loaded mod UniqueIDs in SMAPI load order. Used by the fixture builder to populate `.meta.json`'s `mods_installed` field.

**Params:** none.

**Response:** `{mods: ["UniqueID1", "UniqueID2", ...]}`. Empty array if the harness wasn't wired with an `IModRegistry` (shouldn't happen in production).
```

- [ ] **Step 8: Update docs/milestones/current.md**

Open `docs/milestones/current.md`. After the existing `### D1.7 — Sample suite + DSL extensions landed` subsection and before `## M0 outcome`, insert a new M2 progress block:

```markdown
## M2 — Production polish (in progress)

M1 shipped (see D1.7 completion note above). M2 decomposes per spec §7 Phase 2 into five independent subprojects, each shipping its own plan + smoke:

1. **Fixture builder tool** (§4.8) — scripted creation of reproducible save-state fixtures.
2. Record mode (§4.7) — deferred.
3. TAP + JUnit reporters (§4.7) — deferred.
4. Watch mode (§4.7) — deferred.
5. Bitmap fallback + SSIM (§4.5) — deferred.

### M2 subproject 1 — Fixture builder landed (2026-04-23)

Plan: `docs/superpowers/plans/2026-04-23-m2-fixture-builder.md` (11 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-23-m2-fixture-builder-design.md`.

**Scope:** `sdv-test fixture create <name> --from <script.fixture.json>` builds a fixture by loading a base, running RPC steps, invoking the new `fixture.save` RPC, and copying the resulting save + auto-generated `.meta.json` + `.README.md` into `tests/fixtures/<name>/`. `sdv-test fixture list` enumerates fixtures in the repo. The staging layer (`FixtureStager`) transparently copies `tests/fixtures/<name>/save/` → SDV's `Constants.SavesPath` at scenario-run time, so existing scenarios that reference fixtures by name keep working without modification.

**Migration:** `m0spike_436515781` was migrated from the user's SDV saves dir into `tests/fixtures/m0spike_436515781/` so the full fixture chain lives in git. The spike save is a "root" fixture with `base: null` — it cannot be regenerated with the scripted builder, but derived fixtures can build from it.

**New RPCs:** `fixture.save` (drives `SaveGame.Save()` to completion), `state.mods` (lists loaded mod UniqueIDs for metadata).

**Smoke result:** `sdv-test fixture create test_day2 --from /tmp/d17-sample.fixture.json` produced a complete `tests/fixtures/test_day2/` in ~15 seconds. `./scripts/run-samples.sh` still reports **10/10 passed** post-migration. `sdv-test fixture list` enumerates both fixtures.

**TODOs for later M2/M3 work:**
- Interactive path (`sdv-test fixture create --interactive`) pairs with spec §4.7 record mode.
- New-game base (build from character creation) requires new RPCs driving intro menus; deferred to M3.
- Git LFS — defer until the repo has >5 fixtures per `.claude/rules/fixtures.md`.
- `fixture delete` / `fixture validate` commands — `rm -rf` + load-time schema validation cover for now.

**Test count after M2 fixture-builder:** ~229 Passed + ~28 Skipped (was 201+26 before M2; +28 passed, +2 skipped). Counts:
- T1: +3 (Protocol DTO serialization)
- T2: +2 (StateModsHandler)
- T3: +7 (FixtureLoader schema + parse paths)
- T4: +4 (FixtureStager)
- T5: +4 (FixtureMetadata + FixtureReadme)
- T6: +1 (FixtureBuilder orchestration)
- T7: +1 (FixtureSaveHandler param validation); +2 Skipped (integration)
- T8: +6 (FixtureCommand arg parsing + list); +3 Skipped (T11 integration)
```

- [ ] **Step 9: Final CI**

Run: `./scripts/ci.sh`
Expected: PASS. Final test count ~229 Passed + ~28 Skipped.

---

## Self-review

**1. Spec coverage:**
- (Architecture — Runner subcommand) → Task 8 (FixtureCommand) + Task 9 (Program.cs wiring) ✓
- (Architecture — Harness RPCs) → Task 2 (state.mods) + Task 7 (fixture.save) ✓
- (Architecture — Staging layer) → Task 4 (FixtureStager) + Task 9 (RunCommand staging hook) ✓
- (CLI: `fixture create <name> --from <script>`) → Task 8 step 2 ✓
- (CLI: `fixture list`) → Task 8 step 2 ✓
- (Exit codes 0/2/3/4) → Task 8 step 2 ✓ (verified via arg-parsing tests in step 1)
- (`<name>.fixture.json` shape) → Task 1 step 5 (schema) + Task 3 (loader) ✓
- (`<name>.meta.json` auto-generation) → Task 5 ✓
- (`<name>.README.md` auto-generation) → Task 5 ✓
- (Fixture directory layout) → Task 4 (stager) + Task 10 (m0spike migration demonstrates the layout) ✓
- (`fixture.save` preconditions) → Task 7 ✓
- (`state.mods` registry pattern) → Task 2 ✓
- (Error handling: script missing, collision, base missing) → Task 8 ✓
- (Existing 10 sample scenarios still pass) → Task 10 step 5 + Task 11 step 4 ✓
- (Docs: rpc-schema.md update) → Task 11 step 7 ✓
- (Docs: milestones/current.md update) → Task 11 step 8 ✓

**2. Placeholder scan:** no TBD / TODO / "implement later" / vague requirements. Every code step has exact content. The `/tmp/d17-sample.fixture.json` in T11 step 2 is an intentional ephemeral file (cleaned up in T11 step 6).

**3. Type consistency:**
- `FixtureSaveRequest.Name` + `FixtureSaveResult.SavePath` — used consistently in T1 (DTOs), T7 (handler), T6 (builder), T8 (command). ✓
- `ModsState.Mods` — Task 1 (DTO) + Task 2 (handler) + Task 6 (builder reads `mods` property). ✓
- `FixtureSpec.Name` / `.Base` / `.Description` / `.Steps` — Task 3 (DTO + loader) consumed by Task 6 (builder) and Task 8 (command). ✓
- `FixtureStep.Action` / `.Args` — Task 3 (DTO) consumed by Task 6. ✓
- `FixtureMetadata.*` — Task 5 (generator) consumed by Task 8 (command writes it out) and Task 11 step 8 (docs mention). ✓
- `FixtureStager.Stage(name, fixturesRoot, sdvSavesDir)` / `Capture(name, sdvSavesDir, fixturesRoot)` — arg order verified consistent across T4, T8, T9. ✓
- The widened predicate `Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame` — used in T7 matching the D1.7 T1 / StateTimeHandler / FreezeBeginHandler precedents. ✓

**4. Hazard notes:**
- T7's `DriveSaveToCompletion` synchronously iterates `SaveGame.Save()`. If SDV ever changes `SaveGame.Save` to require inter-tick awaits (e.g. async I/O that yields), this will hang. Mitigation: 30-second timeout; TimeoutException will surface as `InternalError`. Plan steps (T11 smoke) will catch any such regression.
- T10's m0spike migration uses hardcoded paths (`~/.config/StardewValley/Saves/`). If the user's SDV lives elsewhere (Flatpak, Windows, custom) the T10 steps will fail. T10 is a one-time migration — documented in the README of the new fixture directory that manual re-capture is the fallback.
- T9's staging hook in RunCommand silently tolerates missing fixtures in `tests/fixtures/` — preserves backward compatibility with the pre-migration state where m0spike lived only in SDV's saves dir. Post-T10 + T11 smoke pass, this tolerance keeps being useful (a future user who deletes `tests/fixtures/m0spike_436515781/` by accident gets a scenario-level error rather than a runner-level fail-fast).
- T11 step 8's test-count summary assumes all prior tasks' counts compose cleanly — they should, but subtle count mismatches surface during execution and are fixed in the completion note.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-23-m2-fixture-builder.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task with two-stage review (spec compliance then code quality) between each. Proven across D1.5 / D1.6 / D1.7 cycles.

**2. Inline Execution** — execute tasks in this session via `superpowers:executing-plans`, batch through with checkpoints.

**Which approach?**
