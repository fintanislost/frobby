# SVE Slice 3 Runtime Content Assets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a neutral runtime `content.asset` query and JSON assertion support so SVE can prove final Content Patcher-patched maps, data assets, and textures are available in the live Stardew content pipeline.

**Architecture:** Add protocol DTOs for a bounded asset summary, a harness-side injectable content loader/projector, a registered `content.asset` RPC, and a runner assertion type that evaluates the existing small expression style against the returned asset summary. Use SVE only for the proof scenario; the Frobby implementation stays mod-agnostic.

**Tech Stack:** C#/.NET 6, xUnit, SMAPI `IGameContentHelper`, Stardew `Game1.content`/runtime content types, MonoGame `Texture2D`, xTile `Map`, System.Text.Json nodes.

---

## File Structure

- Create `src/Protocol/Models/ContentAssetRequest.cs` — request DTO for `content.asset`.
- Create `src/Protocol/Models/ContentAssetResult.cs` — response DTO with `JsonObject Summary` to preserve exact runtime asset keys.
- Modify `src/Protocol/Models/ScenarioAssertion.cs` — add `content.asset` assertion fields: `Asset`, `AssetType`, `IncludeKeys`, `KeysLimit`, `EntryKeys`, `HashTexture`.
- Create `tests/Protocol.Tests/ContentAssetSerializationTests.cs` — snake-case and exact-key serialization coverage.
- Create `src/Harness/Assets/IContentAssetLoader.cs` — small test seam for typed asset loads.
- Create `src/Harness/Assets/SmapiContentAssetLoader.cs` — production wrapper over `IGameContentHelper`.
- Create `src/Harness/Assets/ContentAssetProjector.cs` — request validation, typed load selection, and bounded summaries.
- Create `src/Harness/Handlers/ContentAssetHandler.cs` — RPC handler for `content.asset`.
- Modify `src/Harness/ModEntry.cs` — wire `helper.GameContent` into the handler and register the RPC.
- Create `tests/Harness.Tests/ContentAssetProjectorTests.cs` — unit tests for request validation and data/map/texture-ish summaries without a live game.
- Create `tests/Harness.Tests/ContentAssetHandlerTests.cs` — handler tests for missing loader and serialization surface.
- Modify `src/Runner/Scenarios/ScenarioRunner.cs` — add `content.asset` assertion evaluation.
- Add to `tests/Runner.Tests/ScenarioRunnerDslTests.cs` or create `tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs` — fake-harness tests for passing/failing content assertions.
- Modify `schemas/scenario.schema.json` — allow `content.asset` assertion fields.
- Modify `docs/rpc-schema.md`, `docs/dsl-quickstart.md`, and `README.md` — document the RPC and scenario assertion.
- Add `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/04-sve-content-assets-runtime.test.json` — live SVE proof scenario.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md` — mark Slice 3 active/done as implementation progresses and record the plan path.

## Task 1: Protocol DTOs And Scenario Schema

**Files:**
- Create: `src/Protocol/Models/ContentAssetRequest.cs`
- Create: `src/Protocol/Models/ContentAssetResult.cs`
- Modify: `src/Protocol/Models/ScenarioAssertion.cs`
- Modify: `schemas/scenario.schema.json`
- Create: `tests/Protocol.Tests/ContentAssetSerializationTests.cs`

- [ ] **Step 1: Write failing protocol serialization tests**

Create `tests/Protocol.Tests/ContentAssetSerializationTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ContentAssetSerializationTests
{
    [Fact]
    public void Request_SerializesSnakeCaseFields()
    {
        var req = new ContentAssetRequest
        {
            Name = "Data/Locations",
            AssetType = "data",
            IncludeKeys = true,
            KeysLimit = 25,
            EntryKeys = new[] { "Custom_TownEast" },
            HashTexture = true,
        };

        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);

        Assert.Contains("\"name\":\"Data/Locations\"", json);
        Assert.Contains("\"asset_type\":\"data\"", json);
        Assert.Contains("\"include_keys\":true", json);
        Assert.Contains("\"keys_limit\":25", json);
        Assert.Contains("\"entry_keys\":[\"Custom_TownEast\"]", json);
        Assert.Contains("\"hash_texture\":true", json);
    }

    [Fact]
    public void Result_PreservesExactSummaryKeys()
    {
        var summary = new JsonObject
        {
            ["entries"] = new JsonObject
            {
                ["Custom_TownEast"] = new JsonObject
                {
                    ["exists"] = true,
                },
            },
        };
        var result = new ContentAssetResult
        {
            Name = "Data/Locations",
            Exists = true,
            Kind = "data",
            RuntimeType = "System.Collections.Generic.Dictionary`2",
            Summary = summary,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"runtime_type\":\"System.Collections.Generic.Dictionary`2\"", json);
        Assert.Contains("\"Custom_TownEast\"", json);
        Assert.DoesNotContain("custom_town_east", json);
    }

    [Fact]
    public void ScenarioAssertion_SerializesContentAssetFields()
    {
        var assertion = new ScenarioAssertion
        {
            Type = "content.asset",
            Asset = "Maps/Custom_TownEast",
            AssetType = "map",
            Expr = "asset.layers contains name 'Back'",
            IncludeKeys = true,
            KeysLimit = 10,
            EntryKeys = new[] { "Custom_TownEast" },
            HashTexture = false,
        };

        var json = JsonSerializer.Serialize(assertion, ProtocolJson.Options);

        Assert.Contains("\"type\":\"content.asset\"", json);
        Assert.Contains("\"asset\":\"Maps/Custom_TownEast\"", json);
        Assert.Contains("\"asset_type\":\"map\"", json);
        Assert.Contains("\"include_keys\":true", json);
        Assert.Contains("\"keys_limit\":10", json);
        Assert.Contains("\"entry_keys\":[\"Custom_TownEast\"]", json);
        Assert.Contains("\"hash_texture\":false", json);
    }
}
```

- [ ] **Step 2: Run protocol tests to verify they fail**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ContentAssetSerializationTests
```

Expected: compile fails because `ContentAssetRequest`, `ContentAssetResult`, and `ScenarioAssertion` fields do not exist.

- [ ] **Step 3: Add protocol DTOs**

Create `src/Protocol/Models/ContentAssetRequest.cs`:

```csharp
namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for the <c>content.asset</c> RPC.</summary>
public sealed class ContentAssetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? AssetType { get; set; }
    public bool IncludeKeys { get; set; }
    public int? KeysLimit { get; set; }
    public string[]? EntryKeys { get; set; }
    public bool HashTexture { get; set; }
}
```

Create `src/Protocol/Models/ContentAssetResult.cs`:

```csharp
using System.Text.Json.Nodes;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for the <c>content.asset</c> RPC.</summary>
public sealed class ContentAssetResult
{
    public string Name { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public string Kind { get; set; } = "missing";
    public string RuntimeType { get; set; } = string.Empty;

    /// <summary>
    /// Asset-specific bounded metadata. JsonObject is intentional so runtime asset keys
    /// like <c>Custom_TownEast</c> are not transformed by the protocol dictionary naming policy.
    /// </summary>
    public JsonObject Summary { get; set; } = new();
}
```

Modify `src/Protocol/Models/ScenarioAssertion.cs` by adding these properties after `Expr`:

```csharp
    /// <summary>For <c>content.asset</c> assertions: runtime asset name to query.</summary>
    public string? Asset { get; set; }

    /// <summary>For <c>content.asset</c> assertions: optional type hint such as map, texture, data, or string.</summary>
    public string? AssetType { get; set; }

    /// <summary>For <c>content.asset</c> data assertions: include a bounded key list.</summary>
    public bool? IncludeKeys { get; set; }

    /// <summary>For <c>content.asset</c> data assertions: maximum key count to include.</summary>
    public int? KeysLimit { get; set; }

    /// <summary>For <c>content.asset</c> data assertions: selected data entries to summarize.</summary>
    public string[]? EntryKeys { get; set; }

    /// <summary>For <c>content.asset</c> texture assertions: include content hash when possible.</summary>
    public bool? HashTexture { get; set; }
```

- [ ] **Step 4: Update scenario schema**

Modify `schemas/scenario.schema.json` under `assertions.items.properties`:

```json
          "asset": { "type": "string", "minLength": 1 },
          "asset_type": {
            "type": "string",
            "enum": ["map", "texture", "data", "string", "unknown"]
          },
          "include_keys": { "type": "boolean" },
          "keys_limit": { "type": "integer", "minimum": 1, "maximum": 500 },
          "entry_keys": {
            "type": "array",
            "items": { "type": "string", "minLength": 1 }
          },
          "hash_texture": { "type": "boolean" },
```

- [ ] **Step 5: Run protocol and loader schema tests**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ContentAssetSerializationTests
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ScenarioLoaderTests
```

Expected: both pass.

- [ ] **Step 6: Commit Task 1**

```bash
git add src/Protocol/Models/ContentAssetRequest.cs src/Protocol/Models/ContentAssetResult.cs src/Protocol/Models/ScenarioAssertion.cs schemas/scenario.schema.json tests/Protocol.Tests/ContentAssetSerializationTests.cs
git commit -m "feat: add content asset protocol models"
```

## Task 2: Harness Asset Projector

**Files:**
- Create: `src/Harness/Assets/IContentAssetLoader.cs`
- Create: `src/Harness/Assets/ContentAssetProjector.cs`
- Create: `tests/Harness.Tests/ContentAssetProjectorTests.cs`

- [ ] **Step 1: Write failing projector tests**

Create `tests/Harness.Tests/ContentAssetProjectorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ContentAssetProjectorTests
{
    private sealed class FakeLoader : IContentAssetLoader
    {
        private readonly Dictionary<(Type Type, string Name), object> _assets = new();

        public void Add<T>(string name, T asset) where T : notnull
            => _assets[(typeof(T), name)] = asset;

        public bool TryLoad<T>(string name, out T? asset) where T : notnull
        {
            if (_assets.TryGetValue((typeof(T), name), out var value))
            {
                asset = (T)value;
                return true;
            }
            asset = default;
            return false;
        }
    }

    [Fact]
    public void Project_RequiresName()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            ContentAssetProjector.Project(new FakeLoader(), new ContentAssetRequest()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Project_RejectsExcessiveKeyLimit()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            ContentAssetProjector.Project(new FakeLoader(), new ContentAssetRequest
            {
                Name = "Data/Locations",
                AssetType = "data",
                KeysLimit = 501,
            }));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("keys_limit", ex.Message);
    }

    [Fact]
    public void Project_DataDictionary_SummarizesKeysAndSelectedEntries()
    {
        var loader = new FakeLoader();
        loader.Add("Data/Locations", new Dictionary<string, string>
        {
            ["Custom_TownEast"] = "Town East payload",
            ["Custom_GrandpasShed"] = "Shed payload",
        });

        var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/Locations",
            AssetType = "data",
            IncludeKeys = true,
            KeysLimit = 10,
            EntryKeys = new[] { "Custom_TownEast", "Missing_Key" },
        });

        Assert.True(result.Exists);
        Assert.Equal("data", result.Kind);
        Assert.Equal(2, (int)result.Summary["count"]!);
        var keys = Assert.IsType<JsonArray>(result.Summary["keys"]);
        Assert.Contains(keys, node => node?.GetValue<string>() == "Custom_TownEast");
        var entries = Assert.IsType<JsonObject>(result.Summary["entries"]);
        Assert.True(entries["Custom_TownEast"]!["exists"]!.GetValue<bool>());
        Assert.Equal("Town East payload", entries["Custom_TownEast"]!["value"]!.GetValue<string>());
        Assert.False(entries["Missing_Key"]!["exists"]!.GetValue<bool>());
    }

    [Fact]
    public void Project_MissingAsset_ReturnsMissingResult()
    {
        var result = ContentAssetProjector.Project(new FakeLoader(), new ContentAssetRequest
        {
            Name = "Maps/Missing",
            AssetType = "map",
        });

        Assert.False(result.Exists);
        Assert.Equal("missing", result.Kind);
        Assert.Equal("Maps/Missing", result.Name);
    }
}
```

- [ ] **Step 2: Run projector tests to verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjectorTests
```

Expected: compile fails because `IContentAssetLoader` and `ContentAssetProjector` do not exist.

- [ ] **Step 3: Add loader interface**

Create `src/Harness/Assets/IContentAssetLoader.cs`:

```csharp
namespace SdvTestFramework.Harness.Assets;

/// <summary>Small seam for testing runtime content loading without launching Stardew.</summary>
public interface IContentAssetLoader
{
    bool TryLoad<T>(string name, out T? asset) where T : notnull;
}
```

- [ ] **Step 4: Add first projector implementation**

Create `src/Harness/Assets/ContentAssetProjector.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using xTile;

namespace SdvTestFramework.Harness.Assets;

public static class ContentAssetProjector
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "map", "texture", "data", "string", "unknown",
    };

    public static ContentAssetResult Project(IContentAssetLoader loader, ContentAssetRequest req)
    {
        Validate(req);

        var type = string.IsNullOrWhiteSpace(req.AssetType) ? null : req.AssetType;
        if (type is "map")
            return loader.TryLoad<Map>(req.Name, out var map) && map is not null
                ? Found(req.Name, "map", map.GetType(), SummarizeMap(map))
                : Missing(req.Name);

        if (type is "texture")
            return loader.TryLoad<Texture2D>(req.Name, out var texture) && texture is not null
                ? Found(req.Name, "texture", texture.GetType(), SummarizeTexture(texture, req.HashTexture))
                : Missing(req.Name);

        if (type is "string")
            return loader.TryLoad<string>(req.Name, out var text) && text is not null
                ? Found(req.Name, "string", text.GetType(), new JsonObject
                {
                    ["text"] = text,
                    ["length"] = text.Length,
                })
                : Missing(req.Name);

        if (type is "data")
            return TryProjectData(loader, req) ?? Missing(req.Name);

        return TryProjectAuto(loader, req) ?? Missing(req.Name);
    }

    private static void Validate(ContentAssetRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "content.asset requires params.name");
        if (req.AssetType is { Length: > 0 } type && !AllowedTypes.Contains(type))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, $"unsupported asset_type: {type}");
        if (req.KeysLimit is < 1 or > 500)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "keys_limit must be between 1 and 500");
    }

    private static ContentAssetResult? TryProjectAuto(IContentAssetLoader loader, ContentAssetRequest req)
    {
        if (loader.TryLoad<Map>(req.Name, out var map) && map is not null)
            return Found(req.Name, "map", map.GetType(), SummarizeMap(map));
        if (loader.TryLoad<Texture2D>(req.Name, out var texture) && texture is not null)
            return Found(req.Name, "texture", texture.GetType(), SummarizeTexture(texture, req.HashTexture));
        if (loader.TryLoad<string>(req.Name, out var text) && text is not null)
            return Found(req.Name, "string", text.GetType(), new JsonObject { ["text"] = text, ["length"] = text.Length });
        return TryProjectData(loader, req);
    }

    private static ContentAssetResult? TryProjectData(IContentAssetLoader loader, ContentAssetRequest req)
    {
        if (loader.TryLoad<Dictionary<string, string>>(req.Name, out var stringDict) && stringDict is not null)
            return Found(req.Name, "data", stringDict.GetType(), SummarizeDictionary(stringDict, req));

        if (loader.TryLoad<Dictionary<string, object>>(req.Name, out var objectDict) && objectDict is not null)
            return Found(req.Name, "data", objectDict.GetType(), SummarizeDictionary(objectDict, req));

        return null;
    }

    private static JsonObject SummarizeMap(Map map)
    {
        var summary = new JsonObject
        {
            ["width"] = map.DisplayWidth / Math.Max(1, map.TileWidth),
            ["height"] = map.DisplayHeight / Math.Max(1, map.TileHeight),
            ["layers"] = new JsonArray(map.Layers.Select(layer => new JsonObject
            {
                ["name"] = layer.Id,
                ["width"] = layer.LayerWidth,
                ["height"] = layer.LayerHeight,
            }).ToArray<JsonNode?>()),
            ["tilesheets"] = new JsonArray(map.TileSheets.Select(sheet => new JsonObject
            {
                ["id"] = sheet.Id,
                ["image_source"] = sheet.ImageSource,
                ["tile_width"] = sheet.TileWidth,
                ["tile_height"] = sheet.TileHeight,
                ["sheet_width"] = sheet.SheetWidth,
                ["sheet_height"] = sheet.SheetHeight,
            }).ToArray<JsonNode?>()),
        };

        var properties = new JsonObject();
        foreach (var key in map.Properties.Keys)
            properties[key] = map.Properties[key]?.ToString() ?? string.Empty;
        summary["properties"] = properties;
        return summary;
    }

    private static JsonObject SummarizeTexture(Texture2D texture, bool hashTexture)
    {
        var summary = new JsonObject
        {
            ["width"] = texture.Width,
            ["height"] = texture.Height,
        };

        if (hashTexture)
        {
            try { summary["content_hash"] = TextureHasher.ComputeHashHexPrefix(texture); }
            catch { }
        }

        return summary;
    }

    private static JsonObject SummarizeDictionary<T>(IDictionary<string, T> data, ContentAssetRequest req)
    {
        var limit = req.KeysLimit ?? 50;
        var summary = new JsonObject
        {
            ["count"] = data.Count,
        };

        if (req.IncludeKeys)
            summary["keys"] = new JsonArray(data.Keys.Take(limit).Select(k => (JsonNode?)k).ToArray());

        if (req.EntryKeys is { Length: > 0 })
        {
            var entries = new JsonObject();
            foreach (var key in req.EntryKeys)
            {
                if (data.TryGetValue(key, out var value))
                {
                    entries[key] = new JsonObject
                    {
                        ["exists"] = true,
                        ["value"] = SummarizeValue(value),
                    };
                }
                else
                {
                    entries[key] = new JsonObject { ["exists"] = false };
                }
            }
            summary["entries"] = entries;
        }

        return summary;
    }

    private static JsonNode? SummarizeValue(object? value)
    {
        if (value is null) return null;
        if (value is string s) return s;
        if (value is bool b) return b;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is float f) return f;
        if (value is double d) return d;
        if (value is decimal m) return (double)m;
        if (value is IEnumerable enumerable and not string)
        {
            var count = 0;
            foreach (var _ in enumerable) count++;
            return new JsonObject
            {
                ["runtime_type"] = value.GetType().FullName ?? value.GetType().Name,
                ["count"] = count,
            };
        }

        var obj = new JsonObject
        {
            ["runtime_type"] = value.GetType().FullName ?? value.GetType().Name,
            ["string_preview"] = value.ToString() is { Length: <= 160 } preview ? preview : value.ToString()?[..160],
        };
        return obj;
    }

    private static ContentAssetResult Found(string name, string kind, Type runtimeType, JsonObject summary)
        => new()
        {
            Name = name,
            Exists = true,
            Kind = kind,
            RuntimeType = runtimeType.FullName ?? runtimeType.Name,
            Summary = summary,
        };

    private static ContentAssetResult Missing(string name)
        => new()
        {
            Name = name,
            Exists = false,
            Kind = "missing",
            RuntimeType = string.Empty,
            Summary = new JsonObject(),
        };
}
```

- [ ] **Step 5: Run projector tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjectorTests
```

Expected: pass, except for any compile issue around `Map.DisplayWidth`/`DisplayHeight`. If xTile exposes width only through the first layer in this version, adjust `SummarizeMap` to use:

```csharp
var firstLayer = map.Layers.FirstOrDefault();
var width = firstLayer?.LayerWidth ?? 0;
var height = firstLayer?.LayerHeight ?? 0;
```

and rerun the same test command.

- [ ] **Step 6: Commit Task 2**

```bash
git add src/Harness/Assets/IContentAssetLoader.cs src/Harness/Assets/ContentAssetProjector.cs tests/Harness.Tests/ContentAssetProjectorTests.cs
git commit -m "feat: summarize runtime content assets"
```

## Task 3: Runtime Loader And RPC Handler

**Files:**
- Create: `src/Harness/Assets/SmapiContentAssetLoader.cs`
- Create: `src/Harness/Handlers/ContentAssetHandler.cs`
- Modify: `src/Harness/ModEntry.cs`
- Create: `tests/Harness.Tests/ContentAssetHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/Harness.Tests/ContentAssetHandlerTests.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ContentAssetHandlerTests
{
    [Fact]
    public void Handle_WithoutLoader_ThrowsGameStateInvalid()
    {
        ContentAssetHandler.Loader = null;
        var p = JsonDocument.Parse("{\"name\":\"Data/Locations\",\"asset_type\":\"data\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => ContentAssetHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("content loader", ex.Message);
    }

    [Fact]
    public void Handle_WithLoader_ReturnsProjectedAsset()
    {
        var loader = new FakeLoader();
        loader.Add("Data/Test", new System.Collections.Generic.Dictionary<string, string>
        {
            ["Alpha"] = "One",
        });
        ContentAssetHandler.Loader = loader;
        var p = JsonDocument.Parse("{\"name\":\"Data/Test\",\"asset_type\":\"data\",\"include_keys\":true}").RootElement;

        var result = ContentAssetHandler.Handle(p);

        Assert.True(result.GetProperty("exists").GetBoolean());
        Assert.Equal("data", result.GetProperty("kind").GetString());
        Assert.Equal(1, result.GetProperty("summary").GetProperty("count").GetInt32());
    }

    private sealed class FakeLoader : IContentAssetLoader
    {
        private readonly System.Collections.Generic.Dictionary<(System.Type Type, string Name), object> _assets = new();

        public void Add<T>(string name, T asset) where T : notnull
            => _assets[(typeof(T), name)] = asset;

        public bool TryLoad<T>(string name, out T? asset) where T : notnull
        {
            if (_assets.TryGetValue((typeof(T), name), out var value))
            {
                asset = (T)value;
                return true;
            }
            asset = default;
            return false;
        }
    }
}
```

- [ ] **Step 2: Run handler tests to verify they fail**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetHandlerTests
```

Expected: compile fails because `ContentAssetHandler` and `SmapiContentAssetLoader` do not exist.

- [ ] **Step 3: Add SMAPI loader**

Create `src/Harness/Assets/SmapiContentAssetLoader.cs`:

```csharp
using Microsoft.Xna.Framework.Content;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Assets;

public sealed class SmapiContentAssetLoader : IContentAssetLoader
{
    private readonly IGameContentHelper _content;

    public SmapiContentAssetLoader(IGameContentHelper content)
        => _content = content;

    public bool TryLoad<T>(string name, out T? asset) where T : notnull
    {
        try
        {
            var parsed = _content.ParseAssetName(name);
            if (!_content.DoesAssetExist<T>(parsed))
            {
                asset = default;
                return false;
            }

            asset = _content.Load<T>(parsed);
            return true;
        }
        catch (ContentLoadException)
        {
            asset = default;
            return false;
        }
        catch (ArgumentException)
        {
            asset = default;
            return false;
        }
        catch (InvalidCastException)
        {
            asset = default;
            return false;
        }
    }
}
```

- [ ] **Step 4: Add RPC handler**

Create `src/Harness/Handlers/ContentAssetHandler.cs`:

```csharp
using System.Text.Json;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>content.asset</c>. Runs on the game thread.</summary>
public static class ContentAssetHandler
{
    public const string Method = "content.asset";

    public static IContentAssetLoader? Loader { get; set; }

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var loader = Loader
            ?? throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid, "content.asset requires a content loader");
        var req = RpcParams.Required<ContentAssetRequest>(paramsElement);
        var result = ContentAssetProjector.Project(loader, req);
        return ProtocolJson.ToElement(result);
    }
}
```

- [ ] **Step 5: Register handler in ModEntry**

Modify `src/Harness/ModEntry.cs` near other handler registrations:

```csharp
        ContentAssetHandler.Loader = new Assets.SmapiContentAssetLoader(helper.GameContent);
        _rpc.Register(ContentAssetHandler.Method, p => ContentAssetHandler.Handle(p));
```

Update the final Monitor log RPC list to include `content.asset` in a new or existing sentence:

```text
Content: content.asset.
```

- [ ] **Step 6: Run harness tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter "ContentAssetHandlerTests|ContentAssetProjectorTests"
```

Expected: pass.

- [ ] **Step 7: Commit Task 3**

```bash
git add src/Harness/Assets/SmapiContentAssetLoader.cs src/Harness/Handlers/ContentAssetHandler.cs src/Harness/ModEntry.cs tests/Harness.Tests/ContentAssetHandlerTests.cs
git commit -m "feat: expose runtime content asset rpc"
```

## Task 4: Data Asset Type Coverage For Stardew 1.6

**Files:**
- Modify: `src/Harness/Assets/ContentAssetProjector.cs`
- Modify: `tests/Harness.Tests/ContentAssetProjectorTests.cs`

- [ ] **Step 1: Add a failing typed dictionary projection test**

Append to `tests/Harness.Tests/ContentAssetProjectorTests.cs`:

```csharp
private sealed class FakeLocationData
{
    public string DisplayName { get; set; } = "";
    public bool CanPlantHere { get; set; }
}

[Fact]
public void Project_DataDictionary_SummarizesObjectScalarProperties()
{
    var loader = new FakeLoader();
    loader.Add("Data/Locations", new Dictionary<string, FakeLocationData>
    {
        ["Custom_TownEast"] = new() { DisplayName = "Town East", CanPlantHere = false },
    });

    var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
    {
        Name = "Data/Locations",
        AssetType = "data",
        EntryKeys = new[] { "Custom_TownEast" },
    });

    var entry = result.Summary["entries"]!["Custom_TownEast"]!;
    Assert.True(entry["exists"]!.GetValue<bool>());
    Assert.Equal("Town East", entry["value"]!["display_name"]!.GetValue<string>());
    Assert.False(entry["value"]!["can_plant_here"]!.GetValue<bool>());
}
```

- [ ] **Step 2: Run projector tests to verify the typed dictionary case fails**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjectorTests
```

Expected: new test fails because `TryProjectData` only checks two concrete dictionary types.

- [ ] **Step 3: Generalize data dictionary detection**

Modify `ContentAssetProjector.TryProjectData` so it attempts known Stardew data types first and then uses reflection-compatible `IDictionary` projection.

Add this using:

```csharp
using StardewValley.GameData.Locations;
```

Replace `TryProjectData` with:

```csharp
private static ContentAssetResult? TryProjectData(IContentAssetLoader loader, ContentAssetRequest req)
{
    if (loader.TryLoad<Dictionary<string, string>>(req.Name, out var stringDict) && stringDict is not null)
        return Found(req.Name, "data", stringDict.GetType(), SummarizeStringKeyDictionary(stringDict, req));

    if (loader.TryLoad<Dictionary<string, object>>(req.Name, out var objectDict) && objectDict is not null)
        return Found(req.Name, "data", objectDict.GetType(), SummarizeStringKeyDictionary(objectDict, req));

    if (loader.TryLoad<Dictionary<string, LocationData>>(req.Name, out var locationDict) && locationDict is not null)
        return Found(req.Name, "data", locationDict.GetType(), SummarizeStringKeyDictionary(locationDict, req));

    return null;
}
```

Rename `SummarizeDictionary<T>` to `SummarizeStringKeyDictionary<T>`.

Enhance `SummarizeValue` object projection by reading public scalar properties and writing snake-case field names:

```csharp
foreach (var prop in value.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
{
    if (prop.GetIndexParameters().Length != 0)
        continue;

    object? propValue;
    try { propValue = prop.GetValue(value); }
    catch { continue; }

    if (propValue is string or bool or int or long or float or double or decimal)
        obj[ToSnakeCase(prop.Name)] = SummarizeValue(propValue);
}
```

Add helper:

```csharp
private static string ToSnakeCase(string value)
{
    if (string.IsNullOrEmpty(value)) return value;
    var chars = new List<char>(value.Length + 8);
    for (var i = 0; i < value.Length; i++)
    {
        var c = value[i];
        if (char.IsUpper(c))
        {
            if (i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        else
        {
            chars.Add(c);
        }
    }
    return new string(chars.ToArray());
}
```

- [ ] **Step 4: Run projector tests**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjectorTests
```

Expected: pass. If `LocationData` type name changed in the installed SDV version, remove the explicit `LocationData` attempt and keep the generic fake-loader tests passing; the live SVE scenario in Task 8 will identify the next concrete type to add.

- [ ] **Step 5: Commit Task 4**

```bash
git add src/Harness/Assets/ContentAssetProjector.cs tests/Harness.Tests/ContentAssetProjectorTests.cs
git commit -m "feat: summarize typed content data entries"
```

## Task 5: Runner `content.asset` Assertions

**Files:**
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Create: `tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs`

- [ ] **Step 1: Write failing runner tests**

Create `tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs`:

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

public class ScenarioRunnerContentAssetTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");

    [Fact]
    public async Task ContentAssetAssertion_EvaluatesContainsExpression()
    {
        var (cts, server, client, calls) = await StartFakeHarness(SocketPath(), """
        {
          "name": "Maps/Custom_TownEast",
          "exists": true,
          "kind": "map",
          "runtime_type": "xTile.Map",
          "summary": {
            "width": 90,
            "height": 64,
            "layers": [ { "name": "Back" }, { "name": "Buildings" } ]
          }
        }
        """);
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "content_asset_contains",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "content.asset",
                    Asset = "Maps/Custom_TownEast",
                    AssetType = "map",
                    Expr = "asset.layers contains name 'Back'",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.True(report.Passed);
        Assert.Equal(1, report.AssertionsPassed);
        Assert.Contains("content.asset", calls);
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ContentAssetAssertion_MissingAsset_FailsWithAssetName()
    {
        var (cts, server, client, _) = await StartFakeHarness(SocketPath(), """
        {
          "name": "Maps/Missing",
          "exists": false,
          "kind": "missing",
          "runtime_type": "",
          "summary": {}
        }
        """);
        using var _ = cts;
        using var __ = client;

        var runner = new ScenarioRunner(client);
        var spec = new ScenarioSpec
        {
            Name = "content_asset_missing",
            Assertions = new()
            {
                new ScenarioAssertion
                {
                    Type = "content.asset",
                    Asset = "Maps/Missing",
                    AssetType = "map",
                    Expr = "asset.width != 0",
                },
            },
        };

        var report = await runner.RunAsync(spec, cts.Token);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure => failure.Contains("Maps/Missing"));
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> StartFakeHarness(string socket, string contentAssetJson)
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
                    var result = req.Method switch
                    {
                        "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                        "content.asset" => JsonDocument.Parse(contentAssetJson).RootElement,
                        "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                        _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                    };
                    await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, result), tok);
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

- [ ] **Step 2: Run runner tests to verify they fail**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ScenarioRunnerContentAssetTests
```

Expected: tests fail because `content.asset` assertion type is not evaluated.

- [ ] **Step 3: Add content assertion evaluator**

Modify `src/Runner/Scenarios/ScenarioRunner.cs`.

In `EvaluateAssertionAsync`, add a case before `case "state":`:

```csharp
            case "content.asset":
            {
                var (passed, detail) = await EvaluateContentAssetAssertionAsync(a, ct);
                if (!passed) await TryCaptureAssertionFailureAsync(ct);
                return (passed, detail);
            }
```

Add helper methods near the state assertion logic:

```csharp
private async Task<(bool Passed, string? Detail)> EvaluateContentAssetAssertionAsync(
    ScenarioAssertion assertion,
    CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(assertion.Asset))
        return (false, "content.asset assertion requires asset");
    if (string.IsNullOrWhiteSpace(assertion.Expr))
        return (false, "content.asset assertion requires expr");

    var req = ProtocolJson.ToElement(new ContentAssetRequest
    {
        Name = assertion.Asset,
        AssetType = assertion.AssetType,
        IncludeKeys = assertion.IncludeKeys ?? false,
        KeysLimit = assertion.KeysLimit,
        EntryKeys = assertion.EntryKeys,
        HashTexture = assertion.HashTexture ?? false,
    });

    var resp = await _session.InvokeAsync("content.asset", req, ct);
    if (resp.Error is not null)
        return (false, resp.Error.Message);
    if (resp.Result is not { } root)
        return (false, "content.asset returned no result");
    if (!root.TryGetProperty("exists", out var exists) || exists.ValueKind != JsonValueKind.True)
        return (false, $"content asset '{assertion.Asset}' did not exist");
    if (!root.TryGetProperty("summary", out var summary))
        return (false, $"content asset '{assertion.Asset}' returned no summary");

    return EvaluateJsonExpression(
        assertion.Expr,
        rootName: "asset",
        root: summary,
        failurePrefix: $"content asset '{assertion.Asset}'");
}
```

Extract the state assertion expression code into a reusable helper to avoid duplicating the mini DSL. Minimum acceptable shape:

```csharp
private static (bool Passed, string? Detail) EvaluateJsonExpression(
    string expr,
    string rootName,
    JsonElement root,
    string failurePrefix)
```

It must support the same forms used by `state`:

- `<root>.<array> contains '<literal>'`
- `<root>.<array> contains <field> '<literal>'`
- `<root>.<path> == '<literal>'`
- `<root>.<path> != '<literal>'`
- integer and boolean literals
- array index tokens already supported by the state path walker

Then change the existing `case "state"` to use the helper after it invokes the relevant `state.*` RPC. Keep existing failure behavior and messages stable for current tests.

- [ ] **Step 4: Run runner tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter "ScenarioRunnerContentAssetTests|ScenarioRunnerDslTests"
```

Expected: pass.

- [ ] **Step 5: Commit Task 5**

```bash
git add src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs
git commit -m "feat: add content asset scenario assertions"
```

## Task 6: Docs And TODO Wiring

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`
- Modify: `README.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update RPC schema**

Add a `content.asset` section to `docs/rpc-schema.md` near the state/content-style query docs:

````markdown
### content.asset

Read-only runtime content query. Loads one asset through Stardew/SMAPI's live game
content pipeline, after Content Patcher and other content events have applied.

Request:

```json
{ "name": "Maps/Custom_TownEast", "asset_type": "map" }
```

Response:

```json
{
  "name": "Maps/Custom_TownEast",
  "exists": true,
  "kind": "map",
  "runtime_type": "xTile.Map",
  "summary": {
    "width": 90,
    "height": 64,
    "layers": [{ "name": "Back", "width": 90, "height": 64 }]
  }
}
```

Supported `asset_type` values: `map`, `texture`, `data`, `string`, and `unknown`.
Missing assets return `exists:false`; invalid request shapes return `InvalidParams`.
````

- [ ] **Step 2: Update DSL quickstart**

Add to `docs/dsl-quickstart.md` near state assertions:

````markdown
### Runtime Content Asset Assertions

Use `content.asset` assertions when a mod needs to prove final loaded content, not
just visible pixels:

```json
{
  "type": "content.asset",
  "asset": "Data/Locations",
  "asset_type": "data",
  "include_keys": true,
  "entry_keys": ["Custom_TownEast"],
  "expr": "asset.entries.Custom_TownEast.exists == true",
  "message": "SVE Town East should be present in Data/Locations"
}
```

This query is read-only and observes the runtime content cache after Content Patcher
has applied active patches.
````

- [ ] **Step 3: Update README**

Add one bullet to the mod-testing capability list in `README.md`:

```markdown
- Use `content.asset` assertions to inspect final runtime Content Patcher assets,
  including loaded maps, data dictionaries, strings, and textures.
```

- [ ] **Step 4: Update SVE capability TODO**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 3:

```markdown
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-3-runtime-content-assets.md`.
  - Active target: runtime `content.asset` query plus JSON scenario assertions for maps, data dictionaries, and textures.
```

- [ ] **Step 5: Run docs-adjacent tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ScenarioLoaderTests
```

Expected: pass.

- [ ] **Step 6: Commit Task 6**

```bash
git add docs/rpc-schema.md docs/dsl-quickstart.md README.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document runtime content asset assertions"
```

## Task 7: SVE Runtime Content Scenario

**Files:**
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/04-sve-content-assets-runtime.test.json`

- [ ] **Step 1: Create failing SVE scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/04-sve-content-assets-runtime.test.json`:

```json
{
  "name": "sve_content_assets_runtime",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "wait.ms", "args": { "ms": 1000 } },
    { "action": "freeze.begin", "args": {} },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "content.asset",
      "asset": "Maps/Custom_TownEast",
      "asset_type": "map",
      "expr": "asset.width != 0",
      "message": "SVE Town East map should load with nonzero width"
    },
    {
      "type": "content.asset",
      "asset": "Maps/Custom_TownEast",
      "asset_type": "map",
      "expr": "asset.layers contains name 'Back'",
      "message": "SVE Town East map should expose the Back layer"
    },
    {
      "type": "content.asset",
      "asset": "Data/Locations",
      "asset_type": "data",
      "include_keys": true,
      "entry_keys": ["Custom_TownEast"],
      "expr": "asset.entries.Custom_TownEast.exists == true",
      "message": "SVE Town East should be registered in Data/Locations"
    },
    {
      "type": "content.asset",
      "asset": "Mods/FlashShifter.StardewValleyExpandedCP/spring_GrampletonFields",
      "asset_type": "texture",
      "expr": "asset.width != 0",
      "message": "SVE Grampleton Fields spring texture should load with nonzero width"
    }
  ]
}
```

- [ ] **Step 2: Run the scenario and expect actionable failure if asset names differ**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --no-build tests/sdv/04-sve-content-assets-runtime.test.json
```

Expected after Tasks 1-6: either pass or fail with a specific missing asset. If it fails because one SVE asset name is inactive in the core-only setup, inspect SVE CP targets with:

```bash
rg -n '"Action"\s*:\s*"Load"|"Target"\s*:\s*"Mods/FlashShifter|Maps/Custom_TownEast|Data/Locations' .
```

Pick a default-active `Load` target from the SVE core CP pack, update only the scenario asset name, and rerun.

- [ ] **Step 3: Run SVE prior slices**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --no-build tests/sdv/01-sve-core-loads.test.json tests/sdv/02-sve-custom-locations-register.test.json tests/sdv/03-sve-event-observability-krobus.test.json tests/sdv/04-sve-content-assets-runtime.test.json
```

Expected: 4/4 pass.

- [ ] **Step 4: Commit SVE scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/04-sve-content-assets-runtime.test.json
git commit -m "test: add SVE runtime content asset scenario"
```

## Task 8: Final Verification And Slice Status

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run Frobby unit suites**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
dotnet test
```

Expected: all non-skipped tests pass.

- [ ] **Step 2: Run Starberg smoke**

Run from `/home/fintan/stardewRepos/stonks`:

```bash
./scripts/sdv-test --headless --no-build tests/sdv/01-starberg-terminal-open.test.json
```

Expected: pass. This proves the new RPC registration and runner changes did not break the existing Starberg path.

- [ ] **Step 3: Run SVE Slice 3 scenario**

Run from `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
./scripts/sdv-test --headless --no-build tests/sdv/04-sve-content-assets-runtime.test.json
```

Expected: pass.

- [ ] **Step 4: Mark Slice 3 implementation done**

Modify `SVE_FROBBY_CAPABILITY_TODO.md` Slice 3:

```markdown
- [x] Done: Slice 3, Content Patcher runtime asset coverage foundation.
  - SVE pressure: CP `Load` and `Edit*` actions for maps, strings, data assets, portraits, sprites, recolors, and config-gated patches.
  - Frobby goal: inspect loaded asset names and selected asset metadata, prove expected CP assets are available, and verify map/texture assets without relying only on full screenshots.
  - Design spec: `docs/superpowers/specs/2026-05-06-sve-slice-3-runtime-content-assets-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-06-sve-slice-3-runtime-content-assets.md`.
  - Done: `content.asset` runtime query, JSON `content.asset` assertions, docs, and SVE scenario 04.
  - Pending Slice 3 follow-up: Content Patcher manifest diagnostics for declared patch intent, asset invalidation/reload observations, and richer typed summaries for more Stardew data assets.
```

- [ ] **Step 5: Commit Frobby status update**

Run from `/home/fintan/stardewRepos/frobby/sdv-test-framework`:

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: mark SVE runtime content slice complete"
```

- [ ] **Step 6: Capture final git state**

Run:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short
git -C /home/fintan/stardewRepos/stonks status --short
```

Expected:

- Frobby clean.
- SVE clean.
- Starberg may still show unrelated `STARBERG_FEATURE_CANDIDATES.todo-completed.md`; do not modify it unless the user asks.

## Plan Self-Review

- Spec coverage: `content.asset` RPC is covered in Tasks 1-3, bounded summaries in Tasks 2 and 4, JSON assertion support in Task 5, docs in Task 6, SVE proof in Task 7, and cross-repo verification/status in Task 8.
- Type consistency: `ContentAssetRequest`, `ContentAssetResult`, `content.asset`, `AssetType`, `IncludeKeys`, `KeysLimit`, `EntryKeys`, and `HashTexture` use the same names across protocol, handler, runner, schema, docs, and scenario JSON.
- Mod neutrality: all Frobby code uses runtime asset names and typed summaries; SVE-specific names are limited to the SVE scenario and examples.
- TDD order: every code task starts with failing tests, then implementation, then focused verification, then commit.
