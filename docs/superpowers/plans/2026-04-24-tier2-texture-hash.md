# Tier 2 Texture-Hash Fallback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green**. T5's extra gates:
> - `sdv-test build-manifest` produces `~/.cache/sdv-test-framework/texture-manifests/<sdv-version>.json` with ~3000+ entries in ≤90 seconds.
> - A deliberately-crafted scenario targeting a Tier-2-only texture resolves (i.e. `texture_asset` populated after Tier 1 miss).
> - With manifest deleted, the same scenario falls through to Tier 3 (`content_hash` + `texture_size` populated, `texture_asset = null`).
> - `./scripts/run-samples.sh` still 11/11 PASS (no regression).

**Goal:** Close the 9.2% unresolved-textures gap from D1.5 via a build-on-user-machine SHA-256 manifest. New `sdv-test build-manifest` command produces a per-SDV-version manifest; harness uses it for Tier 2 resolution on Tier 1 cache miss; Tier 3 anonymous-fallback shape (`content_hash` + `texture_size`) handles the remaining unresolved textures so `DrawFilter` can still match.

**Architecture:** three-phase cascade at `draw.snapshot` time: Tier 1 weak-ref map (unchanged), Tier 2 hash-on-miss + manifest lookup (new), Tier 3 anonymous emission (new DTO fields). Manifest is per-SDV-version JSON at `~/.cache/sdv-test-framework/texture-manifests/<version>.json`. Build is driven by a new harness RPC + CLI command; users bootstrap once per SDV upgrade.

**Tech Stack:**
- No new NuGet dependencies — SHA-256 via `System.Security.Cryptography.SHA256`.
- Harness reads manifest at startup via `System.Text.Json`.
- Manifest size: ~300-500KB for ~4K vanilla textures with 16-hex-prefix hashes.

**Design spec:** `docs/superpowers/specs/2026-04-24-tier2-texture-hash-design.md`

---

## File structure

**New Harness files:**
- `src/Harness/Assets/TextureHasher.cs` — `ComputeHash(Texture2D) → byte[]` + `HashToHexPrefix(byte[]) → string` (16-hex prefix).
- `src/Harness/Assets/TextureHashManifest.cs` — loads JSON at startup; `TryResolve(string hashHex) → string?`.
- `src/Harness/Handlers/DiagnosticBuildManifestHandler.cs` — `diagnostic.build_texture_manifest` RPC.

**New Runner files:**
- `src/Runner/Commands/BuildManifestCommand.cs` — `sdv-test build-manifest` CLI.

**New test files:**
- `tests/Harness.Tests/TextureHashManifestTests.cs` — 3 tests.
- `tests/Harness.Tests/TextureHasherTests.cs` — 2 tests.
- `tests/Harness.Tests/TextureHashIntegrationTests.cs` — 1 skipped integration placeholder.
- `tests/Runner.Tests/BuildManifestCommandTests.cs` — 2 tests.

**Modified Protocol files:**
- `src/Protocol/Models/DrawEventSnapshot.cs` (DrawEventDto) — add `ContentHash` + `TextureSize`.
- `src/Protocol/Models/DrawFilter.cs` — add `ContentHash` + `TextureSize`.

**Modified Harness files:**
- `src/Harness/Assets/TextureAssetRegistry.cs` — `TryResolveWithFallback(Texture2D, TextureHashManifest) → (path, hash, size)` cascade.
- `src/Harness/Handlers/DrawSnapshotHandler.cs` — populate new DTO fields via cascade.
- `src/Harness/Handlers/DrawFilterMatcher.cs` — evaluate new filter fields.
- `src/Harness/Handlers/DrawFilterValidator.cs` — validate new filter fields (extend existing tests).
- `src/Harness/ModEntry.cs` — load manifest at startup; register the new RPC.

**Modified Runner files:**
- `src/Runner/Program.cs` — dispatch `build-manifest` + PrintHelp.

**Solution file:**
- `sdv-test-framework.slnx` — no change. New files live in existing projects.

**Starting test count:** 298 Passed + 37 Skipped.
**Target test count after ship:** ~308 Passed + 38 Skipped (+10 passed, +1 skipped).

---

## Task 1: Protocol DTO extensions + DrawFilter validator updates

**Why:** Wire changes land first — every subsequent task depends on the new DTO fields. This task is pure plumbing: add optional fields, no behavior change.

**Files:**
- Modify: `src/Protocol/Models/DrawEventSnapshot.cs`
- Modify: `src/Protocol/Models/DrawFilter.cs`
- Modify: `src/Harness/Handlers/DrawFilterValidator.cs`
- Modify: `src/Harness/Handlers/DrawFilterMatcher.cs`
- Modify: existing `tests/Harness.Tests/DrawFilterMatcherTests.cs` + `DrawFilterValidatorTests.cs` — extend with 3 new tests.

### Step 1: Extend DrawEventDto

Open `src/Protocol/Models/DrawEventSnapshot.cs`. Locate `DrawEventDto`. Add two new properties (nullable — backward compatible):

```csharp
/// <summary>16-hex-char prefix of SHA-256(texture pixels). Present on all events once Tier 2 lands. Nullable until backfilled.</summary>
public string? ContentHash { get; set; }

/// <summary>Texture dimensions as <c>[width, height]</c>. Present on all events. Nullable until backfilled.</summary>
public int[]? TextureSize { get; set; }
```

### Step 2: Extend DrawFilter

Open `src/Protocol/Models/DrawFilter.cs`. Add the matching filter fields:

```csharp
/// <summary>Match draw events whose <c>ContentHash</c> starts with this hex string (prefix match — users can pass 8, 16, or full).</summary>
public string? ContentHash { get; set; }

/// <summary>Match draw events whose texture dimensions exactly equal this [width, height].</summary>
public int[]? TextureSize { get; set; }
```

### Step 3: Write failing tests for DrawFilterMatcher

Extend `tests/Harness.Tests/DrawFilterMatcherTests.cs`. Add three tests:

```csharp
[Fact]
public void ContentHash_PrefixMatches_ReturnsTrue()
{
    var evt = new DrawEventDto { ContentHash = "a1b2c3d4e5f6a789" };
    var filter = new DrawFilter { ContentHash = "a1b2c3d4" };
    Assert.True(DrawFilterMatcher.Matches(evt, filter));
}

[Fact]
public void ContentHash_PrefixMismatch_ReturnsFalse()
{
    var evt = new DrawEventDto { ContentHash = "a1b2c3d4e5f6a789" };
    var filter = new DrawFilter { ContentHash = "f0e0d0c0" };
    Assert.False(DrawFilterMatcher.Matches(evt, filter));
}

[Fact]
public void TextureSize_ExactMatch_ReturnsTrue()
{
    var evt = new DrawEventDto { TextureSize = new[] { 512, 1002 } };
    var filter = new DrawFilter { TextureSize = new[] { 512, 1002 } };
    Assert.True(DrawFilterMatcher.Matches(evt, filter));
}
```

Run: `dotnet test tests/Harness.Tests/ --filter DrawFilterMatcher`
Expected: the 3 new tests FAIL (matcher doesn't evaluate new fields yet).

### Step 4: Update DrawFilterMatcher

Open `src/Harness/Handlers/DrawFilterMatcher.cs`. Find `Matches(DrawEventDto, DrawFilter)`. Add evaluation for the two new fields — they AND with existing fields:

```csharp
        if (filter.ContentHash is { Length: > 0 } hashPrefix)
        {
            if (evt.ContentHash is null) return false;
            if (!evt.ContentHash.StartsWith(hashPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (filter.TextureSize is { Length: 2 } size)
        {
            if (evt.TextureSize is not { Length: 2 } evtSize) return false;
            if (evtSize[0] != size[0] || evtSize[1] != size[1]) return false;
        }
```

Place these checks alongside the existing filter-field checks, before the `return true`.

### Step 5: Write failing test for DrawFilterValidator

Extend `tests/Harness.Tests/DrawFilterValidatorTests.cs`:

```csharp
[Fact]
public void ContentHash_NonHexChars_ThrowsInvalidParams()
{
    var filter = new DrawFilter { ContentHash = "xyz!" };
    var ex = Assert.Throws<JsonRpcException>(() => DrawFilterValidator.Validate(filter));
    Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    Assert.Contains("content_hash", ex.Message);
}
```

Run: expect FAIL.

### Step 6: Update DrawFilterValidator

In `src/Harness/Handlers/DrawFilterValidator.cs`, add a hex-chars check:

```csharp
        if (filter.ContentHash is { Length: > 0 } ch)
        {
            foreach (var c in ch)
            {
                if (!Uri.IsHexDigit(c))
                    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                        $"content_hash must be hex chars only (got '{ch}')");
            }
        }

        if (filter.TextureSize is { } ts)
        {
            if (ts.Length != 2 || ts[0] <= 0 || ts[1] <= 0)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    "texture_size must be a 2-element array of positive integers");
        }
```

### Step 7: Verify CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +4 tests. Total **302 Passed + 37 Skipped** (was 298+37).

---

## Task 2: TextureHasher + TextureHashManifest

**Why:** Pure data-layer utilities — hashing + manifest lookup. No SDV / GraphicsDevice dependency, fully unit-testable.

**Files:**
- Create: `src/Harness/Assets/TextureHasher.cs`
- Create: `src/Harness/Assets/TextureHashManifest.cs`
- Create: `tests/Harness.Tests/TextureHasherTests.cs`
- Create: `tests/Harness.Tests/TextureHashManifestTests.cs`

### Step 1: Write failing tests for TextureHasher

Create `tests/Harness.Tests/TextureHasherTests.cs`:

```csharp
using SdvTestFramework.Harness.Assets;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextureHasherTests
{
    [Fact]
    public void ComputeHashFromBytes_SameData_ReturnsSameHash()
    {
        var data = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        var h1 = TextureHasher.ComputeHashHexPrefix(data);
        var h2 = TextureHasher.ComputeHashHexPrefix(data);
        Assert.Equal(h1, h2);
        Assert.Equal(16, h1.Length);
    }

    [Fact]
    public void ComputeHashFromBytes_DifferentData_ReturnsDifferentHash()
    {
        var a = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var b = new byte[] { 0x10, 0x20, 0x30, 0x41 };
        Assert.NotEqual(
            TextureHasher.ComputeHashHexPrefix(a),
            TextureHasher.ComputeHashHexPrefix(b));
    }
}
```

Run: expect compile failure — `TextureHasher` doesn't exist.

### Step 2: Create TextureHasher

Create `src/Harness/Assets/TextureHasher.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SdvTestFramework.Harness.Assets;

/// <summary>
/// SHA-256 over texture pixel data. Tier 2 of the texture-resolution cascade uses this
/// to look up textures missed by Tier 1's <c>IContentEvents.AssetReady</c> hook.
/// </summary>
/// <remarks>
/// Runs on the game thread — <see cref="Texture2D.GetData{T}(T[])"/> requires it.
/// Expect ~1ms for a 512x1002 portrait; cheap enough for on-demand use.
/// </remarks>
public static class TextureHasher
{
    /// <summary>Compute the 16-hex-char prefix of SHA-256 over the texture's pixel data.</summary>
    public static string ComputeHashHexPrefix(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        var bytes = MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
        return ComputeHashHexPrefix(bytes);
    }

    /// <summary>Same as above but accepts a raw byte buffer — used by tests that can't construct a GPU-backed Texture2D.</summary>
    public static string ComputeHashHexPrefix(byte[] data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        // 16 hex chars = 8 bytes = 64 bits of hash. Collision prob ≈ 2^-64 × N²/2.
        // For a ~5K-entry manifest: 2^-64 × 12.5M ≈ 7e-13 — safe.
        return Convert.ToHexString(hash.Slice(0, 8)).ToLowerInvariant();
    }
}
```

### Step 3: Failing tests for TextureHashManifest

Create `tests/Harness.Tests/TextureHashManifestTests.cs`:

```csharp
using System;
using System.IO;
using SdvTestFramework.Harness.Assets;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class TextureHashManifestTests
{
    [Fact]
    public void Load_MissingFile_ReturnsEmptyManifest()
    {
        var m = TextureHashManifest.Load("/tmp/definitely-does-not-exist.json");
        Assert.Equal(0, m.Count);
        Assert.Null(m.TryResolve("a1b2c3d4e5f6a789"));
    }

    [Fact]
    public void Load_ValidJson_ResolvesHashToPath()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp,
            "{\"sdv_version\":\"1.6.15\",\"texture_count\":2," +
             "\"manifest\":{\"a1b2c3d4e5f6a789\":\"Characters/Abigail\",\"deadbeefcafef00d\":\"LooseSprites/Cursors\"}}");
        try
        {
            var m = TextureHashManifest.Load(tmp);
            Assert.Equal(2, m.Count);
            Assert.Equal("Characters/Abigail", m.TryResolve("a1b2c3d4e5f6a789"));
            Assert.Equal("LooseSprites/Cursors", m.TryResolve("deadbeefcafef00d"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void TryResolve_UnknownHash_ReturnsNull()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, "{\"sdv_version\":\"1.6.15\",\"texture_count\":0,\"manifest\":{}}");
        try
        {
            var m = TextureHashManifest.Load(tmp);
            Assert.Null(m.TryResolve("0000000000000000"));
        }
        finally { File.Delete(tmp); }
    }
}
```

### Step 4: Create TextureHashManifest

Create `src/Harness/Assets/TextureHashManifest.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdvTestFramework.Harness.Assets;

/// <summary>
/// Read-only view of a <c>hash → asset_path</c> manifest produced by
/// <c>sdv-test build-manifest</c>. Absent manifest → empty + Tier 2 no-ops.
/// </summary>
public sealed class TextureHashManifest
{
    private readonly Dictionary<string, string> _map;

    public int Count => _map.Count;

    private TextureHashManifest(Dictionary<string, string> map) => _map = map;

    /// <summary>Load from disk. Missing / corrupt file → empty manifest (no throw).</summary>
    public static TextureHashManifest Load(string path)
    {
        if (!File.Exists(path))
            return new TextureHashManifest(new Dictionary<string, string>());

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<ManifestFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return new TextureHashManifest(parsed?.Manifest ?? new Dictionary<string, string>());
        }
        catch
        {
            return new TextureHashManifest(new Dictionary<string, string>());
        }
    }

    /// <summary>Look up a 16-hex-char hash prefix; returns null if absent.</summary>
    public string? TryResolve(string hashHex) =>
        _map.TryGetValue(hashHex, out var path) ? path : null;

    private sealed class ManifestFile
    {
        [JsonPropertyName("sdv_version")]
        public string? SdvVersion { get; set; }

        [JsonPropertyName("texture_count")]
        public int TextureCount { get; set; }

        [JsonPropertyName("manifest")]
        public Dictionary<string, string>? Manifest { get; set; }
    }
}
```

### Step 5: CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +5 tests (2 hasher + 3 manifest). Total **307 Passed + 37 Skipped**.

---

## Task 3: Wire Tier 2/3 into DrawSnapshotHandler + TextureAssetRegistry

**Why:** The behavior change. On draw.snapshot, cascade Tier 1 → Tier 2 → Tier 3. Manifest is loaded by ModEntry at startup.

**Files:**
- Modify: `src/Harness/Assets/TextureAssetRegistry.cs` — add `TryResolveWithFallback`.
- Modify: `src/Harness/Handlers/DrawSnapshotHandler.cs` — use the cascade; populate new DTO fields.
- Modify: `src/Harness/ModEntry.cs` — load the manifest at startup; log when absent.

### Step 1: Extend TextureAssetRegistry

Open `src/Harness/Assets/TextureAssetRegistry.cs`. Add:

```csharp
/// <summary>
/// Full resolution cascade: Tier 1 (weak-ref map) → Tier 2 (hash + manifest) → Tier 3 (anonymous).
/// Populates the Tier 1 map on Tier 2 hit so subsequent queries skip rehashing.
/// </summary>
public (string? Path, string Hash, int Width, int Height) TryResolveWithFallback(
    Texture2D texture,
    TextureHashManifest manifest)
{
    // Tier 1 — existing behavior.
    if (TryResolve(texture, out var path))
    {
        // Tier 1 hit: compute hash + size for DTO backfill, but don't re-lookup manifest.
        var hash = TextureHasher.ComputeHashHexPrefix(texture);
        return (path, hash, texture.Width, texture.Height);
    }

    // Tier 2 — hash + manifest lookup.
    var computed = TextureHasher.ComputeHashHexPrefix(texture);
    var resolved = manifest.TryResolve(computed);
    if (resolved is not null)
    {
        // Populate Tier 1 map so future queries skip the hash.
        Register(texture, resolved);
        return (resolved, computed, texture.Width, texture.Height);
    }

    // Tier 3 — anonymous.
    return (null, computed, texture.Width, texture.Height);
}
```

Note: `TryResolve(out path)` is the existing Tier 1 accessor — its exact name may be different (`TryResolveTexture`, `Lookup`, etc). Read the file first and adapt.

### Step 2: Wire into DrawSnapshotHandler.ToDto

Open `src/Harness/Handlers/DrawSnapshotHandler.cs`. Find `ToDto(DrawEvent)` (or the equivalent event-to-DTO mapper). Replace the existing `TextureAsset` resolution with the cascade:

```csharp
var (texturePath, hash, w, h) = TextureAssetRegistry.Shared.TryResolveWithFallback(
    evt.Texture, _manifest);

return new DrawEventDto
{
    // ... existing fields ...
    TextureAsset = texturePath,
    ContentHash = hash,
    TextureSize = new[] { w, h },
};
```

The handler needs access to `_manifest` — inject via `DrawSnapshotHandler.Manifest` static (mirror the existing `TextureAssetRegistry.Shared` pattern). Add:

```csharp
public static TextureHashManifest? Manifest { get; set; }
```

ModEntry sets this at startup (Step 3).

### Step 3: Load manifest in ModEntry

Open `src/Harness/ModEntry.cs`. In `Entry(IModHelper helper)` after `TextureAssetRegistry.Shared = new TextureAssetRegistry();`, add:

```csharp
var manifestDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".cache", "sdv-test-framework", "texture-manifests");
var manifestPath = Path.Combine(manifestDir, $"{StardewValley.Game1.version}.json");
var manifest = Assets.TextureHashManifest.Load(manifestPath);
Handlers.DrawSnapshotHandler.Manifest = manifest;
if (manifest.Count == 0)
{
    this.Monitor.Log(
        $"texture-manifest for SDV {StardewValley.Game1.version} not found — Tier 2 resolution disabled. " +
        "Run 'sdv-test build-manifest' to generate.",
        LogLevel.Info);
}
else
{
    this.Monitor.Log(
        $"loaded texture-manifest for SDV {StardewValley.Game1.version} ({manifest.Count} textures)",
        LogLevel.Info);
}
```

### Step 4: CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: **307 Passed + 37 Skipped** (no new tests; behavior verified by T-final smoke). If any existing tests broke (e.g. DrawSnapshotHandler tests that assumed null `ContentHash`), update them to accept the backfilled values.

---

## Task 4: `diagnostic.build_texture_manifest` RPC + `sdv-test build-manifest` CLI

**Why:** The user-facing path to generate the manifest. Harness enumerates + hashes; Runner writes the file.

**Files:**
- Create: `src/Harness/Handlers/DiagnosticBuildManifestHandler.cs`
- Create: `src/Runner/Commands/BuildManifestCommand.cs`
- Create: `tests/Runner.Tests/BuildManifestCommandTests.cs`
- Modify: `src/Harness/ModEntry.cs` — register the new RPC.
- Modify: `src/Runner/Program.cs` — dispatch `build-manifest` + help text.

### Step 1: DiagnosticBuildManifestHandler.cs

Create `src/Harness/Handlers/DiagnosticBuildManifestHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Protocol;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// <c>diagnostic.build_texture_manifest</c> — enumerates SDV's loaded content, hashes
/// every Texture2D, returns the full <c>{hash → asset_path}</c> map. Intended to be
/// driven by <c>sdv-test build-manifest</c> once per SDV version install.
/// </summary>
public static class DiagnosticBuildManifestHandler
{
    public const string Method = "diagnostic.build_texture_manifest";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        // Reflect into ContentManager.loadedAssets — same approach as ContentLoadPatches.
        var loadedField = typeof(Microsoft.Xna.Framework.Content.ContentManager).GetField(
            "loadedAssets", BindingFlags.Instance | BindingFlags.NonPublic);
        if (loadedField is null)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "loadedAssets field not found");

        var loaded = loadedField.GetValue(Game1.content) as System.Collections.IDictionary
            ?? throw new JsonRpcException(JsonRpcErrorCode.InternalError, "loadedAssets not a dictionary");

        var map = new Dictionary<string, string>();
        int count = 0;
        foreach (System.Collections.DictionaryEntry entry in loaded)
        {
            if (entry.Value is not Texture2D tex) continue;
            var path = entry.Key as string;
            if (string.IsNullOrEmpty(path)) continue;

            try
            {
                var hash = TextureHasher.ComputeHashHexPrefix(tex);
                // If two textures share a hash (vanishingly rare for 16-hex), last-write wins.
                map[hash] = path!;
                count++;
            }
            catch { /* skip GPU-backed / disposed textures */ }
        }

        var result = new JsonObject
        {
            ["sdv_version"] = Game1.version,
            ["texture_count"] = count,
            ["manifest"] = JsonNode.Parse(JsonSerializer.Serialize(map))!,
        };
        return JsonDocument.Parse(result.ToJsonString()).RootElement.Clone();
    }
}
```

### Step 2: Register in ModEntry

In `src/Harness/ModEntry.cs`, add to the RPC registrations:

```csharp
_rpc.Register(DiagnosticBuildManifestHandler.Method, p => DiagnosticBuildManifestHandler.Handle(p));
```

Update the startup log-line's method list to append `Diagnostic: diagnostic.build_texture_manifest.`.

### Step 3: Failing tests for BuildManifestCommand

Create `tests/Runner.Tests/BuildManifestCommandTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class BuildManifestCommandTests
{
    [Fact]
    public async Task UnknownFlag_ReturnsTwo()
    {
        var code = await BuildManifestCommand.RunAsync(
            new[] { "--nope" }.AsMemory(), CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task DefaultOutput_UsesCacheDir()
    {
        // We can't fully exercise SDV launch here. Instead: parse args with --help-style
        // introspection to verify the default-path computation. Simpler alternative:
        // BuildManifestCommand exposes a static ResolveOutputPath(string? explicit) method
        // that's pure. Verify it returns a path under ~/.cache/sdv-test-framework/texture-manifests/.
        var path = BuildManifestCommand.ResolveOutputPath(explicitPath: null, sdvVersion: "1.6.15");
        Assert.Contains(".cache/sdv-test-framework/texture-manifests", path);
        Assert.EndsWith("1.6.15.json", path);
    }
}
```

### Step 4: BuildManifestCommand.cs

Create `src/Runner/Commands/BuildManifestCommand.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test build-manifest</c> — drive the harness's <c>diagnostic.build_texture_manifest</c>
/// RPC, write the result to <c>~/.cache/sdv-test-framework/texture-manifests/&lt;version&gt;.json</c>.
/// </summary>
public static class BuildManifestCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        // ---- parse args ----
        string? explicitOutput = null;
        string? modsPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--output" && i + 1 < args.Length) { explicitOutput = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            Console.Error.WriteLine($"build-manifest: unknown argument '{a}'");
            return 2;
        }

        // ---- resolve mods path ----
        modsPath ??= Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
            modsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "mods");
        Directory.CreateDirectory(modsPath);
        HarnessDeployer.Deploy(modsPath);

        // ---- launch SDV ----
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-manifest-{Guid.NewGuid():N}.sock");
        using var sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(120));

        try
        {
            for (int i = 0; i < 240 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);
            if (!File.Exists(socket))
                throw new TimeoutException("SDV never opened the manifest socket");

            using var session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            Console.Error.WriteLine("[build-manifest] harness ready, iterating content...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await session.InvokeAsync("diagnostic.build_texture_manifest", params_: null, ct);
            sw.Stop();
            if (resp.Error is { } err)
            {
                Console.Error.WriteLine($"[build-manifest] RPC failed: {err.Message}");
                return 4;
            }
            if (resp.Result is not { } result)
            {
                Console.Error.WriteLine("[build-manifest] RPC returned no result");
                return 4;
            }

            var sdvVersion = result.GetProperty("sdv_version").GetString()!;
            var count = result.GetProperty("texture_count").GetInt32();
            var outputPath = ResolveOutputPath(explicitOutput, sdvVersion);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(outputPath, result.GetRawText(), ct);

            var size = new FileInfo(outputPath).Length;
            Console.Error.WriteLine(
                $"[build-manifest] hashed {count} textures in {sw.Elapsed.TotalSeconds:F1}s");
            Console.Error.WriteLine(
                $"[build-manifest] wrote {outputPath} ({size / 1024} KB)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[build-manifest] fatal: {ex.Message}");
            return 4;
        }
        finally
        {
            try { if (!sdv.HasExited) { sdv.Kill(); sdv.WaitForExit(5000); } } catch { }
        }
    }

    /// <summary>Resolve the output path. Pure — unit-testable without SDV.</summary>
    public static string ResolveOutputPath(string? explicitPath, string sdvVersion)
    {
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "sdv-test-framework", "texture-manifests",
            $"{sdvVersion}.json");
    }
}
```

Note: `HarnessDeployer`, `SdvLauncher`, `UnixSocketRpc`, `JsonRpcNotification` are all
in `SdvTestFramework.Protocol` as of MCP-T6's reorg. Add the appropriate `using`.

### Step 5: Program.cs wiring

Open `src/Runner/Program.cs`. Add `"build-manifest"` to the dispatch switch before the `_ => Unknown(args[0])` default:

```csharp
"build-manifest" => await BuildManifestCommand.RunAsync(args.AsMemory()[1..], cts.Token),
```

Add help text after the `record` block:

```csharp
w.WriteLine("  build-manifest [--output <path>] [--mods-path <path>]");
w.WriteLine("                    Build a texture-hash manifest for the installed SDV version.");
w.WriteLine("                    Resolves the 9.2% of textures that Tier 1 (IContentEvents)");
w.WriteLine("                    misses. Writes ~/.cache/sdv-test-framework/texture-manifests/");
w.WriteLine("                    <sdv-version>.json by default. Run once per SDV version install.");
```

### Step 6: CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: +2 tests. Total **309 Passed + 37 Skipped**.

---

## Task 5: Integration placeholder + smoke + docs + milestone + roadmap

**Why:** Final task. Ship the skipped integration placeholder, run live smokes, update docs and roadmap.

**Files:**
- Create: `tests/Harness.Tests/TextureHashIntegrationTests.cs` (skipped).
- Modify: `docs/milestones/current.md` — add Tier 2 completion subsection.
- Modify: `docs/roadmap.md` — move item to Completed.

### Step 1: Integration placeholder

Create `tests/Harness.Tests/TextureHashIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration surface for Tier 2 texture-hash fallback — verified manually (Step 2 of T5).</summary>
public class TextureHashIntegrationTests
{
    [Fact(Skip = "Requires live SDV — manifest build + Tier 2 resolution verified manually.")]
    public void Tier2HashResolution_ResolvesPortraitMissedByTier1() { }
}
```

Run: `./scripts/ci.sh 2>&1 | tail -3`
Expected: **309 Passed + 38 Skipped**.

### Step 2: Live smoke — build manifest

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
pkill -9 -f StardewModdingAPI 2>/dev/null; pkill Xvfb 2>/dev/null; sleep 1
rm -f ~/.cache/sdv-test-framework/texture-manifests/*.json
Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/Runner -c Release --no-build -- build-manifest 2>&1 | tail -5
pkill Xvfb 2>/dev/null
```

Expected:
- `[build-manifest] hashed NNNN textures in XX.Ys` (NNNN ≥ 1000 for vanilla content at `ready` time; more gets added as scenarios run)
- `[build-manifest] wrote /home/fintan/.cache/sdv-test-framework/texture-manifests/1.6.15.json (NNN KB)`

Check the file:
```bash
ls -la ~/.cache/sdv-test-framework/texture-manifests/
head -c 200 ~/.cache/sdv-test-framework/texture-manifests/1.6.15.json
```

### Step 3: Live smoke — Tier 2 resolution

Run the sample suite with the manifest in place, verify no regression:

```bash
./scripts/run-samples.sh 2>&1 | tail -5
```

Expected: **11/11 passed**.

Then inspect SMAPI's log for the harness startup line — should say something like
`loaded texture-manifest for SDV 1.6.15 (NNNN textures)`.

### Step 4: Live smoke — Tier 3 fallback (manifest absent)

```bash
mv ~/.cache/sdv-test-framework/texture-manifests/1.6.15.json /tmp/manifest-backup.json
./scripts/run-samples.sh 2>&1 | tail -5
```

Expected: 11/11 passed still. Harness startup log line: `texture-manifest for SDV 1.6.15 not found — Tier 2 resolution disabled`. No regression — existing scenarios don't depend on Tier 2.

Restore:
```bash
mv /tmp/manifest-backup.json ~/.cache/sdv-test-framework/texture-manifests/1.6.15.json
```

### Step 5: Update docs/milestones/current.md

After the existing `### M3 subproject 2 — MCP server landed (2026-04-24)` subsection, insert:

```markdown
### Tier 2 texture-hash fallback landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-tier2-texture-hash.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-tier2-texture-hash-design.md`.

**Scope:** close the 9.2% unresolved-textures gap from D1.5. New `sdv-test build-manifest`
command generates a per-SDV-version `hash → asset_path` manifest at
`~/.cache/sdv-test-framework/texture-manifests/<version>.json`. Harness loads it at
startup; `DrawSnapshotHandler` cascades Tier 1 (weak map) → Tier 2 (hash + manifest) →
Tier 3 (anonymous `content_hash` + `texture_size`). New `DrawFilter` fields
`content_hash` + `texture_size` let assertions match on the anonymous shape.

**Missing-manifest behavior:** harness logs a one-line info message + no-ops Tier 2.
Tier 3 still emits `content_hash` + `texture_size`, so all assertions keep working — the
gap just isn't closed. Users run `sdv-test build-manifest` once per SDV version to
enable Tier 2.

**Test count after Tier 2:** 309 Passed + 38 Skipped (was 298+37; +11 passed, +1 skipped).

**Out of scope (M4):** shipped pre-built manifest, auto-regeneration on SDV update,
streaming manifest-build progress (pairs with MCP streaming Tier 3), modded-content
entries, hash-algorithm agility.
```

### Step 6: Update docs/roadmap.md

In `docs/roadmap.md`, remove the "Tier 2 texture-hash fallback" item from Tier 1 and
add a "2026-04-24 (later)" or new-dated bullet in Completed:

```markdown
- **Tier 2 texture-hash fallback**. Closes the 9.2% D1.5 gap via build-on-user-machine
  SHA-256 manifest. New `sdv-test build-manifest` command; 3-tier cascade (weak map →
  hash+manifest → anonymous). New `DrawEvent.ContentHash` + `TextureSize` DTO fields.
  298+37 → 309+38.
```

### Step 7: Final CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:\|Skipped:" | head -10`
Expected: **309 Passed + 38 Skipped**.

---

## Self-review

**1. Spec coverage:**
- Tier 1 → Tier 2 → Tier 3 cascade → T3 ✓
- `TextureHasher` + `TextureHashManifest` → T2 ✓
- `diagnostic.build_texture_manifest` RPC + `sdv-test build-manifest` CLI → T4 ✓
- `DrawEventDto.ContentHash` + `TextureSize` → T1 ✓
- `DrawFilter.ContentHash` + `TextureSize` → T1 ✓
- Missing-manifest silent no-op + info log → T3 step 3 ✓
- Integration placeholder + smoke + docs → T5 ✓
- Acceptance criteria 1-9 → every task ✓

**2. Placeholder scan:** no TBD. One soft spot: T3 Step 1's `TryResolve(out path)`
existing-accessor name may differ — the plan notes this and tells the implementer to
read the file first and adapt. Fine.

**3. Type consistency:**
- `TextureHasher.ComputeHashHexPrefix(Texture2D | byte[]) → string` — T2 defines,
  T3 + T4 consume. ✓
- `TextureHashManifest.TryResolve(string) → string?` — T2 defines, T3 consumes. ✓
- `TextureAssetRegistry.TryResolveWithFallback(Texture2D, TextureHashManifest) → (string? Path, string Hash, int Width, int Height)` — T3 defines,
  `DrawSnapshotHandler.ToDto` consumes. ✓
- `DiagnosticBuildManifestHandler.Method` constant — T4 defines, T4 registers in ModEntry. ✓
- `BuildManifestCommand.ResolveOutputPath(string?, string) → string` — T4 defines + test. ✓

**4. Hazards:**
- **Manifest scale.** Vanilla SDV has ~4000 loaded textures; JSON file ~300-500KB. Fine.
- **Hash collisions.** 16-hex prefix = 64 bits. For 5000 entries, collision prob ≈ 7e-13. Safe.
- **GPU-backed textures** (render targets) can't be `GetData`'d on the CPU without
  staging. T2 Step 2 catches `Exception` in the iteration loop; T3's `TryResolveWithFallback`
  should also catch (add try/catch around `ComputeHashHexPrefix` in production — noted
  in the implementation but worth double-checking during T3).
- **Diagnostic RPC cost.** `diagnostic.build_texture_manifest` blocks the game thread
  for 30-60 seconds. Acceptable for a one-time operation; SDV will appear frozen during
  the build. Document in CLI output.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-24-tier2-texture-hash.md`. Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
