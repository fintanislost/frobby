# NuGet Packaging — Design

**Milestone:** Roadmap Tier 2 (NuGet package)
**Date:** 2026-04-24
**Author:** fintan + Claude (brainstorming session, auto-mode)
**Status:** Approved — ready for implementation-plan drafting

## Goal

Package the framework so a modder can install it from NuGet and use it on their own
mod's repo without cloning ours. Two consumable packages:

1. **`SdvTestFramework.Runner.Dsl`** — library NuGet. `dotnet add package` in a mod's
   test project. Brings in the typed C# DSL (`Player.Warp`, `[Scenario]`, `SdvFixture`,
   etc.) plus its transitive dependency on the Protocol assembly.

2. **`SdvTestFramework.Cli`** — `dotnet tool` package. `dotnet tool install -g` makes
   `sdv-test` globally available. Bundles the Runner CLI (probe, doctor, list, run,
   fixture, record, build-manifest, mcp), the MCP server, and the Harness mod payload
   (extracted to `~/.cache/sdv-test-framework/mods/` on first invocation if not
   already there).

Result: a modder writes `MyMod.Tests/MyShopTests.cs`, runs `dotnet test`, sees real
SDV smoke tests pass. A Claude Code user adds one entry to `.mcp.json` and gets the
tool surface.

## Architecture

**Two packages, one source tree.** Source layout unchanged from today; what changes
is csproj metadata + a new packaging step.

**`SdvTestFramework.Runner.Dsl` (library):**
- TFM: `net10.0` (matches existing).
- Drops the `<ProjectReference Include="..\Runner\Runner.csproj" />` — all the
  cross-project types Runner.Dsl uses (`SdvLauncher`, `HarnessDeployer`,
  `UnixSocketRpc`, `JsonRpcSession`, all DTOs) live in Protocol post-MCP-T6 reorg.
  Free architecture win — surfaced during planning.
- Keeps `<ProjectReference Include="..\Protocol\Protocol.csproj" />` — Protocol
  packs as a transitive dependency.
- Adds `<PackageId>`, `<Version>`, `<Authors>`, `<Description>`, `<PackageTags>`,
  `<RepositoryUrl>`, `<PackageLicenseExpression>` (MIT), `<PackageReadmeFile>`.
- `<GeneratePackageOnBuild>false</GeneratePackageOnBuild>` — explicit `dotnet pack`
  step in CI; no surprise packaging during normal builds.
- Protocol.csproj also gets package metadata so it can be a published transitive dep.

**`SdvTestFramework.Cli` (dotnet tool):**
- Repurpose the existing Runner.csproj.
- Add `<PackAsTool>true</PackAsTool>` + `<ToolCommandName>sdv-test</ToolCommandName>`.
- Output executable becomes `sdv-test` after `dotnet tool install`.
- References Runner.Mcp + Runner.Dsl (both already there) + Protocol.

**Harness payload bundling — the trickiest piece.**

Today, `HarnessDeployer.Deploy(modsPath)` copies from
`~/.cache/sdv-test-framework/mods/SdvTestFramework.Harness/` (which is populated by
the `StageHarnessPayload` MSBuild target during `dotnet build` from the source tree).
NuGet-installed users don't have a source tree, so this falls over.

**Solution: embedded resources.** The CLI tool packs the harness DLLs (Harness.dll +
Protocol.dll + ImageSharp.dll + manifest.json + i18n folder) as embedded resources in
the CLI assembly. `HarnessDeployer.Deploy` is updated:

1. Check `<modsPath>/SdvTestFramework.Harness/` — if a payload exists there matching
   our embedded version (compare `manifest.json` versions), no-op.
2. Otherwise extract embedded resources to that location.

The first run after install takes ~1 second to extract; subsequent runs no-op.

This avoids the alternative — shipping harness as a content file alongside the tool —
which is awkward because dotnet tools don't have a clean "payload directory" pattern.
Embedded resources keeps the tool a single binary that knows how to bootstrap itself.

**Versioning:**
- Start at **0.1.0** for both packages. Pre-1.0 signals the framework is still
  evolving. The two packages version in lockstep; `Cli` 0.1.0 expects `Dsl` 0.1.0.
- Centralize the version in `Directory.Build.props` so a single edit bumps both.

## Components

**Modified files:**

- `src/Protocol/Protocol.csproj` — package metadata. TFM is already `net6.0` (it has
  to stay net6 because Harness consumes it). Package as `SdvTestFramework.Protocol`.
  This is a transitive dep of both Runner.Dsl and Cli; users don't install it
  directly.
- `src/Runner.Dsl/Runner.Dsl.csproj` — drop Runner project ref (free win). Add
  package metadata. Package as `SdvTestFramework.Runner.Dsl`.
- `src/Runner/Runner.csproj` — `<PackAsTool>true</PackAsTool>` + tool command name.
  Add embedded resources for the harness payload. Package as `SdvTestFramework.Cli`.
- `src/Harness/Harness.csproj` — already builds Harness.dll. No package metadata
  needed (Harness is consumed only as bundled content of the Cli tool — never
  installed directly via NuGet).
- `src/Runner/HarnessDeployer.cs` (in Protocol per MCP-T6 reorg, actually at
  `src/Protocol/HarnessDeployer.cs`) — update to extract from embedded resources if
  the source-tree cache path doesn't exist. Backward-compat: source-tree devs still
  get the StageHarnessPayload-cached version.
- `Directory.Build.props` — central version property `<SdvTestFrameworkVersion>0.1.0</SdvTestFrameworkVersion>` referenced by package csprojs.

**New files:**

- `LICENSE` — MIT license text. Required for NuGet `<PackageLicenseExpression>`.
- `nuget/README-Dsl.md` — short readme that ships inside the Dsl NuGet package
  (shown on nuget.org). ~30 lines: install command, minimal example, link to repo
  for full docs.
- `nuget/README-Cli.md` — same for the Cli tool. Quickstart for `dotnet tool
  install` + `.mcp.json` config + `sdv-test --help`.
- `nuget/README-Protocol.md` — even shorter for Protocol (transitive dep, mostly
  invisible). Just "this is consumed by SdvTestFramework.Runner.Dsl + .Cli; you
  shouldn't need to install it directly."

**New scripts:**

- `scripts/pack.sh` — `dotnet pack -c Release -o ./nupkg/` for the three packages.
  Used by the local install smoke + future CI publish step.

**Modified docs:**

- `docs/dsl-quickstart.md` — replace `<ProjectReference>` instructions with
  `dotnet add package SdvTestFramework.Runner.Dsl`.
- `docs/mcp-quickstart.md` — replace the `dotnet run --project` `.mcp.json` snippet
  with the cleaner `{"command": "sdv-test", "args": ["mcp"]}` snippet.
- `docs/milestones/current.md` — Tier 2 NuGet completion subsection.
- `docs/roadmap.md` — move NuGet package from Tier 2 to Completed.

**No new tests** for the packaging itself — pack output is verified by the local
install smoke (Phase D). One skipped integration placeholder for the smoke.

**Test count target:** 347+44 → 347+45 (no new passing; +1 skipped).

## Wire / shape

### Protocol package (transitive dependency)

```xml
<PackageId>SdvTestFramework.Protocol</PackageId>
<Version>$(SdvTestFrameworkVersion)</Version>
<Authors>fintan + contributors</Authors>
<Description>JSON-RPC types and Stardew Valley test-framework transport. Internal package — consumed by SdvTestFramework.Runner.Dsl and SdvTestFramework.Cli.</Description>
<PackageTags>stardew-valley;testing;json-rpc</PackageTags>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageReadmeFile>README.md</PackageReadmeFile>
<RepositoryUrl>https://github.com/fintan/sdv-test-framework</RepositoryUrl>
<RepositoryType>git</RepositoryType>
```

### Runner.Dsl package

```xml
<PackageId>SdvTestFramework.Runner.Dsl</PackageId>
<Version>$(SdvTestFrameworkVersion)</Version>
<Description>Typed C# DSL for writing Stardew Valley mod tests. Use [Scenario] + ambient static facets (Player, World, Time, Draw, etc.) to author tests as plain xUnit methods. Pairs with the SdvTestFramework.Cli tool for SDV launch + MCP server.</Description>
<PackageTags>stardew-valley;testing;xunit;dsl</PackageTags>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Removes `<ProjectReference Include="..\Runner\Runner.csproj" />`. Keeps Protocol.

### Cli tool package

```xml
<PackageId>SdvTestFramework.Cli</PackageId>
<Version>$(SdvTestFrameworkVersion)</Version>
<Description>Stardew Valley test-framework CLI: launch SDV, run scenarios, scaffold templates, build texture manifests, run as MCP server for Claude Code.</Description>
<PackageTags>stardew-valley;testing;cli;mcp</PackageTags>
<PackAsTool>true</PackAsTool>
<ToolCommandName>sdv-test</ToolCommandName>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Embedded harness payload via `<EmbeddedResource Include="..." />` items pointing
at the harness output directory after build.

### Embedded harness resources

In `Runner.csproj` (the Cli):

```xml
<ItemGroup>
  <!-- Harness payload — extracted by HarnessDeployer at first run. -->
  <EmbeddedResource Include="$(MSBuildProjectDirectory)\..\Harness\bin\$(Configuration)\net6.0\Harness.dll"
                   LogicalName="harness/Harness.dll" />
  <EmbeddedResource Include="$(MSBuildProjectDirectory)\..\Harness\bin\$(Configuration)\net6.0\Protocol.dll"
                   LogicalName="harness/Protocol.dll" />
  <EmbeddedResource Include="$(MSBuildProjectDirectory)\..\Harness\bin\$(Configuration)\net6.0\SixLabors.ImageSharp.dll"
                   LogicalName="harness/SixLabors.ImageSharp.dll" />
  <EmbeddedResource Include="$(MSBuildProjectDirectory)\..\Harness\manifest.json"
                   LogicalName="harness/manifest.json" />
</ItemGroup>
```

Plus a build-order dependency: `<ProjectReference Include="..\Harness\Harness.csproj" ReferenceOutputAssembly="false" />` so MSBuild builds the Harness before reading
its output as a resource. (This already exists for `StageHarnessPayload`; adapt.)

### `HarnessDeployer.Deploy` — updated logic

Pseudo-code:

```
Deploy(string modsPath):
    var targetDir = modsPath/SdvTestFramework.Harness/
    if exists(targetDir/manifest.json) and version matches embedded:
        return  // already deployed, current version
    create targetDir if missing
    foreach embedded resource named "harness/...":
        extract to targetDir/<basename>
    log "[harness-deployer] extracted harness to <targetDir>"
```

Source-tree devs continue to get the StageHarnessPayload behavior because the cache
dir at `~/.cache/sdv-test-framework/mods/` already has a fresh payload — Deploy
sees the manifest version match and skips extraction.

## Error handling

- **Embedded resource missing** (build skipped harness, or manifest mismatch) — log
  warning, fall back to old behavior of looking for a pre-staged cache dir. If that
  also missing, throw with clear message: "no harness payload available — reinstall
  SdvTestFramework.Cli or re-run dotnet build."
- **Permission failure on extraction** (read-only mods dir) — propagate the IOException
  with the target path in the message.
- **Version mismatch** between manifest.json on-disk vs embedded — overwrite (newer
  embedded version always wins). Log "[harness-deployer] upgraded harness from x to y".
- **Cli tool installed at `0.1.0` invoked against a project using `Runner.Dsl 0.2.0`**
  — would happen if user updates one but not the other. Out-of-scope to detect at
  runtime; leave for ecosystem feedback. Document version-lockstep in the README.

## Testing

**No new unit tests** — packaging is a build-time concern, not a runtime behavior.
Existing 347 passing tests verify the framework still works after the project-ref
shuffle.

**Skipped integration placeholder** (1):

- `tests/Runner.Dsl.Tests/NuGetPackagingIntegrationTests.cs` — `[Fact(Skip="...")]`,
  exercised by Phase D's local-install smoke.

**Manual smoke** (Phase D):

1. `./scripts/pack.sh` — produces 3 .nupkg files.
2. `dotnet new tool-manifest --force; dotnet tool install --add-source ./nupkg --tool-path ./.dotnet-tools SdvTestFramework.Cli`.
3. `./.dotnet-tools/sdv-test --help` — verify all subcommands listed.
4. Create a fresh test project in `/tmp/smoke-mod/` outside the source tree:
   ```bash
   mkdir /tmp/smoke-mod && cd /tmp/smoke-mod
   dotnet new xunit
   dotnet add package SdvTestFramework.Runner.Dsl --add-source <repo>/nupkg
   ```
5. Write a tiny test using the DSL — verify `dotnet test` compiles. (Doesn't need to
   pass — that requires live SDV — just verify the package resolves and the DSL
   types are visible.)
6. Try `./.dotnet-tools/sdv-test mcp < /dev/null | head -1` — verify the MCP server
   binary is intact.
7. Try `./.dotnet-tools/sdv-test list <repo>/tests/samples/` — verify scenario
   discovery works using the installed CLI (no source tree).

## Acceptance criteria

1. `./scripts/ci.sh` green at 347 Passed + 45 Skipped (just +1 skipped placeholder).
2. `./scripts/pack.sh` produces three .nupkg files in `./nupkg/` with the expected
   names (`SdvTestFramework.Protocol.0.1.0.nupkg`, `SdvTestFramework.Runner.Dsl.0.1.0.nupkg`,
   `SdvTestFramework.Cli.0.1.0.nupkg`).
3. Local install via `dotnet tool install --add-source ./nupkg` succeeds; `sdv-test
   --help` shows all subcommands.
4. Local `dotnet add package SdvTestFramework.Runner.Dsl --add-source ./nupkg` in a
   fresh project resolves; DSL types visible at compile time.
5. Existing `./scripts/run-samples.sh` still 11/11 PASS — no regression from the
   project-ref drop or harness-deployer changes.
6. `docs/dsl-quickstart.md` + `docs/mcp-quickstart.md` use the NuGet install commands.
7. `docs/roadmap.md` — NuGet item moved from Tier 2 to Completed.
8. `docs/milestones/current.md` gains a NuGet completion subsection.

## Out of scope (Tier 2 followups)

- **Publishing to nuget.org** — the package is buildable + locally installable. Public
  publishing requires a NuGet API key + decision about who maintains. Logged as a
  Tier 2 followup ("Publish 0.1.0 to nuget.org once smoke-tested by a real user").
- **GitHub Actions release workflow** — automate `dotnet pack` + `dotnet nuget push`
  on tag. M3 ecosystem polish. Tier 2 followup.
- **Strong-name signing** — required for some enterprise consumers; almost zero of
  our target audience needs this. Skip.
- **Source link** — debug-symbol mapping back to GitHub source. Polish; Tier 4.
- **Symbol packages** (`.snupkg`) — same. Tier 4.
- **`SdvTestFramework.Mcp` as a separate package** — MCP server is bundled in the
  Cli tool. A standalone Mcp package would let someone install JUST the MCP server
  without the CLI. No real use case yet — skip.
- **`SdvTestFramework.Harness` as a published package** — the harness mod is bundled
  inside Cli's embedded resources. Publishing it standalone would let users install
  the harness without our CLI. No use case — skip.

## Links

- Roadmap: `docs/roadmap.md` Tier 2 (this item).
- Originally spec'd as M3 subproject 3 in `docs/spec.md` §7 Phase 3.
- Brainstorm: 2026-04-24 auto-mode session (this doc).
