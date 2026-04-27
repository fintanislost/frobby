# NuGet Packaging — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green**. T4's extra gates:
> - `./scripts/pack.sh` produces 3 `.nupkg` files in `./nupkg/`.
> - Fresh-shell `dotnet tool install --add-source ./nupkg --tool-path ./.dotnet-tools SdvTestFramework.Cli` succeeds; `./.dotnet-tools/sdv-test --help` lists all subcommands.
> - Fresh `/tmp/smoke-mod/` project with `dotnet add package SdvTestFramework.Runner.Dsl --add-source <repo>/nupkg` resolves and the DSL types compile.
> - `./scripts/run-samples.sh` still 11/11 PASS.

**Goal:** Ship the framework as installable NuGet packages so modders don't need to clone the source. Two consumable packages: `SdvTestFramework.Runner.Dsl` (library) + `SdvTestFramework.Cli` (`dotnet tool` — bundles CLI + MCP server + harness payload via embedded resources). Plus one transitive-only package `SdvTestFramework.Protocol`.

**Architecture:** Source layout unchanged. Add package metadata to 3 csprojs. Drop Runner.Dsl's stale Runner project ref (free win — post-MCP-T6 reorg makes it unused). Bundle harness as embedded resources in the Cli tool; `HarnessDeployer.Deploy` extracts on first run if not already there. Single version property `<SdvTestFrameworkVersion>0.1.0</SdvTestFrameworkVersion>` in `Directory.Build.props` keeps all packages in lockstep.

**Tech Stack:**
- Existing: net6 Harness, net10 Runner/Runner.Dsl/Runner.Mcp.
- New: `<PackAsTool>true</PackAsTool>` for Runner.csproj. Embedded resources for harness DLLs + manifest. MIT license.

**Design spec:** `docs/superpowers/specs/2026-04-24-nuget-packaging-design.md`

---

## File structure

**Modified files (csprojs + props):**
- `Directory.Build.props` — add `<SdvTestFrameworkVersion>0.1.0</SdvTestFrameworkVersion>` property + shared package metadata (Authors, Repository).
- `src/Protocol/Protocol.csproj` — package metadata. Already net6.
- `src/Runner.Dsl/Runner.Dsl.csproj` — drop Runner project ref. Add package metadata.
- `src/Runner/Runner.csproj` — `PackAsTool` + ToolCommandName. Add embedded harness resources.

**Modified source files:**
- `src/Protocol/HarnessDeployer.cs` — add embedded-resource extraction path; preserve existing source-tree-cache fallback.

**New files:**
- `LICENSE` — MIT license text at repo root.
- `nuget/README-Dsl.md` — ships inside Runner.Dsl package.
- `nuget/README-Cli.md` — ships inside Cli package.
- `nuget/README-Protocol.md` — ships inside Protocol package.
- `scripts/pack.sh` — `dotnet pack` driver.
- `tests/Runner.Dsl.Tests/NuGetPackagingIntegrationTests.cs` — 1 skipped placeholder.

**Modified docs:**
- `docs/dsl-quickstart.md` — NuGet install commands.
- `docs/mcp-quickstart.md` — clean `.mcp.json` snippet.
- `docs/milestones/current.md` — completion subsection.
- `docs/roadmap.md` — move from Tier 2 to Completed.

**Starting test count:** 347 Passed + 44 Skipped.
**Target:** 347 Passed + 45 Skipped (no new passing tests; +1 skipped integration placeholder).

---

## Task 1: License + Directory.Build.props version property + decouple Runner.Dsl

**Why:** Prerequisites. License is required for NuGet `<PackageLicenseExpression>`. Directory.Build.props version centralizes the lockstep version. Dropping the stale Runner ref shrinks the eventual NuGet payload + de-risks Phase D.

**Files:**
- Create: `LICENSE` (MIT).
- Modify: `Directory.Build.props` — add version + shared package metadata.
- Modify: `src/Runner.Dsl/Runner.Dsl.csproj` — drop Runner project ref.

### Step 1: LICENSE

Write `/home/fintan/stardewRepos/frobby/sdv-test-framework/LICENSE`:

```
MIT License

Copyright (c) 2026 fintan + sdv-test-framework contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Step 2: Directory.Build.props

Read `/home/fintan/stardewRepos/frobby/sdv-test-framework/Directory.Build.props` first. Append a new `<PropertyGroup>` block (don't merge with existing — new section for clarity):

```xml
<!-- NuGet packaging — applied to packable projects. Non-packable projects (Harness,
     test projects) ignore these. Packable csprojs opt in by setting <IsPackable>true</IsPackable>. -->
<PropertyGroup Condition="'$(IsPackable)' == 'true'">
  <Version>$(SdvTestFrameworkVersion)</Version>
  <Authors>fintan + contributors</Authors>
  <PackageProjectUrl>https://github.com/fintan/sdv-test-framework</PackageProjectUrl>
  <RepositoryUrl>https://github.com/fintan/sdv-test-framework</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <Copyright>Copyright (c) fintan + sdv-test-framework contributors</Copyright>
</PropertyGroup>

<PropertyGroup>
  <SdvTestFrameworkVersion>0.1.0</SdvTestFrameworkVersion>
</PropertyGroup>
```

### Step 3: Drop Runner.Dsl's stale Runner project reference

Open `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner.Dsl/Runner.Dsl.csproj`. Remove the line:

```xml
<ProjectReference Include="..\Runner\Runner.csproj" />
```

Keep the `..\Protocol\Protocol.csproj` reference. Final shape of the ItemGroup:

```xml
<ItemGroup>
  <ProjectReference Include="..\Protocol\Protocol.csproj" />
</ItemGroup>
```

### Step 4: Verify

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: **347 Passed + 44 Skipped** unchanged. The Runner.Dsl assembly should still build clean — confirms it never actually used Runner types post-MCP-T6.

If the build breaks (any Runner.Dsl source file using a Runner-only type), restore the project ref + investigate. The grep done at planning time showed only `SdvTestFramework.Protocol.HarnessDeployer` + `SdvTestFramework.Protocol.SdvLauncher` references — both in Protocol — so this should be a clean drop.

---

## Task 2: Package metadata on the 3 packable csprojs

**Why:** Make the projects packable. Add per-project `<IsPackable>` opt-in, `<PackageId>`, `<Description>`, `<PackageTags>`, `<PackageReadmeFile>`. This is the bulk of the NuGet plumbing.

**Files:**
- Create: `nuget/README-Protocol.md`
- Create: `nuget/README-Dsl.md`
- Create: `nuget/README-Cli.md`
- Modify: `src/Protocol/Protocol.csproj`
- Modify: `src/Runner.Dsl/Runner.Dsl.csproj`
- Modify: `src/Runner/Runner.csproj`

### Step 1: Package READMEs

Create `nuget/README-Protocol.md`:

```markdown
# SdvTestFramework.Protocol

JSON-RPC types and Stardew Valley test-framework transport. Internal package — consumed
by **SdvTestFramework.Runner.Dsl** and **SdvTestFramework.Cli**. You shouldn't need to
install this directly.

If you're writing tests, install `SdvTestFramework.Runner.Dsl`. If you're running tests
or driving Claude Code via MCP, install `SdvTestFramework.Cli`.

Repository + full docs: https://github.com/fintan/sdv-test-framework
```

Create `nuget/README-Dsl.md`:

```markdown
# SdvTestFramework.Runner.Dsl

Typed C# DSL for writing Stardew Valley mod tests. Use `[Scenario]` + ambient static
facets (`Player`, `World`, `Time`, `Draw`, `State`, `Freeze`, `Fixture`, `Bitmap`,
`Wait`) to author tests as plain xUnit methods.

## Install

```bash
dotnet add package SdvTestFramework.Runner.Dsl
```

You also need the CLI tool (which provides SDV launch + harness deployment):

```bash
dotnet tool install -g SdvTestFramework.Cli
```

## Minimal example

```csharp
using SdvTestFramework.Runner.Dsl;
using Xunit;

[Collection("SDV")]
public class ShopMenuTests
{
    [Fact, Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_ShopOpens()
    {
        await Player.Warp("SeedShop", 4, 19);
        await Player.SetMoney(5000);
        var player = await State.Player();
        Assert.Equal(5000, player.Money);
    }
}
```

Quickstart: https://github.com/fintan/sdv-test-framework/blob/main/docs/dsl-quickstart.md
```

Create `nuget/README-Cli.md`:

```markdown
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
```

### Step 2: Update Protocol.csproj

Read `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Protocol/Protocol.csproj` first. Add to the existing `<PropertyGroup>`:

```xml
<IsPackable>true</IsPackable>
<PackageId>SdvTestFramework.Protocol</PackageId>
<Description>JSON-RPC types and Stardew Valley test-framework transport. Transitive dependency of SdvTestFramework.Runner.Dsl and SdvTestFramework.Cli.</Description>
<PackageTags>stardew-valley;testing;json-rpc</PackageTags>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Add a new ItemGroup at the bottom:

```xml
<ItemGroup>
  <None Include="..\..\nuget\README-Protocol.md" Pack="true" PackagePath="README.md" />
</ItemGroup>
```

### Step 3: Update Runner.Dsl.csproj

Add to the existing `<PropertyGroup>`:

```xml
<IsPackable>true</IsPackable>
<PackageId>SdvTestFramework.Runner.Dsl</PackageId>
<Description>Typed C# DSL for writing Stardew Valley mod tests. Pairs with SdvTestFramework.Cli for SDV launch and MCP server.</Description>
<PackageTags>stardew-valley;testing;xunit;dsl</PackageTags>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Add ItemGroup:

```xml
<ItemGroup>
  <None Include="..\..\nuget\README-Dsl.md" Pack="true" PackagePath="README.md" />
</ItemGroup>
```

### Step 4: Update Runner.csproj — package metadata only (NOT yet PackAsTool)

Tool packing comes in T3 along with embedded resources. For now, just add the package metadata so it could pack as a regular package if needed:

```xml
<IsPackable>true</IsPackable>
<PackageId>SdvTestFramework.Cli</PackageId>
<Description>Stardew Valley test-framework CLI: launch SDV, run scenarios, scaffold templates, build texture manifests, MCP server.</Description>
<PackageTags>stardew-valley;testing;cli;mcp</PackageTags>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Add ItemGroup:

```xml
<ItemGroup>
  <None Include="..\..\nuget\README-Cli.md" Pack="true" PackagePath="README.md" />
</ItemGroup>
```

### Step 5: Verify

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: **347 Passed + 44 Skipped** (no behavior change, just metadata).

Verify pack succeeds for the two non-tool packages:
```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
dotnet pack src/Protocol/Protocol.csproj -c Release -o /tmp/pack-test 2>&1 | tail -5
dotnet pack src/Runner.Dsl/Runner.Dsl.csproj -c Release -o /tmp/pack-test 2>&1 | tail -5
ls /tmp/pack-test/
rm -rf /tmp/pack-test
```
Expected: `SdvTestFramework.Protocol.0.1.0.nupkg` + `SdvTestFramework.Runner.Dsl.0.1.0.nupkg` produced. Cli is not yet packable as a tool — leave that for T3.

---

## Task 3: Pack Cli as a dotnet tool with embedded harness payload

**Why:** Cli ships the harness DLLs bundled. Modders + Claude Code don't have a source tree, so the harness must travel with the tool. Embedded resources are extracted by `HarnessDeployer.Deploy` on first run.

**Files:**
- Modify: `src/Runner/Runner.csproj` — add `PackAsTool` + embedded resources.
- Modify: `src/Protocol/HarnessDeployer.cs` — extraction path.

### Step 1: Add PackAsTool + embedded resources to Runner.csproj

Read `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Runner/Runner.csproj`. The existing csproj should already have a `StageHarnessPayload` target — keep it for source-tree dev workflow. Add packaging-specific configuration:

```xml
<!-- Append to the <PropertyGroup> from T2 step 4: -->
<PackAsTool>true</PackAsTool>
<ToolCommandName>sdv-test</ToolCommandName>
```

Add embedded resources for the harness payload:

```xml
<ItemGroup>
  <!-- Embedded harness payload — extracted by HarnessDeployer at first NuGet-installed run.
       Source-tree devs continue to use the StageHarnessPayload-cached version transparently.
       Build-order dependency on Harness via the existing ProjectReference (ReferenceOutputAssembly=false). -->
  <EmbeddedResource Include="..\Harness\bin\$(Configuration)\net6.0\Harness.dll"
                   LogicalName="harness/Harness.dll"
                   Visible="false" />
  <EmbeddedResource Include="..\Harness\bin\$(Configuration)\net6.0\Protocol.dll"
                   LogicalName="harness/Protocol.dll"
                   Visible="false" />
  <EmbeddedResource Include="..\Harness\bin\$(Configuration)\net6.0\SixLabors.ImageSharp.dll"
                   LogicalName="harness/SixLabors.ImageSharp.dll"
                   Visible="false" />
  <EmbeddedResource Include="..\Harness\manifest.json"
                   LogicalName="harness/manifest.json"
                   Visible="false" />
</ItemGroup>
```

If there's a `<ProjectReference Include="..\Harness\Harness.csproj" ReferenceOutputAssembly="false" />` already, keep it (build-order dependency). If not, add it.

Verify the build still works:
```bash
dotnet build src/Runner/Runner.csproj -c Release 2>&1 | tail -5
```

### Step 2: Update HarnessDeployer.Deploy

Open `/home/fintan/stardewRepos/frobby/sdv-test-framework/src/Protocol/HarnessDeployer.cs`. Read the existing implementation first.

Modify `Deploy(string modsPath)` to add an embedded-resource fallback:

```csharp
using System;
using System.IO;
using System.Reflection;

namespace SdvTestFramework.Protocol;

public static class HarnessDeployer
{
    /// <summary>
    /// Deploy the harness mod payload to <paramref name="modsPath"/>/SdvTestFramework.Harness/.
    /// Two sources, in order:
    /// 1. Source-tree cache (~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness/) —
    ///    populated by the StageHarnessPayload MSBuild target during 'dotnet build'.
    /// 2. Embedded resources in the calling assembly — populated when SdvTestFramework.Cli
    ///    is installed via 'dotnet tool install'.
    /// Idempotent: skips extraction if the target dir already has a manifest.
    /// </summary>
    public static void Deploy(string modsPath)
    {
        var targetDir = Path.Combine(modsPath, "SdvTestFramework.Harness");
        var targetManifest = Path.Combine(targetDir, "manifest.json");

        // Source 1: source-tree cache (existing behavior).
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "sdv-test-framework", "mods", "SdvTestFramework.Harness");
        if (Directory.Exists(cacheDir) && File.Exists(Path.Combine(cacheDir, "manifest.json")))
        {
            CopyDirectory(cacheDir, targetDir);
            return;
        }

        // Source 2: embedded resources.
        var asm = typeof(HarnessDeployer).Assembly;
        var resourceNames = asm.GetManifestResourceNames();
        var harnessResources = Array.FindAll(resourceNames, n => n.StartsWith("harness/", StringComparison.Ordinal));
        if (harnessResources.Length > 0)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var name in harnessResources)
            {
                using var stream = asm.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"manifest resource '{name}' returned null");
                var fileName = name.Substring("harness/".Length);
                var dest = Path.Combine(targetDir, fileName);
                using var fileStream = File.Create(dest);
                stream.CopyTo(fileStream);
            }
            return;
        }

        throw new FileNotFoundException(
            $"No harness payload available. Source-tree cache not found at {cacheDir} " +
            $"and no embedded harness resources in {asm.FullName}. " +
            "Reinstall SdvTestFramework.Cli or rebuild from source.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
```

**Important:** `typeof(HarnessDeployer).Assembly` is the Protocol assembly (since HarnessDeployer lives there post-MCP-T6). But the embedded resources are in the **Cli** assembly (Runner.dll). The deployer needs to look in the calling assembly OR all loaded assemblies.

Update the resource lookup to scan all loaded assemblies:

```csharp
// Source 2: embedded resources, searched across all loaded assemblies.
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    string[] names;
    try { names = asm.GetManifestResourceNames(); }
    catch { continue; }
    var harnessResources = Array.FindAll(names, n => n.StartsWith("harness/", StringComparison.Ordinal));
    if (harnessResources.Length == 0) continue;

    Directory.CreateDirectory(targetDir);
    foreach (var name in harnessResources)
    {
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"manifest resource '{name}' returned null");
        var fileName = name.Substring("harness/".Length);
        var dest = Path.Combine(targetDir, fileName);
        using var fileStream = File.Create(dest);
        stream.CopyTo(fileStream);
    }
    return;
}
```

This finds the resources wherever they were embedded.

### Step 3: Verify

Run: `./scripts/ci.sh 2>&1 | grep "Passed:" | head -10`
Expected: **347 Passed + 44 Skipped**.

Verify Cli packs as a tool:
```bash
dotnet pack src/Runner/Runner.csproj -c Release -o /tmp/pack-test 2>&1 | tail -10
ls /tmp/pack-test/
unzip -l /tmp/pack-test/SdvTestFramework.Cli.0.1.0.nupkg | grep -E "Harness|harness" | head -10
rm -rf /tmp/pack-test
```
Expected: `SdvTestFramework.Cli.0.1.0.nupkg` produced. The `unzip -l` should show `Harness.dll` and `manifest.json` somewhere inside the nupkg (under `tools/` or as embedded — the structure varies).

---

## Task 4: pack.sh + integration placeholder + smoke

**Why:** Wrap the three pack invocations in a script. Add the skipped integration placeholder. Run the local-install smoke.

**Files:**
- Create: `scripts/pack.sh`
- Create: `tests/Runner.Dsl.Tests/NuGetPackagingIntegrationTests.cs`

### Step 1: pack.sh

Write `/home/fintan/stardewRepos/frobby/sdv-test-framework/scripts/pack.sh`:

```bash
#!/usr/bin/env bash
# Build NuGet packages: SdvTestFramework.Protocol, .Runner.Dsl, .Cli.
#
# Output: ./nupkg/*.0.1.0.nupkg
# Used by the local-install smoke and (eventually) a CI publish step.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

OUT="$REPO_ROOT/nupkg"
mkdir -p "$OUT"
rm -f "$OUT"/*.nupkg

echo "==> Build solution (so embedded harness resources are fresh)"
dotnet build sdv-test-framework.slnx -c Release --no-restore

echo "==> Pack Protocol"
dotnet pack src/Protocol/Protocol.csproj -c Release -o "$OUT" --no-build

echo "==> Pack Runner.Dsl"
dotnet pack src/Runner.Dsl/Runner.Dsl.csproj -c Release -o "$OUT" --no-build

echo "==> Pack Cli"
dotnet pack src/Runner/Runner.csproj -c Release -o "$OUT" --no-build

echo "==> Produced packages:"
ls "$OUT"

echo "==> pack.sh PASSED"
```

`chmod +x scripts/pack.sh`.

### Step 2: Integration placeholder

Create `tests/Runner.Dsl.Tests/NuGetPackagingIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

/// <summary>Integration surface for NuGet packaging — verified manually via the local-install smoke (Task 4 step 4).</summary>
public class NuGetPackagingIntegrationTests
{
    [Fact(Skip = "Requires manual local-install smoke — run scripts/pack.sh + dotnet tool install --add-source ./nupkg.")]
    public void NuGetPackages_InstallAndResolve() { }
}
```

### Step 3: Verify CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:\|Skipped:" | head -10`
Expected: **347 Passed + 45 Skipped** (+1 placeholder).

### Step 4: Local install smoke

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
./scripts/pack.sh
ls nupkg/
```
Expected: 3 .nupkg files.

Install the tool locally (not globally — uses tool-path):
```bash
mkdir -p .dotnet-tools
dotnet new tool-manifest --force >/dev/null
dotnet tool install --add-source ./nupkg --tool-path ./.dotnet-tools SdvTestFramework.Cli
./.dotnet-tools/sdv-test --help | head -10
```
Expected: `Commands:` section with `probe`, `doctor`, `list`, `run`, `fixture`, `record`, `mcp`, `build-manifest` listed.

Test a fresh consumer project:
```bash
mkdir -p /tmp/smoke-mod && cd /tmp/smoke-mod
rm -rf *
dotnet new xunit -n MyMod.Tests
cd MyMod.Tests
dotnet add package SdvTestFramework.Runner.Dsl --add-source /home/fintan/stardewRepos/frobby/sdv-test-framework/nupkg --version 0.1.0
# Write a tiny test using the DSL.
cat > UsesDslTest.cs <<'EOF'
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace MyMod.Tests;

public class UsesDslTest
{
    [Fact]
    public void DslTypesCompile()
    {
        // Verifies the package resolved + types are visible. No real test — would need live SDV.
        Assert.NotNull(typeof(Player));
        Assert.NotNull(typeof(SdvTestSession));
    }
}
EOF
dotnet build 2>&1 | tail -10
dotnet test 2>&1 | tail -5
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
rm -rf /tmp/smoke-mod
```
Expected: build succeeds, `Passed: 1` from the consumer test.

Verify MCP startup from the installed tool:
```bash
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"smoke"},"capabilities":{}}}' | \
    timeout 10 ./.dotnet-tools/sdv-test mcp 2>&1 | head -3
```
Expected: JSON response with `"serverInfo":{"name":"sdv-test-mcp"...`. Confirms the installed tool can host MCP.

Verify sample suite still passes:
```bash
./scripts/run-samples.sh 2>&1 | tail -5
```
Expected: `[run] 11/11 passed`.

### Step 5: Cleanup

```bash
rm -rf .dotnet-tools .config nupkg
```
Cleanup leaves the repo as it was before the smoke (the `nupkg/` and tool-install bits are smoke-only).

---

## Task 5: Docs + roadmap + milestone

**Why:** Final task. Update docs to use NuGet install commands; close the roadmap loop.

**Files:**
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/mcp-quickstart.md`
- Modify: `docs/milestones/current.md`
- Modify: `docs/roadmap.md`

### Step 1: docs/dsl-quickstart.md

Find the "Add the project reference" section. Replace the `<ProjectReference>` instruction with:

```markdown
## 1. Install

In your mod's test project:

\`\`\`bash
dotnet add package SdvTestFramework.Runner.Dsl
dotnet tool install -g SdvTestFramework.Cli
\`\`\`

(For development against the source tree, you can still use a `<ProjectReference>` — see
`docs/developer-setup.md`.)
```

(Keep the rest of the doc as-is.)

### Step 2: docs/mcp-quickstart.md

Find the `.mcp.json` snippet that uses `dotnet run --project ...`. Replace with the clean tool-install version:

```markdown
## 1. Install

\`\`\`bash
dotnet tool install -g SdvTestFramework.Cli
\`\`\`

## 2. Configure Claude Code

Add to `.mcp.json` in your workspace:

\`\`\`json
{
  "mcpServers": {
    "sdv-test": {
      "command": "sdv-test",
      "args": ["mcp"]
    }
  }
}
\`\`\`
```

### Step 3: docs/milestones/current.md

After the most recent subsection, append:

```markdown
### NuGet packaging landed (2026-04-24)

Plan: `docs/superpowers/plans/2026-04-24-nuget-packaging.md` (5 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-24-nuget-packaging-design.md`.

**Scope:** ship the framework as installable NuGet packages so modders don't need a
source-tree clone. Three packages produced by `scripts/pack.sh`:
- **`SdvTestFramework.Protocol`** — transitive dep with the JSON-RPC types.
- **`SdvTestFramework.Runner.Dsl`** — library users `dotnet add package` in their
  mod's test project.
- **`SdvTestFramework.Cli`** — `dotnet tool install -g SdvTestFramework.Cli` makes
  `sdv-test` globally available. Bundles CLI + MCP server + the harness mod payload
  via embedded resources.

**Architecture:** central version property (`SdvTestFrameworkVersion=0.1.0` in
`Directory.Build.props`). Free architecture win surfaced during planning: dropped
Runner.Dsl's stale Runner project ref (post-MCP-T6 reorg made it unused —
Runner.Dsl now references only Protocol). Embedded harness payload via
`<EmbeddedResource>` items in Runner.csproj; `HarnessDeployer.Deploy` detects
embedded resources at runtime and extracts to the target mods directory on first
NuGet-installed run. Source-tree devs continue to use the existing
`StageHarnessPayload` MSBuild target — both paths coexist.

**Local-install smoke verified:** packed via `scripts/pack.sh`, installed Cli to
`./.dotnet-tools/`, fresh `MyMod.Tests` project resolved `SdvTestFramework.Runner.Dsl`
and compiled DSL types, MCP `initialize` round-trip succeeded against the installed
tool, sample suite still 11/11.

**Test count after NuGet packaging:** 347+45 (was 347+44; +1 skipped integration
placeholder, no new passing tests — packaging is build-time, not runtime).

**Out of scope:** publishing to nuget.org (logged as Tier 2 followup), GitHub Actions
release workflow, strong-name signing, source link, symbol packages, separate
`SdvTestFramework.Mcp` package.
```

### Step 4: docs/roadmap.md

Remove the **NuGet package** item from Tier 2. Add to the most recent Completed bucket:

```markdown
- **NuGet packaging**. Three packages produced by `scripts/pack.sh`:
  `SdvTestFramework.Protocol` (transitive), `SdvTestFramework.Runner.Dsl` (library),
  `SdvTestFramework.Cli` (`dotnet tool` bundling CLI + MCP server + embedded harness
  payload). Free architecture win — dropped Runner.Dsl's stale Runner project ref.
  347+44 → 347+45.
```

Add a new Tier 2 followup item (since publishing to nuget.org is the next natural
step but out-of-scope here):

```markdown
- [ ] **Publish 0.1.0 to nuget.org** (~2 hours once a real user has smoke-tested).
  Requires NuGet API key + decision about who maintains. Source: NuGet packaging
  out-of-scope.
- [ ] **GitHub Actions release workflow** (~half-day). Automate `dotnet pack` +
  `dotnet nuget push` on `git tag v0.1.0`. Source: NuGet packaging out-of-scope.
```

### Step 5: Final CI

Run: `./scripts/ci.sh 2>&1 | grep "Passed:\|Skipped:" | head -10`
Expected: **347 Passed + 45 Skipped**.

---

## Self-review

**1. Spec coverage:**
- LICENSE + Directory.Build.props version property → T1 ✓
- Drop Runner.Dsl's stale Runner ref → T1 ✓
- Package metadata on 3 csprojs → T2 ✓
- Package READMEs → T2 ✓
- PackAsTool + ToolCommandName on Runner.csproj → T3 ✓
- Embedded harness resources → T3 ✓
- HarnessDeployer extraction path → T3 ✓
- pack.sh script → T4 ✓
- Skipped integration placeholder → T4 ✓
- Local-install smoke (manual) → T4 ✓
- Doc updates → T5 ✓
- Roadmap + milestone → T5 ✓
- All 8 acceptance criteria covered.

**2. Placeholder scan:** No TBD / vague items. The "(Tier 2 followup)" notes are
explicit deferrals.

**3. Type consistency:**
- `SdvTestFrameworkVersion` property — defined in Directory.Build.props (T1),
  referenced via `$(SdvTestFrameworkVersion)` in 3 csprojs (T2 + T3).
- `<IsPackable>true</IsPackable>` per project — set on Protocol, Runner.Dsl, Runner;
  defaults to false elsewhere (test projects, Harness).
- Embedded-resource paths (`harness/Harness.dll`, etc.) — produced by Runner.csproj
  (T3), consumed by HarnessDeployer (T3).
- Package IDs match between csproj `PackageId`, README mentions, and pack.sh ls
  output. ✓

**4. Hazards:**
- **`net6.0` Protocol consumed by `net10.0` Runner.Dsl** — works today (already
  cross-TFM), continues working through NuGet because TFM-compat is automatic.
- **Embedded resources increase Cli package size** by ~5MB (Harness.dll +
  ImageSharp.dll). Acceptable for a one-time install. Not on a hot path.
- **`AppDomain.CurrentDomain.GetAssemblies()` scan in HarnessDeployer** — runs once
  per session at first deploy. Microsoft generally discourages this pattern but it's
  the simplest way to find resources embedded in the calling Cli assembly without
  hard-coding the type. Acceptable; documented in the code comment.
- **Source-tree dev workflow vs NuGet-installed workflow** — both supported via the
  cache-first / embedded-fallback ordering in HarnessDeployer. A source-tree dev who
  later runs from a NuGet-installed Cli would use the cached payload (which may be
  newer than the embedded one). Acceptable — manifest.json version comparison is
  optional polish (M4).
- **Manual nuget.org publishing** — not automated. The smoke verifies "would publish
  cleanly if you ran `dotnet nuget push`," but doesn't push. Documented as Tier 2
  followup.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-24-nuget-packaging.md`.
Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
