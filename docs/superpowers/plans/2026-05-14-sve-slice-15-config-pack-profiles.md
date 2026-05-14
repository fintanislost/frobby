# SVE Slice 15 Config Pack Profiles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral repo-local profile support so Frobby can run scenarios against isolated alternate mod/config packs, then prove it with SVE Grandpa's Farm.

**Architecture:** Extend the repo runner's existing `sdv-test.config.json` and `modSets` flow instead of adding a second launcher path. Scenario JSON gains an optional `profile` field. Repo runs resolve that profile to dependency mods, repo-owned mods, a profile-specific test `Mods` cache, and optional config overlays before invoking the normal `run` command.

**Tech Stack:** C#/.NET 10, xUnit, System.Text.Json, Frobby JSON scenario schema, SMAPI mod folder staging, repo-local shell wrappers, SVE `scripts/sdv-test`.

---

## Branch And Repo Setup

Work in Frobby:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch -c feature/sve-slice-15-config-pack-profiles
```

If the branch already exists:

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework switch feature/sve-slice-15-config-pack-profiles
```

Work in SVE only on a feature branch. Do not merge SVE to master unless Fintan explicitly asks:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch -c feature/frobby-sve-slice-15-config-pack-profiles
```

If the branch already exists:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded switch feature/frobby-sve-slice-15-config-pack-profiles
```

## File Map

- Modify `src/Protocol/Models/ScenarioSpec.cs`
  - Add optional `Profile` property.
- Modify `schemas/scenario.schema.json`
  - Allow top-level `"profile": "profile-id"`.
- Modify `tests/Runner.Tests/ScenarioLoaderTests.cs`
  - Prove scenario profile deserializes and schema blocks non-string profile values.
- Modify `src/Runner/Repo/RepoTestConfig.cs`
  - Add top-level `profiles` config plus `RepoProfileConfig` and `RepoConfigOverlayConfig`.
  - Keep existing `modSets` behavior valid.
- Create `src/Runner/Repo/RepoProfileResolver.cs`
  - Resolve inherited profiles and legacy mod sets to a single deployable profile.
- Create `tests/Runner.Tests/Repo/RepoProfileResolverTests.cs`
  - Cover inheritance, dependency ordering, duplicate mod paths, missing profiles, cycles, and overlay path resolution.
- Modify `src/Runner/Repo/RepoRunPlanner.cs`
  - Select the requested profile, set a profile-specific `--mods-path`, pass profile metadata and config overlays to child `run`.
- Modify `tests/Runner.Tests/Repo/RepoRunPlannerTests.cs`
  - Cover profile args, default mod set compatibility, profile cache namespace, and overlay args.
- Modify `src/Protocol/ExtraModDeployer.cs`
  - Add neutral config overlay application after mods are deployed.
- Create `src/Protocol/ExtraModConfigOverlay.cs`
  - Shared overlay value type used by deployer and runner.
- Modify `tests/Protocol.Tests/ExtraModDeployerTests.cs`
  - Cover overlay copy success and path traversal rejection.
- Modify `src/Runner/Commands/RunCommandOptions.cs`
  - Add profile metadata and overlay fields.
- Modify `src/Runner/Commands/RunCommand.cs`
  - Parse profile metadata flags and apply config overlays after extra mod deployment.
- Modify `tests/Runner.Tests/RunCommandTests.cs`
  - Cover overlay flag parsing, application, and errors without launching SDV.
- Modify `src/Runner/Commands/RunSuiteCommand.cs`
  - Pass profile metadata and overlay flags through child runs.
- Modify `tests/Runner.Tests/RunSuiteCommandTests.cs`
  - Cover pass-through for three-value overlay flags.
- Modify `src/Runner/Commands/RepoCommand.cs`
  - For mixed-profile scenario directories, build once then run each selected scenario with its declared profile.
- Modify `tests/Runner.Tests/Repo/RepoCommandTests.cs`
  - Cover scenario-declared profile routing and no-profile compatibility.
- Modify `src/Protocol/Reports/RunSummary.cs`, `src/Runner/Reports/RunMetadataBuilder.cs`, and `src/Runner/Reports/HtmlReportGenerator.cs`
  - Record and render selected profile, cache namespace, mods path, staged mod sources, and overlays.
- Modify `tests/Runner.Tests/Reports/RunMetadataBuilderTests.cs` and `tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs`
  - Cover profile metadata in JSON and HTML.
- Modify `src/Runner/Repo/RepoScaffoldGenerator.cs`
  - Document profile examples in generated `docs/FROBBY.md`.
- Modify `tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs`
  - Cover neutral profile documentation in generated scaffolds.
- Modify `README.md`, `docs/rpc-schema.md`, and `docs/dsl-quickstart.md`
  - Document scenario profiles, repo `profiles`, and config overlays.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`
  - Move Slice 15 from pending to done after live verification.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json`
  - Add `profiles.sve-core` and `profiles.sve-grandpas-farm`.
- Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/20-sve-grandpas-farm-profile.test.json`
  - Prove Grandpa's Farm profile loads in isolation.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
  - Document the Grandpa's Farm profile scenario.

## Task 1: Scenario Profile Field

**Files:**
- Modify: `src/Protocol/Models/ScenarioSpec.cs`
- Modify: `schemas/scenario.schema.json`
- Test: `tests/Runner.Tests/ScenarioLoaderTests.cs`

- [ ] **Step 1: Write the failing scenario loader test**

Add this test after `Load_WithConfigAndAssertions_RoundTripsAll`:

```csharp
[Fact]
public void Load_WithProfile_RoundTripsProfile()
{
    var path = WriteTemp("""
{
  "name": "profiled",
  "profile": "sve-grandpas-farm",
  "steps": []
}
""");

    var spec = ScenarioLoader.Load(path);

    Assert.Equal("sve-grandpas-farm", spec.Profile);
}
```

Add this schema validation test after `Load_ExtraTopLevelField_Throws`:

```csharp
[Fact]
public void Load_NonStringProfile_Throws()
{
    var path = WriteTemp("""{"name":"x","profile":42,"steps":[]}""");

    var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));

    Assert.Contains("schema validation", ex.Message);
    Assert.Contains("profile", ex.Message);
}
```

- [ ] **Step 2: Run the focused test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests"
```

Expected: `Load_WithProfile_RoundTripsProfile` fails because `ScenarioSpec.Profile` is missing or stays null.

- [ ] **Step 3: Add the protocol model field**

In `ScenarioSpec`, add this property after `Fixture`:

```csharp
/// <summary>Optional repo-local mod/config profile required by this scenario.</summary>
public string? Profile { get; set; }
```

- [ ] **Step 4: Extend the scenario schema**

In `schemas/scenario.schema.json`, add this top-level property next to `fixture`:

```json
"profile": { "type": "string", "minLength": 1 },
```

- [ ] **Step 5: Run the focused test and confirm GREEN**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~ScenarioLoaderTests"
```

Expected: all `ScenarioLoaderTests` pass.

- [ ] **Step 6: Commit**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/Models/ScenarioSpec.cs schemas/scenario.schema.json tests/Runner.Tests/ScenarioLoaderTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: allow scenario profiles"
```

## Task 2: Repo Profile Config And Resolver

**Files:**
- Modify: `src/Runner/Repo/RepoTestConfig.cs`
- Create: `src/Runner/Repo/RepoProfileResolver.cs`
- Test: `tests/Runner.Tests/Repo/RepoProfileResolverTests.cs`
- Modify: `tests/Runner.Tests/Repo/RepoTestConfigTests.cs`

- [ ] **Step 1: Write config parsing tests**

Add this test to `RepoTestConfigTests` after `Load_reads_mod_set_deps`:

```csharp
[Fact]
public void Load_reads_profiles_with_inheritance_deps_extra_mods_cache_namespace_and_overlays()
{
    WriteConfig(
        """
        {
          "project": { "name": "Frobby", "slug": "frobby", "version": "1.2.3" },
          "build": { "command": "dotnet" },
          "defaultTarget": "smoke",
          "modSets": [
            { "name": "core", "extraMods": ["mods/Core"] }
          ],
          "profiles": {
            "sve-core": {
              "deps": [{ "id": "Pathoschild.ContentPatcher" }],
              "extraMods": ["mods/SVE"]
            },
            "sve-grandpas-farm": {
              "inherits": "sve-core",
              "extraMods": ["Grandpa's Farm/[CP] Grandpa's Farm"],
              "cacheNamespace": "sve-grandpas-farm",
              "configOverlays": [
                {
                  "source": "tests/config/grandpas-farm/content-patcher.json",
                  "targetMod": "Pathoschild.ContentPatcher",
                  "targetPath": "config.json"
                }
              ]
            }
          }
        }
        """);

    var config = RepoTestConfig.Load(_repoRoot);

    Assert.True(config.Profiles.ContainsKey("sve-core"));
    Assert.True(config.Profiles.ContainsKey("sve-grandpas-farm"));
    Assert.Equal("sve-core", config.Profiles["sve-grandpas-farm"].Inherits);
    Assert.Equal("sve-grandpas-farm", config.Profiles["sve-grandpas-farm"].CacheNamespace);
    var overlay = Assert.Single(config.Profiles["sve-grandpas-farm"].ConfigOverlays);
    Assert.Equal("tests/config/grandpas-farm/content-patcher.json", overlay.Source);
    Assert.Equal("Pathoschild.ContentPatcher", overlay.TargetMod);
    Assert.Equal("config.json", overlay.TargetPath);
}
```

Add validation coverage to `Load_validates_required_fields` as extra `InlineData` rows:

```csharp
[InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"extraMods":[" "]}}}""", "profiles.bad.extraMods[0]")]
[InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"configOverlays":[{"source":" ","targetMod":"Mod","targetPath":"config.json"}]}}}""", "profiles.bad.configOverlays[0].source")]
[InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"configOverlays":[{"source":"a.json","targetMod":" ","targetPath":"config.json"}]}}}""", "profiles.bad.configOverlays[0].targetMod")]
[InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","extraMods":["mods/a"]}],"profiles":{"bad":{"configOverlays":[{"source":"a.json","targetMod":"Mod","targetPath":" "} ]}}}""", "profiles.bad.configOverlays[0].targetPath")]
```

- [ ] **Step 2: Run config tests and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~RepoTestConfigTests"
```

Expected: compile failure because `Profiles`, `RepoProfileConfig`, and `RepoConfigOverlayConfig` do not exist.

- [ ] **Step 3: Add profile config models**

In `RepoTestConfig`, add:

```csharp
[JsonPropertyName("profiles")]
public IReadOnlyDictionary<string, RepoProfileConfig> Profiles { get; init; }
    = new Dictionary<string, RepoProfileConfig>(StringComparer.Ordinal);
```

Add these model types after `RepoModSetConfig`:

```csharp
public sealed class RepoProfileConfig
{
    [JsonPropertyName("inherits")]
    public string? Inherits { get; init; }

    [JsonPropertyName("deps")]
    public IReadOnlyList<RepoModDependencyConfig> Deps { get; init; } = Array.Empty<RepoModDependencyConfig>();

    [JsonPropertyName("extraMods")]
    public IReadOnlyList<string> ExtraMods { get; init; } = Array.Empty<string>();

    [JsonPropertyName("configOverlays")]
    public IReadOnlyList<RepoConfigOverlayConfig> ConfigOverlays { get; init; } = Array.Empty<RepoConfigOverlayConfig>();

    [JsonPropertyName("cacheNamespace")]
    public string? CacheNamespace { get; init; }
}

public sealed class RepoConfigOverlayConfig
{
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("targetMod")]
    public string? TargetMod { get; init; }

    [JsonPropertyName("targetPath")]
    public string? TargetPath { get; init; }
}
```

- [ ] **Step 4: Validate profile config**

In `RepoTestConfig.Validate`, after the `modSets` loop, add:

```csharp
foreach (var (name, profile) in Profiles)
{
    RequireText(name, path, $"profiles.{name}");
    if (profile is null)
    {
        throw Missing(path, $"profiles.{name}");
    }

    if (profile.Inherits is not null)
    {
        RequireText(profile.Inherits, path, $"profiles.{name}.inherits");
    }

    if (profile.CacheNamespace is not null)
    {
        RequireText(profile.CacheNamespace, path, $"profiles.{name}.cacheNamespace");
    }

    ValidateDependencies(profile.Deps, path, $"profiles.{name}.deps");
    ValidateEntries(profile.ExtraMods, path, $"profiles.{name}.extraMods");
    ValidateConfigOverlays(profile.ConfigOverlays, path, $"profiles.{name}.configOverlays");
}
```

Add this helper near `ValidateEntries`:

```csharp
private static void ValidateConfigOverlays(
    IReadOnlyList<RepoConfigOverlayConfig>? overlays,
    string path,
    string field)
{
    if (overlays is null)
    {
        return;
    }

    for (var i = 0; i < overlays.Count; i++)
    {
        if (overlays[i] is not { } overlay)
        {
            throw Missing(path, $"{field}[{i}]");
        }

        RequireText(overlay.Source, path, $"{field}[{i}].source");
        RequireText(overlay.TargetMod, path, $"{field}[{i}].targetMod");
        RequireText(overlay.TargetPath, path, $"{field}[{i}].targetPath");
    }
}
```

- [ ] **Step 5: Write failing resolver tests**

Create `tests/Runner.Tests/Repo/RepoProfileResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public sealed class RepoProfileResolverTests : IDisposable
{
    private readonly string _repoRoot = CreateTempDirectory();

    [Fact]
    public void Resolve_uses_legacy_mod_set_when_profile_name_matches_mod_set()
    {
        Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core"));
        var config = Config(modSets: [ModSet("core", "mods/Core")]);

        var profile = RepoProfileResolver.Resolve(
            _repoRoot,
            config,
            requestedName: "core",
            environment: new Dictionary<string, string?>(),
            requireRepoExtraMods: true);

        Assert.Equal("core", profile.Id);
        Assert.Equal("core", profile.CacheNamespace);
        Assert.Equal([Path.Combine(_repoRoot, "mods", "Core")], profile.ExtraMods);
        Assert.Empty(profile.ConfigOverlays);
    }

    [Fact]
    public void Resolve_profile_inherits_parent_deps_extra_mods_and_overlays_in_order()
    {
        var cacheRoot = Path.Combine(_repoRoot, "dep-cache");
        var contentPatcher = CreateCachedMod(cacheRoot, "Pathoschild.ContentPatcher", "2.7.0");
        var coreMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core")).FullName;
        var farmMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "GrandpasFarm")).FullName;
        var overlaySource = Path.Combine(_repoRoot, "tests", "config", "gf.json");
        Directory.CreateDirectory(Path.GetDirectoryName(overlaySource)!);
        File.WriteAllText(overlaySource, "{}");
        var config = Config(
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["sve-core"] = new()
                {
                    Deps = [new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher", Version = "2.7.0" }],
                    ExtraMods = ["mods/Core"],
                },
                ["sve-grandpas-farm"] = new()
                {
                    Inherits = "sve-core",
                    ExtraMods = ["mods/Core", "mods/GrandpasFarm"],
                    CacheNamespace = "grandpas-farm-cache",
                    ConfigOverlays =
                    [
                        new RepoConfigOverlayConfig
                        {
                            Source = "tests/config/gf.json",
                            TargetMod = "Pathoschild.ContentPatcher",
                            TargetPath = "config.json",
                        },
                    ],
                },
            });
        var env = new Dictionary<string, string?> { [RepoDependencyCache.CacheEnvironmentVariable] = cacheRoot };

        var profile = RepoProfileResolver.Resolve(_repoRoot, config, "sve-grandpas-farm", env, requireRepoExtraMods: true);

        Assert.Equal("sve-grandpas-farm", profile.Id);
        Assert.Equal("grandpas-farm-cache", profile.CacheNamespace);
        Assert.Equal([contentPatcher, coreMod, farmMod], profile.ExtraMods);
        var overlay = Assert.Single(profile.ConfigOverlays);
        Assert.Equal(overlaySource, overlay.SourcePath);
        Assert.Equal("Pathoschild.ContentPatcher", overlay.TargetModUniqueId);
        Assert.Equal("config.json", overlay.TargetRelativePath);
    }

    [Fact]
    public void Resolve_unknown_profile_throws_clear_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RepoProfileResolver.Resolve(_repoRoot, Config(), "missing", new Dictionary<string, string?>(), true));

        Assert.Contains("Unknown profile 'missing'", ex.Message);
    }

    [Fact]
    public void Resolve_profile_cycle_throws_clear_error()
    {
        var config = Config(
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["a"] = new() { Inherits = "b", ExtraMods = ["mods/A"] },
                ["b"] = new() { Inherits = "a", ExtraMods = ["mods/B"] },
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RepoProfileResolver.Resolve(_repoRoot, config, "a", new Dictionary<string, string?>(), false));

        Assert.Contains("profile inheritance cycle", ex.Message);
        Assert.Contains("a -> b -> a", ex.Message);
    }

    [Fact]
    public void Resolve_missing_overlay_source_throws_before_launch()
    {
        var config = Config(
            profiles: new Dictionary<string, RepoProfileConfig>
            {
                ["broken"] = new()
                {
                    ExtraMods = ["mods/Broken"],
                    ConfigOverlays =
                    [
                        new RepoConfigOverlayConfig
                        {
                            Source = "tests/config/missing.json",
                            TargetMod = "Example.Mod",
                            TargetPath = "config.json",
                        },
                    ],
                },
            });

        var ex = Assert.Throws<FileNotFoundException>(() =>
            RepoProfileResolver.Resolve(_repoRoot, config, "broken", new Dictionary<string, string?>(), false));

        Assert.Contains("tests/config/missing.json", ex.Message);
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }

    private static RepoTestConfig Config(
        RepoModSetConfig[]? modSets = null,
        IReadOnlyDictionary<string, RepoProfileConfig>? profiles = null)
        => new()
        {
            Project = new RepoProjectConfig { Name = "Frobby", Slug = "frobby", Version = "1.2.3" },
            Build = new RepoBuildConfig { Command = "dotnet", Args = ["build"] },
            DefaultTarget = "tests/sdv",
            ModSets = modSets ?? [ModSet("core", "mods/Core")],
            Profiles = profiles ?? new Dictionary<string, RepoProfileConfig>(),
        };

    private static RepoModSetConfig ModSet(string name, params string[] extraMods)
        => new() { Name = name, ExtraMods = extraMods };

    private static string CreateCachedMod(string cacheRoot, string uniqueId, string version)
    {
        var path = Path.Combine(cacheRoot, uniqueId);
        Directory.CreateDirectory(path);
        File.WriteAllText(
            Path.Combine(path, "manifest.json"),
            $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "repo-profile-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [ ] **Step 6: Run resolver tests and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~RepoProfileResolverTests|FullyQualifiedName~RepoTestConfigTests"
```

Expected: compile failure because `RepoProfileResolver` and resolved overlay types do not exist.

- [ ] **Step 7: Add the resolver implementation**

Create `src/Runner/Repo/RepoProfileResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Repo;

public sealed record ResolvedRepoProfile(
    string Id,
    string CacheNamespace,
    IReadOnlyList<string> ExtraMods,
    IReadOnlyList<ExtraModConfigOverlay> ConfigOverlays);

public static class RepoProfileResolver
{
    public static ResolvedRepoProfile Resolve(
        string repoRoot,
        RepoTestConfig config,
        string? requestedName,
        IReadOnlyDictionary<string, string?>? environment,
        bool requireRepoExtraMods)
    {
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? SelectDefaultName(config)
            : requestedName!;

        if (config.Profiles.ContainsKey(name))
        {
            return ResolveProfile(repoRoot, config, name, environment, requireRepoExtraMods, new Stack<string>());
        }

        var modSet = config.ModSets.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new InvalidOperationException($"Unknown profile '{name}'.");
        return ResolveModSet(repoRoot, modSet, environment, requireRepoExtraMods);
    }

    private static string SelectDefaultName(RepoTestConfig config)
    {
        if (config.ModSets.Count > 0 && !string.IsNullOrWhiteSpace(config.ModSets[0].Name))
        {
            return config.ModSets[0].Name!;
        }

        if (config.Profiles.Count > 0)
        {
            return config.Profiles.Keys.OrderBy(value => value, StringComparer.Ordinal).First();
        }

        throw new InvalidOperationException("sdv-test config must define at least one mod set or profile.");
    }

    private static ResolvedRepoProfile ResolveModSet(
        string repoRoot,
        RepoModSetConfig modSet,
        IReadOnlyDictionary<string, string?>? environment,
        bool requireRepoExtraMods)
    {
        var extraMods = ResolveDeps(modSet.Deps, environment)
            .Concat(ResolveExtraMods(repoRoot, modSet.ExtraMods, environment, requireRepoExtraMods))
            .Distinct(PathComparer)
            .ToArray();
        var id = RequireName(modSet.Name, "mod set");
        return new ResolvedRepoProfile(id, SanitizeCacheNamespace(id), extraMods, Array.Empty<ExtraModConfigOverlay>());
    }

    private static ResolvedRepoProfile ResolveProfile(
        string repoRoot,
        RepoTestConfig config,
        string name,
        IReadOnlyDictionary<string, string?>? environment,
        bool requireRepoExtraMods,
        Stack<string> stack)
    {
        if (stack.Contains(name))
        {
            var cycle = stack.Reverse().Concat([name]);
            throw new InvalidOperationException($"profile inheritance cycle: {string.Join(" -> ", cycle)}");
        }

        if (!config.Profiles.TryGetValue(name, out var profile))
        {
            throw new InvalidOperationException($"Unknown profile '{name}'.");
        }

        stack.Push(name);
        var inherited = string.IsNullOrWhiteSpace(profile.Inherits)
            ? new ResolvedRepoProfile(name, SanitizeCacheNamespace(name), Array.Empty<string>(), Array.Empty<ExtraModConfigOverlay>())
            : ResolveProfile(repoRoot, config, profile.Inherits!, environment, requireRepoExtraMods, stack);
        stack.Pop();

        var extraMods = inherited.ExtraMods
            .Concat(ResolveDeps(profile.Deps, environment))
            .Concat(ResolveExtraMods(repoRoot, profile.ExtraMods, environment, requireRepoExtraMods))
            .Distinct(PathComparer)
            .ToArray();
        var overlays = inherited.ConfigOverlays
            .Concat(ResolveOverlays(repoRoot, profile.ConfigOverlays, environment))
            .ToArray();
        var cacheNamespace = string.IsNullOrWhiteSpace(profile.CacheNamespace)
            ? SanitizeCacheNamespace(name)
            : SanitizeCacheNamespace(profile.CacheNamespace!);

        return new ResolvedRepoProfile(name, cacheNamespace, extraMods, overlays);
    }

    private static IEnumerable<string> ResolveDeps(
        IReadOnlyList<RepoModDependencyConfig> deps,
        IReadOnlyDictionary<string, string?>? environment)
        => deps.Select(dep => RepoDependencyCache.ResolveRequired(dep, environment));

    private static IEnumerable<string> ResolveExtraMods(
        string repoRoot,
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, string?>? environment,
        bool requireExists)
        => paths.Select(path => RepoPathResolver.Resolve(repoRoot, path, environment, requireExists));

    private static IEnumerable<ExtraModConfigOverlay> ResolveOverlays(
        string repoRoot,
        IReadOnlyList<RepoConfigOverlayConfig> overlays,
        IReadOnlyDictionary<string, string?>? environment)
    {
        foreach (var overlay in overlays)
        {
            var source = RepoPathResolver.Resolve(repoRoot, overlay.Source!, environment, requireExists: true);
            yield return new ExtraModConfigOverlay(source, overlay.TargetMod!, overlay.TargetPath!);
        }
    }

    private static string RequireName(string? name, string label)
        => !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new InvalidOperationException($"repo {label} name is required.");

    private static string SanitizeCacheNamespace(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value.Trim();
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
```

- [ ] **Step 8: Run focused tests and confirm GREEN**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~RepoProfileResolverTests|FullyQualifiedName~RepoTestConfigTests"
```

Expected: resolver and config tests pass.

- [ ] **Step 9: Commit**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Runner/Repo/RepoTestConfig.cs src/Runner/Repo/RepoProfileResolver.cs tests/Runner.Tests/Repo/RepoProfileResolverTests.cs tests/Runner.Tests/Repo/RepoTestConfigTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: resolve repo test profiles"
```

## Task 3: Overlay Deployer And Run Command Flags

**Files:**
- Create: `src/Protocol/ExtraModConfigOverlay.cs`
- Modify: `src/Protocol/ExtraModDeployer.cs`
- Modify: `tests/Protocol.Tests/ExtraModDeployerTests.cs`
- Modify: `src/Runner/Commands/RunCommandOptions.cs`
- Modify: `src/Runner/Commands/RunCommand.cs`
- Modify: `tests/Runner.Tests/RunCommandTests.cs`
- Modify: `src/Runner/Commands/RunSuiteCommand.cs`
- Modify: `tests/Runner.Tests/RunSuiteCommandTests.cs`

- [ ] **Step 1: Write failing deployer overlay tests**

Add these tests to `ExtraModDeployerTests`:

```csharp
[Fact]
public void ApplyConfigOverlays_CopiesSourceIntoDeployedMod()
{
    var root = CreateTempDirectory();
    try
    {
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "Example.Mod"));
        File.WriteAllText(Path.Combine(mods, "Example.Mod", "manifest.json"), """{"UniqueID":"Example.Mod"}""");
        var source = Path.Combine(root, "overlays", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, """{"Enabled":true}""");

        ExtraModDeployer.ApplyConfigOverlays(
            mods,
            [new ExtraModConfigOverlay(source, "Example.Mod", "config.json")]);

        Assert.Equal("""{"Enabled":true}""", File.ReadAllText(Path.Combine(mods, "Example.Mod", "config.json")));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

[Theory]
[InlineData("../escape.json")]
[InlineData("/tmp/escape.json")]
public void ApplyConfigOverlays_RejectsTargetPathsOutsideMod(string targetPath)
{
    var root = CreateTempDirectory();
    try
    {
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "Example.Mod"));
        File.WriteAllText(Path.Combine(mods, "Example.Mod", "manifest.json"), """{"UniqueID":"Example.Mod"}""");
        var source = Path.Combine(root, "config.json");
        File.WriteAllText(source, "{}");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExtraModDeployer.ApplyConfigOverlays(
                mods,
                [new ExtraModConfigOverlay(source, "Example.Mod", targetPath)]));

        Assert.Contains("overlay target must stay inside deployed mod", ex.Message);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

[Fact]
public void ApplyConfigOverlays_MissingTargetModThrowsClearError()
{
    var root = CreateTempDirectory();
    try
    {
        var source = Path.Combine(root, "config.json");
        File.WriteAllText(source, "{}");

        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            ExtraModDeployer.ApplyConfigOverlays(
                Path.Combine(root, "mods"),
                [new ExtraModConfigOverlay(source, "Missing.Mod", "config.json")]));

        Assert.Contains("Missing.Mod", ex.Message);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
```

Use the existing temporary-directory helper in the file, or add this helper if none exists:

```csharp
private static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "extra-mod-deployer-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}
```

- [ ] **Step 2: Run deployer tests and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ExtraModDeployerTests"
```

Expected: compile failure because `ExtraModConfigOverlay` and `ApplyConfigOverlays` do not exist.

- [ ] **Step 3: Add overlay value type**

Create `src/Protocol/ExtraModConfigOverlay.cs`:

```csharp
namespace SdvTestFramework.Protocol;

public sealed record ExtraModConfigOverlay(
    string SourcePath,
    string TargetModUniqueId,
    string TargetRelativePath);
```

- [ ] **Step 4: Implement overlay copy with path safety**

In `ExtraModDeployer`, add:

```csharp
public static void ApplyConfigOverlays(string modsPath, IEnumerable<ExtraModConfigOverlay> overlays)
{
    if (string.IsNullOrWhiteSpace(modsPath))
        throw new ArgumentException("modsPath required", nameof(modsPath));

    var fullModsPath = NormalizeDirectoryPath(modsPath);
    foreach (var overlay in overlays)
    {
        if (string.IsNullOrWhiteSpace(overlay.SourcePath))
            throw new ArgumentException("overlay source path required", nameof(overlays));
        if (string.IsNullOrWhiteSpace(overlay.TargetModUniqueId))
            throw new ArgumentException("overlay target mod id required", nameof(overlays));
        if (string.IsNullOrWhiteSpace(overlay.TargetRelativePath))
            throw new ArgumentException("overlay target path required", nameof(overlays));
        if (Path.IsPathRooted(overlay.TargetRelativePath))
            throw new InvalidOperationException("overlay target must stay inside deployed mod.");
        if (!File.Exists(overlay.SourcePath))
            throw new FileNotFoundException($"overlay source not found: {overlay.SourcePath}", overlay.SourcePath);

        var targetModDir = Path.Combine(fullModsPath, SanitizeFolderName(overlay.TargetModUniqueId));
        if (!Directory.Exists(targetModDir))
            throw new DirectoryNotFoundException($"overlay target mod not found: {overlay.TargetModUniqueId} at {targetModDir}");

        var targetPath = Path.GetFullPath(Path.Combine(targetModDir, overlay.TargetRelativePath));
        if (!IsSubPathOf(targetPath, targetModDir) && !PathsEqual(targetPath, targetModDir))
            throw new InvalidOperationException("overlay target must stay inside deployed mod.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(overlay.SourcePath, targetPath, overwrite: true);
        File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(overlay.SourcePath));
    }
}
```

- [ ] **Step 5: Run deployer tests and confirm GREEN**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ExtraModDeployerTests"
```

Expected: all `ExtraModDeployerTests` pass.

- [ ] **Step 6: Write failing run-command overlay test**

Add this test to `RunCommandTests` after `Run_ExtraModFlag_DeploysModIntoModsDir`:

```csharp
[Fact]
public async Task Run_ConfigOverlayFlag_AppliesOverlayAfterExtraModDeploy()
{
    var root = Path.Combine(Path.GetTempPath(), $"run-overlay-{Guid.NewGuid():N}");
    var mods = Path.Combine(root, "mods");
    var scenarios = Path.Combine(root, "scenarios");
    var extra = Path.Combine(root, "extra");
    var overlay = Path.Combine(root, "overlay.json");
    Directory.CreateDirectory(mods);
    Directory.CreateDirectory(scenarios);
    Directory.CreateDirectory(extra);
    try
    {
        File.WriteAllText(
            Path.Combine(extra, "manifest.json"),
            "{\"Name\":\"Probe\",\"UniqueID\":\"Example.Probe\",\"EntryDll\":\"Probe.dll\"}");
        File.WriteAllText(Path.Combine(extra, "Probe.dll"), "not real");
        File.WriteAllText(overlay, """{"Enabled":true}""");

        var outW = new StringWriter();
        var priorOut = Console.Out;
        Console.SetOut(outW);
        int exit;
        try
        {
            exit = await RunCommand.RunAsync(
                new ReadOnlyMemory<string>(new[]
                {
                    "--mods-path", mods,
                    "--extra-mod", extra,
                    "--config-overlay", overlay, "Example.Probe", "config.json",
                    scenarios,
                }),
                CancellationToken.None);
        }
        finally
        {
            Console.SetOut(priorOut);
        }

        Assert.Equal(0, exit);
        Assert.Equal("""{"Enabled":true}""", File.ReadAllText(Path.Combine(mods, "Example.Probe", "config.json")));
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
```

- [ ] **Step 7: Run run-command test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "Run_ConfigOverlayFlag_AppliesOverlayAfterExtraModDeploy"
```

Expected: exit is `2` or the overlay file is missing because `--config-overlay` is treated as a scenario path.

- [ ] **Step 8: Extend run options and argument parsing**

Modify `RunCommandOptions` constructor fields to include:

```csharp
string? ProfileId,
string? ProfileCacheNamespace,
IReadOnlyList<ExtraModConfigOverlay> ConfigOverlays,
```

Update all `new RunCommandOptions(...)` calls to pass:

```csharp
ProfileId: null,
ProfileCacheNamespace: null,
ConfigOverlays: Array.Empty<ExtraModConfigOverlay>(),
```

In `RunCommand.RunAsync`, add local variables:

```csharp
string? profileId = null;
string? profileCacheNamespace = null;
var configOverlays = new List<ExtraModConfigOverlay>();
```

Add parsing cases:

```csharp
if (a == "--profile-id" && i + 1 < args.Length) { profileId = args.Span[++i]; continue; }
if (a == "--profile-cache-namespace" && i + 1 < args.Length) { profileCacheNamespace = args.Span[++i]; continue; }
if (a == "--config-overlay" && i + 3 < args.Length)
{
    var source = args.Span[++i];
    var targetMod = args.Span[++i];
    var targetPath = args.Span[++i];
    configOverlays.Add(new ExtraModConfigOverlay(source, targetMod, targetPath));
    continue;
}
```

Pass the parsed values into `RunCommandOptions`.

- [ ] **Step 9: Apply overlays after mod deployment**

In `RunCommand.RunFromOptions`, after `ExtraModDeployer.DeployMany(...)`, add:

```csharp
ExtraModDeployer.ApplyConfigOverlays(modsPath, opts.ConfigOverlays);
```

Keep this inside the existing `[extra-mod]` exception handler so overlay failures return exit code `2`.

- [ ] **Step 10: Pass overlay flags through run-suite**

In `RunSuiteCommand.ParseArgs`, extend the multi-value option handling:

```csharp
case "--profile-id":
case "--profile-cache-namespace":
case "--mods-path":
case "--extra-mod":
case "--tier":
case "--diff-format":
    if (!TryReadValue(args, ref i, value, out var argValue, out var error))
        return ParseResult.Fail(error);
    passThrough.Add(value);
    passThrough.Add(argValue!);
    continue;

case "--config-overlay":
    if (!TryReadValue(args, ref i, value, out var source, out var sourceError))
        return ParseResult.Fail(sourceError);
    if (!TryReadValue(args, ref i, value, out var targetMod, out var targetModError))
        return ParseResult.Fail(targetModError);
    if (!TryReadValue(args, ref i, value, out var targetPath, out var targetPathError))
        return ParseResult.Fail(targetPathError);
    passThrough.Add(value);
    passThrough.Add(source!);
    passThrough.Add(targetMod!);
    passThrough.Add(targetPath!);
    continue;
```

Add a `RunSuiteCommandTests` case:

```csharp
[Fact]
public async Task RunSuite_ConfigOverlayFlag_IsPassedToChildRun()
{
    var root = CreateTempDirectory();
    try
    {
        var scenario = Path.Combine(root, "a.test.json");
        File.WriteAllText(scenario, """{"name":"a","steps":[]}""");
        var calls = new List<string[]>();
        var original = RunSuiteCommand.RunExecutor;
        RunSuiteCommand.RunExecutor = (args, _) =>
        {
            calls.Add(args.ToArray());
            return Task.FromResult(0);
        };

        try
        {
            var exit = await RunSuiteCommand.RunAsync(
                new[]
                {
                    "--config-overlay", "/tmp/source.json", "Example.Mod", "config.json",
                    "--profile-id", "profile-a",
                    root,
                }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            RunSuiteCommand.RunExecutor = original;
        }

        var call = Assert.Single(calls);
        Assert.Contains("--config-overlay", call);
        Assert.Contains("/tmp/source.json", call);
        Assert.Contains("Example.Mod", call);
        Assert.Contains("config.json", call);
        Assert.Contains("--profile-id", call);
        Assert.Contains("profile-a", call);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
```

Use the existing `CreateTempDirectory` helper in `RunSuiteCommandTests`, or add the same helper shape used in runner tests.

- [ ] **Step 11: Run focused tests and confirm GREEN**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ExtraModDeployerTests"
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "Run_ConfigOverlayFlag_AppliesOverlayAfterExtraModDeploy|RunSuite_ConfigOverlayFlag_IsPassedToChildRun|FullyQualifiedName~RunCommandTests|FullyQualifiedName~RunSuiteCommandTests"
```

Expected: selected protocol and runner tests pass.

- [ ] **Step 12: Commit**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/ExtraModConfigOverlay.cs src/Protocol/ExtraModDeployer.cs src/Runner/Commands/RunCommandOptions.cs src/Runner/Commands/RunCommand.cs src/Runner/Commands/RunSuiteCommand.cs tests/Protocol.Tests/ExtraModDeployerTests.cs tests/Runner.Tests/RunCommandTests.cs tests/Runner.Tests/RunSuiteCommandTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: apply repo profile config overlays"
```

## Task 4: Repo Planner Profile Cache And Scenario Routing

**Files:**
- Modify: `src/Runner/Repo/RepoRunPlanner.cs`
- Modify: `tests/Runner.Tests/Repo/RepoRunPlannerTests.cs`
- Modify: `src/Runner/Commands/RepoCommand.cs`
- Modify: `tests/Runner.Tests/Repo/RepoCommandTests.cs`

- [ ] **Step 1: Write failing planner tests**

Add this test to `RepoRunPlannerTests`:

```csharp
[Fact]
public void BuildRunPlan_profile_adds_profile_mods_metadata_overlays_and_isolated_mods_path()
{
    Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
    var core = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Core")).FullName;
    var farm = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "GrandpasFarm")).FullName;
    var overlay = Path.Combine(_repoRoot, "tests", "config", "content-patcher.json");
    Directory.CreateDirectory(Path.GetDirectoryName(overlay)!);
    File.WriteAllText(overlay, "{}");
    var config = Config(
        defaultTarget: "tests/scenarios",
        profiles: new Dictionary<string, RepoProfileConfig>
        {
            ["sve-core"] = new() { ExtraMods = ["mods/Core"] },
            ["sve-grandpas-farm"] = new()
            {
                Inherits = "sve-core",
                ExtraMods = ["mods/GrandpasFarm"],
                CacheNamespace = "sve-grandpas-farm",
                ConfigOverlays =
                [
                    new RepoConfigOverlayConfig
                    {
                        Source = "tests/config/content-patcher.json",
                        TargetMod = "Pathoschild.ContentPatcher",
                        TargetPath = "config.json",
                    },
                ],
            },
        });

    var plan = RepoRunPlanner.BuildRunPlan(
        _repoRoot,
        config,
        new RepoRunRequest(false, false, false, false, "sve-grandpas-farm", null, Array.Empty<string>()),
        new Dictionary<string, string?>());

    Assert.Equal("sve-grandpas-farm", plan.ProfileId);
    Assert.Equal("sve-grandpas-farm", plan.ProfileCacheNamespace);
    Assert.Equal(Path.Combine(_repoRoot, ".cache", "frobby-test-mods", "sve-grandpas-farm"), plan.ModsPath);
    Assert.Equal([core, farm], plan.ExtraMods);
    Assert.Contains("--mods-path", plan.FrobbyArgs);
    Assert.Contains(plan.ModsPath, plan.FrobbyArgs);
    Assert.Contains("--profile-id", plan.FrobbyArgs);
    Assert.Contains("sve-grandpas-farm", plan.FrobbyArgs);
    Assert.Contains("--profile-cache-namespace", plan.FrobbyArgs);
    Assert.Contains("--config-overlay", plan.FrobbyArgs);
    Assert.Contains(overlay, plan.FrobbyArgs);
    Assert.Contains("Pathoschild.ContentPatcher", plan.FrobbyArgs);
    Assert.Contains("config.json", plan.FrobbyArgs);
}
```

Extend the `Config` helper signature and object initializer:

```csharp
IReadOnlyDictionary<string, RepoProfileConfig>? profiles = null
```

```csharp
Profiles = profiles ?? new Dictionary<string, RepoProfileConfig>(),
```

- [ ] **Step 2: Run planner tests and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "BuildRunPlan_profile_adds_profile_mods_metadata_overlays_and_isolated_mods_path"
```

Expected: compile failure because `RepoRunPlan` has no profile or mods-path properties.

- [ ] **Step 3: Extend run plan and profile resolution**

Modify `RepoRunPlan` to:

```csharp
public sealed record RepoRunPlan(
    string RepoRoot,
    IReadOnlyList<string>? BuildCommand,
    List<string> FrobbyArgs,
    string ReportDir,
    IReadOnlyList<string> ExtraMods,
    string ModsPath,
    string ProfileId,
    string ProfileCacheNamespace,
    IReadOnlyList<ExtraModConfigOverlay> ConfigOverlays);
```

In `BuildRunPlan`, replace direct mod-set selection with:

```csharp
var profile = RepoProfileResolver.Resolve(
    fullRepoRoot,
    config,
    request.ModSet,
    environment,
    requireRepoExtraMods: request.NoBuild);
var extraMods = profile.ExtraMods;
var modsPath = Path.Combine(fullRepoRoot, ".cache", "frobby-test-mods", profile.CacheNamespace);
```

Before adding `--extra-mod`, add:

```csharp
frobbyArgs.Add("--mods-path");
frobbyArgs.Add(modsPath);
frobbyArgs.Add("--profile-id");
frobbyArgs.Add(profile.Id);
frobbyArgs.Add("--profile-cache-namespace");
frobbyArgs.Add(profile.CacheNamespace);
```

After adding extra mods, add:

```csharp
foreach (var overlay in profile.ConfigOverlays)
{
    frobbyArgs.Add("--config-overlay");
    frobbyArgs.Add(overlay.SourcePath);
    frobbyArgs.Add(overlay.TargetModUniqueId);
    frobbyArgs.Add(overlay.TargetRelativePath);
}
```

Return the new plan fields.

- [ ] **Step 4: Keep legacy planner tests passing**

Update existing `RepoRunPlannerTests` assertions that construct or inspect `RepoRunPlan`:

```csharp
Assert.Equal("core", plan.ProfileId);
Assert.Equal("core", plan.ProfileCacheNamespace);
Assert.Equal(Path.Combine(_repoRoot, ".cache", "frobby-test-mods", "core"), plan.ModsPath);
Assert.Contains("--mods-path", plan.FrobbyArgs);
Assert.Contains(plan.ModsPath, plan.FrobbyArgs);
```

The existing `--extra-mod` counts should stay unchanged.

- [ ] **Step 5: Write failing repo command routing test**

Add this test to `RepoCommandTests`:

```csharp
[Fact]
public async Task RepoRun_DirectoryWithScenarioProfiles_RunsEachScenarioWithDeclaredProfile()
{
    var root = CreateTempDirectory();
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "tests", "sdv"));
        Directory.CreateDirectory(Path.Combine(root, "mods", "Core"));
        Directory.CreateDirectory(Path.Combine(root, "mods", "GrandpasFarm"));
        File.WriteAllText(Path.Combine(root, "tests", "sdv", "01-core.test.json"), """{"name":"core","steps":[]}""");
        File.WriteAllText(Path.Combine(root, "tests", "sdv", "20-grandpa.test.json"), """{"name":"grandpa","profile":"grandpas","steps":[]}""");
        WriteConfig(root,
            """
            {
              "project": { "name": "Example", "slug": "example", "version": "1.0.0" },
              "build": { "command": "dotnet" },
              "defaultTarget": "tests/sdv",
              "modSets": [
                { "name": "core", "extraMods": ["mods/Core"] }
              ],
              "profiles": {
                "grandpas": { "extraMods": ["mods/GrandpasFarm"], "cacheNamespace": "grandpas" }
              }
            }
            """);

        var calls = new List<IReadOnlyList<string>>();
        var original = RepoCommand.RunExecutor;
        RepoCommand.RunExecutor = (args, _) =>
        {
            calls.Add(args.ToArray());
            return Task.FromResult(0);
        };

        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "run", "--repo-root", root, "--no-build", "--headless" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
        }
        finally
        {
            RepoCommand.RunExecutor = original;
        }

        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Equal("run", call[0]));
        Assert.Contains(calls, call =>
            call.Contains("--profile-id")
            && call.Contains("core")
            && call.Any(arg => arg.EndsWith("01-core.test.json", StringComparison.Ordinal)));
        Assert.Contains(calls, call =>
            call.Contains("--profile-id")
            && call.Contains("grandpas")
            && call.Any(arg => arg.EndsWith("20-grandpa.test.json", StringComparison.Ordinal)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
```

Use existing helpers in `RepoCommandTests` if present. If not present, add:

```csharp
private static void WriteConfig(string root, string json)
    => File.WriteAllText(Path.Combine(root, RepoTestConfig.FileName), json);

private static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "repo-command-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}
```

- [ ] **Step 6: Run repo command test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "RepoRun_DirectoryWithScenarioProfiles_RunsEachScenarioWithDeclaredProfile"
```

Expected: a single `run-suite` call is recorded instead of per-scenario `run` calls.

- [ ] **Step 7: Add profile-aware scenario orchestration**

In `RepoCommand.RunRepoRunAsync`, after loading config and environment, load selected scenario paths before planning:

```csharp
var profiledScenarioPlans = BuildProfiledScenarioPlans(options, config, environment);
if (profiledScenarioPlans is { Count: > 0 })
{
    if (!options.DryRun)
    {
        var buildPlan = RepoRunPlanner.BuildRunPlan(options.RepoRoot, config, options.ToRequest(), environment);
        var buildExit = await RunBuildIfNeededAsync(buildPlan, ct);
        if (buildExit != 0)
        {
            return buildExit;
        }
    }

    var worstExit = 0;
    foreach (var plan in profiledScenarioPlans)
    {
        if (options.DryRun)
        {
            PrintDryRun(plan);
            continue;
        }

        var exit = await RunExecutor(plan.FrobbyArgs, ct);
        worstExit = Math.Max(worstExit, exit);
    }

    return worstExit;
}
```

Add helper methods:

```csharp
private static List<RepoRunPlan> BuildProfiledScenarioPlans(
    RunOptions options,
    RepoTestConfig config,
    IReadOnlyDictionary<string, string?> environment)
{
    var scenarios = DiscoverRepoScenarios(
        options.RepoRoot,
        options.Targets.Count > 0 ? options.Targets : [RequireText(config.DefaultTarget, "defaultTarget")],
        environment);
    if (!string.IsNullOrWhiteSpace(options.Filter))
    {
        scenarios = scenarios
            .Where(item => item.Spec.Name.Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    if (scenarios.All(item => string.IsNullOrWhiteSpace(item.Spec.Profile)))
    {
        return new List<RepoRunPlan>();
    }

    var plans = new List<RepoRunPlan>();
    foreach (var (path, spec) in scenarios)
    {
        var profileName = string.IsNullOrWhiteSpace(spec.Profile) ? options.ModSet : spec.Profile;
        var request = options with
        {
            NoBuild = true,
            ModSet = profileName,
            Targets = [path],
        };
        plans.Add(RepoRunPlanner.BuildRunPlan(options.RepoRoot, config, request.ToRequest(), environment));
    }

    return plans;
}

private static List<(string Path, ScenarioSpec Spec)> DiscoverRepoScenarios(
    string repoRoot,
    IReadOnlyList<string> rawTargets,
    IReadOnlyDictionary<string, string?> environment)
{
    var scenarios = new List<(string Path, ScenarioSpec Spec)>();
    foreach (var target in rawTargets)
    {
        var resolved = RepoPathResolver.Resolve(repoRoot, target, environment, requireExists: true);
        if (File.Exists(resolved))
        {
            scenarios.Add((resolved, SdvTestFramework.Protocol.Scenarios.ScenarioLoader.Load(resolved)));
            continue;
        }

        foreach (var file in Directory.EnumerateFiles(resolved, "*.test.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            scenarios.Add((file, SdvTestFramework.Protocol.Scenarios.ScenarioLoader.Load(file)));
        }
    }

    return scenarios;
}
```

Add `using SdvTestFramework.Protocol.Models;` at the top of `RepoCommand.cs` for the `ScenarioSpec` helper signature.

If `RunOptions` is a private record without `Filter`, add `Filter` to it and thread the parsed `--filter` value from `ParseRunOptions`. If `ParseRunOptions` does not currently support `--filter`, add support so profiled directory runs preserve existing filtering expectations:

```csharp
case "--filter":
    filter = ReadRequiredValue(args, ref i, value);
    continue;
```

The non-profile path should continue to use the existing single `RepoRunPlanner.BuildRunPlan` and `run-suite` flow.

- [ ] **Step 8: Run focused repo planner and command tests**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~RepoRunPlannerTests|FullyQualifiedName~RepoCommandTests"
```

Expected: planner and repo command tests pass.

- [ ] **Step 9: Commit**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Runner/Repo/RepoRunPlanner.cs src/Runner/Commands/RepoCommand.cs tests/Runner.Tests/Repo/RepoRunPlannerTests.cs tests/Runner.Tests/Repo/RepoCommandTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: route repo scenarios by profile"
```

## Task 5: Profile Metadata In Reports

**Files:**
- Modify: `src/Protocol/Reports/RunSummary.cs`
- Modify: `src/Runner/Reports/RunMetadataBuilder.cs`
- Modify: `src/Runner/Reports/HtmlReportGenerator.cs`
- Test: `tests/Runner.Tests/Reports/RunMetadataBuilderTests.cs`
- Test: `tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs`

- [ ] **Step 1: Write failing metadata test**

Add this to `RunMetadataBuilderTests`:

```csharp
[Fact]
public void Build_RecordsProfileMetadataWhenPresent()
{
    var opts = new RunCommandOptions(
        Paths: new[] { "tests/sdv/20-profile.test.json" },
        Filter: null,
        ModsPath: "/tmp/example-mods",
        ExtraMods: new[] { "/tmp/extra-a", "/tmp/extra-b" },
        ReporterName: "console",
        OutputPath: null,
        Watch: false,
        UpdateBaselines: false,
        ReportDirPath: null,
        NoReport: false,
        DiffFormat: DiffFormat.Files,
        Tier: "generic",
        NoCacheCleanup: false,
        Headless: true,
        PreCreatedRunDir: null,
        ProfileId: "sve-grandpas-farm",
        ProfileCacheNamespace: "sve-grandpas-farm",
        ConfigOverlays:
        [
            new SdvTestFramework.Protocol.ExtraModConfigOverlay(
                "/tmp/source.json",
                "Pathoschild.ContentPatcher",
                "config.json"),
        ]);

    var metadata = RunMetadataBuilder.Build(
        opts,
        effectiveHeadless: true,
        launcher: "xvfb-run",
        command: "sdv-test run",
        workingDirectory: Directory.GetCurrentDirectory());

    Assert.NotNull(metadata.Profile);
    Assert.Equal("sve-grandpas-farm", metadata.Profile.Id);
    Assert.Equal("sve-grandpas-farm", metadata.Profile.CacheNamespace);
    Assert.Equal("/tmp/example-mods", metadata.Profile.ModsPath);
    Assert.Equal(["/tmp/extra-a", "/tmp/extra-b"], metadata.Profile.ExtraMods);
    var overlay = Assert.Single(metadata.Profile.ConfigOverlays);
    Assert.Equal("/tmp/source.json", overlay.SourcePath);
    Assert.Equal("Pathoschild.ContentPatcher", overlay.TargetModUniqueId);
    Assert.Equal("config.json", overlay.TargetRelativePath);
}
```

- [ ] **Step 2: Run metadata test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "Build_RecordsProfileMetadataWhenPresent"
```

Expected: compile failure because `RunMetadata.Profile` does not exist.

- [ ] **Step 3: Add report metadata records**

In `RunSummary.cs`, change `RunMetadata` to:

```csharp
public sealed record RunMetadata(
    string Command,
    string WorkingDirectory,
    string LaunchMode,
    bool Headless,
    string Launcher,
    IReadOnlyList<RunRepositoryMetadata> Repositories)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunProfileMetadata? Profile { get; init; }
}
```

Add:

```csharp
public sealed record RunProfileMetadata(
    string Id,
    string CacheNamespace,
    string? ModsPath,
    IReadOnlyList<string> ExtraMods,
    IReadOnlyList<RunConfigOverlayMetadata> ConfigOverlays);

public sealed record RunConfigOverlayMetadata(
    string SourcePath,
    string TargetModUniqueId,
    string TargetRelativePath);
```

- [ ] **Step 4: Populate metadata**

In `RunMetadataBuilder.Build`, assign the record to a local and attach profile metadata:

```csharp
var metadata = new RunMetadata(
    Command: command ?? Environment.CommandLine,
    WorkingDirectory: cwd,
    LaunchMode: effectiveHeadless ? "headless" : "windowed",
    Headless: effectiveHeadless,
    Launcher: launcher,
    Repositories: repositories);

if (!string.IsNullOrWhiteSpace(opts.ProfileId))
{
    metadata = metadata with
    {
        Profile = new RunProfileMetadata(
            opts.ProfileId!,
            string.IsNullOrWhiteSpace(opts.ProfileCacheNamespace) ? opts.ProfileId! : opts.ProfileCacheNamespace!,
            opts.ModsPath,
            opts.ExtraMods.ToArray(),
            opts.ConfigOverlays
                .Select(overlay => new RunConfigOverlayMetadata(
                    overlay.SourcePath,
                    overlay.TargetModUniqueId,
                    overlay.TargetRelativePath))
                .ToArray()),
    };
}

return metadata;
```

- [ ] **Step 5: Write failing HTML test**

Add this test to `HtmlReportGeneratorTests`:

```csharp
[Fact]
public void Generate_RendersProfileMetadata()
{
    var rd = RunDirectory.Create(CreateTempDirectory(), "profile-run", replaceExisting: true);
    var summary = new RunSummary(
        "profile-run",
        DateTime.UtcNow.ToString("o"),
        1,
        Array.Empty<ScenarioOutcome>())
    {
        Metadata = new RunMetadata(
            "sdv-test run",
            "/repo",
            "headless",
            true,
            "xvfb-run",
            Array.Empty<RunRepositoryMetadata>())
        {
            Profile = new RunProfileMetadata(
                "sve-grandpas-farm",
                "sve-grandpas-farm",
                "/repo/.cache/frobby-test-mods/sve-grandpas-farm",
                ["/repo/Grandpa's Farm/[CP] Grandpa's Farm"],
                [new RunConfigOverlayMetadata("/repo/config.json", "Pathoschild.ContentPatcher", "config.json")]),
        },
    };

    HtmlReportGenerator.Generate(rd, summary);

    var html = File.ReadAllText(Path.Combine(rd.Root, "index.html"));
    Assert.Contains("sve-grandpas-farm", html);
    Assert.Contains("frobby-test-mods", html);
    Assert.Contains("Pathoschild.ContentPatcher", html);
}
```

- [ ] **Step 6: Render profile metadata in HTML**

In `HtmlReportGenerator`, find the run metadata rendering block and add:

```csharp
if (summary.Metadata?.Profile is { } profile)
{
    sb.AppendLine("<section class=\"run-profile\">");
    sb.AppendLine("<h2>Profile</h2>");
    sb.AppendLine("<dl>");
    sb.AppendLine($"<dt>ID</dt><dd>{Html(profile.Id)}</dd>");
    sb.AppendLine($"<dt>Cache namespace</dt><dd>{Html(profile.CacheNamespace)}</dd>");
    if (!string.IsNullOrWhiteSpace(profile.ModsPath))
        sb.AppendLine($"<dt>Mods path</dt><dd>{Html(profile.ModsPath)}</dd>");
    sb.AppendLine("</dl>");
    if (profile.ExtraMods.Count > 0)
    {
        sb.AppendLine("<h3>Staged mod sources</h3><ul>");
        foreach (var extraMod in profile.ExtraMods)
            sb.AppendLine($"<li>{Html(extraMod)}</li>");
        sb.AppendLine("</ul>");
    }
    if (profile.ConfigOverlays.Count > 0)
    {
        sb.AppendLine("<h3>Config overlays</h3><ul>");
        foreach (var overlay in profile.ConfigOverlays)
            sb.AppendLine($"<li>{Html(overlay.SourcePath)} -> {Html(overlay.TargetModUniqueId)}/{Html(overlay.TargetRelativePath)}</li>");
        sb.AppendLine("</ul>");
    }
    sb.AppendLine("</section>");
}
```

Use the existing HTML escaping helper name in the file. If the helper is named `H`, use `H(...)` instead of `Html(...)`.

- [ ] **Step 7: Run focused report tests**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "Build_RecordsProfileMetadataWhenPresent|Generate_RendersProfileMetadata|FullyQualifiedName~HtmlReportGeneratorTests|FullyQualifiedName~RunMetadataBuilderTests"
```

Expected: metadata and HTML report tests pass.

- [ ] **Step 8: Commit**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Protocol/Reports/RunSummary.cs src/Runner/Reports/RunMetadataBuilder.cs src/Runner/Reports/HtmlReportGenerator.cs tests/Runner.Tests/Reports/RunMetadataBuilderTests.cs tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "feat: report repo profile metadata"
```

## Task 6: Documentation And Generated Scaffold Updates

**Files:**
- Modify: `src/Runner/Repo/RepoScaffoldGenerator.cs`
- Modify: `tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs`
- Modify: `README.md`
- Modify: `docs/rpc-schema.md`
- Modify: `docs/dsl-quickstart.md`

- [ ] **Step 1: Write failing scaffold docs test**

In `RepoScaffoldGeneratorTests.Generate_scripts_and_docs_reference_repo_commands_without_project_specific_names`, add:

```csharp
Assert.Contains("\"profiles\"", docsText);
Assert.Contains("\"profile\"", docsText);
Assert.Contains("configOverlays", docsText);
Assert.Contains(".cache/frobby-test-mods", docsText);
```

- [ ] **Step 2: Run scaffold test and confirm RED**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "Generate_scripts_and_docs_reference_repo_commands_without_project_specific_names"
```

Expected: docs test fails because generated docs do not mention profiles.

- [ ] **Step 3: Update generated scaffold docs**

In `RepoScaffoldGenerator.Docs()`, add this section after "Dependency mods":

```markdown
## Test profiles

Use `profiles` when a scenario needs a different mod/config set than the default
core suite. A scenario declares its environment with a top-level `profile` field:

```json
{
  "name": "alternate_pack_loads",
  "profile": "alternate-pack",
  "steps": []
}
```

Profiles can inherit shared dependencies and repo-owned mods:

```json
"profiles": {
  "core": {
    "deps": [{ "id": "Pathoschild.ContentPatcher" }],
    "extraMods": ["bin/Release/net6.0"]
  },
  "alternate-pack": {
    "inherits": "core",
    "extraMods": ["packs/Alternate Pack"],
    "cacheNamespace": "alternate-pack"
  }
}
```

Profile runs stage mods into `.cache/frobby-test-mods/<cacheNamespace>/`, which
is separate from the playable Stardew `Mods` folder. Use `configOverlays` only
when a profile needs to copy a known repo file into a staged mod folder before
launch.
```

Escape the nested markdown fences inside the C# raw string by using a longer raw string delimiter if needed.

- [ ] **Step 4: Update Frobby docs**

Add a short "Repo profiles" subsection to `README.md` under "Repo Dependency Cache":

```markdown
### Repo Profiles

Large mods can define `profiles` in `sdv-test.config.json` for alternate packs
or config-gated runs. Scenarios select a profile with top-level `"profile":
"profile-id"`. Repo runs stage each profile into `.cache/frobby-test-mods/<id>/`
and keep the user's playable Stardew `Mods` folder untouched.
```

Add scenario-schema and repo-config notes to `docs/rpc-schema.md`:

```markdown
Scenario files may include top-level `"profile": "profile-id"` when run through
`sdv-test repo run`. The profile is resolved from `sdv-test.config.json` before
Stardew launches.
```

Add authoring guidance to `docs/dsl-quickstart.md`:

```markdown
Use repo profiles for alternate mod/config packs. Keep pack-specific paths in
`sdv-test.config.json`; keep assertions in scenario JSON.
```

- [ ] **Step 5: Run docs/scaffold tests**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "FullyQualifiedName~RepoScaffoldGeneratorTests"
```

Expected: scaffold generator tests pass and generated docs remain neutral.

- [ ] **Step 6: Commit**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add src/Runner/Repo/RepoScaffoldGenerator.cs tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs README.md docs/rpc-schema.md docs/dsl-quickstart.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: explain repo test profiles"
```

## Task 7: SVE Grandpa's Farm Proof Scenario

**Files:**
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json`
- Create: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/20-sve-grandpas-farm-profile.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update SVE config with profiles**

In `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json`, keep the existing `modSets` entry for compatibility and add:

```json
"profiles": {
  "sve-core": {
    "deps": [
      { "id": "Pathoschild.ContentPatcher" },
      { "id": "Esca.FarmTypeManager" }
    ],
    "extraMods": [
      ".cache/frobby-game-mods/StardewValleyExpanded/StardewValleyExpanded",
      ".cache/frobby-game-mods/StardewValleyExpanded/[CP] Stardew Valley Expanded",
      ".cache/frobby-game-mods/StardewValleyExpanded/[FTM] Stardew Valley Expanded"
    ],
    "cacheNamespace": "sve-core"
  },
  "sve-grandpas-farm": {
    "inherits": "sve-core",
    "extraMods": [
      "Grandpa's Farm/[CP] Grandpa's Farm",
      "Grandpa's Farm/[FTM] Grandpa's Farm"
    ],
    "cacheNamespace": "sve-grandpas-farm"
  }
}
```

- [ ] **Step 2: Create the SVE proof scenario**

Create `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/20-sve-grandpas-farm-profile.test.json`:

```json
{
  "name": "sve_grandpas_farm_profile",
  "profile": "sve-grandpas-farm",
  "fixture": "m0spike_436515781",
  "config": { "seed": 436515781 },
  "steps": [
    { "action": "time.set", "args": { "time": 900, "day": 1, "season": "spring", "year": 1 } },
    { "action": "player.warp", "args": { "location": "Farm", "x": 64, "y": 15 } },
    { "action": "wait.location", "args": { "location": "Farm", "timeout_ms": 10000, "poll_ms": 100 } },
    { "action": "freeze.begin", "args": {} },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'flashshifter.GrandpasFarm'",
      "message": "Grandpa's Farm Content Patcher pack should be loaded by the profile"
    },
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'FlashShifter.GrandpasFarmFTM'",
      "message": "Grandpa's Farm FTM pack should be loaded by the profile"
    },
    {
      "type": "content.asset",
      "asset": "Maps/Custom_GrandpasGrove",
      "asset_type": "map",
      "expr": "asset.width != 0",
      "message": "Grandpa's Farm profile should load the Grandpas Grove custom map"
    },
    {
      "type": "content.asset",
      "asset": "Data/Locations",
      "asset_type": "data",
      "entry_keys": ["Custom_GrandpasGrove", "Custom_FarmCliff"],
      "expr": "asset.entries.Custom_GrandpasGrove.exists == true",
      "message": "Grandpa's Farm profile should register Grandpas Grove location data"
    },
    {
      "type": "content.asset",
      "asset": "Data/Locations",
      "asset_type": "data",
      "entry_keys": ["Custom_FarmCliff"],
      "expr": "asset.entries.Custom_FarmCliff.exists == true",
      "message": "Grandpa's Farm profile should register Farm Cliff location data"
    }
  ]
}
```

- [ ] **Step 3: Document the SVE profile scenario**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, add:

```markdown
Scenario `tests/sdv/20-sve-grandpas-farm-profile.test.json` covers profile-based
alternate pack testing. It declares `profile: "sve-grandpas-farm"`, which stages
SVE core plus Grandpa's Farm CP/FTM packs into an isolated
`.cache/frobby-test-mods/sve-grandpas-farm/` test Mods folder. The scenario
asserts both Grandpa's Farm pack IDs are loaded and that profile-only runtime
locations such as `Custom_GrandpasGrove` and `Custom_FarmCliff` exist.
```

- [ ] **Step 4: Mark Slice 15 active before live verification**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change Slice 15 from:

```markdown
- [ ] Pending: Slice 15, config packs and alternate farm variants.
```

to:

```markdown
- [ ] Active: Slice 15, config packs and alternate farm variants.
```

Add:

```markdown
  - Design spec: `docs/superpowers/specs/2026-05-14-sve-slice-15-config-pack-profiles-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-05-14-sve-slice-15-config-pack-profiles.md`.
```

- [ ] **Step 5: Run SVE dry-run proof**

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --dry-run --headless tests/sdv/20-sve-grandpas-farm-profile.test.json
```

Expected dry-run output contains:

```text
--profile-id sve-grandpas-farm
--mods-path /home/fintan/stardewRepos/StardewValleyExpanded/.cache/frobby-test-mods/sve-grandpas-farm
Grandpa's Farm/[CP] Grandpa's Farm
Grandpa's Farm/[FTM] Grandpa's Farm
```

- [ ] **Step 6: Commit SVE scenario/config/docs**

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded add sdv-test.config.json tests/sdv/20-sve-grandpas-farm-profile.test.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: add grandpas farm frobby profile"
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: mark sve slice 15 active"
```

## Task 8: Full Verification And Completion

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Run focused Frobby unit tests**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Runner.Tests/Runner.Tests.csproj --filter "ScenarioLoaderTests|RepoTestConfigTests|RepoProfileResolverTests|RepoRunPlannerTests|RepoCommandTests|RunCommandTests|RunSuiteCommandTests|RunMetadataBuilderTests|HtmlReportGeneratorTests|RepoScaffoldGeneratorTests"
```

Expected: all selected runner tests pass.

- [ ] **Step 2: Run protocol deployer tests**

```bash
dotnet test /home/fintan/stardewRepos/frobby/sdv-test-framework/tests/Protocol.Tests/Protocol.Tests.csproj --filter "FullyQualifiedName~ExtraModDeployerTests"
```

Expected: all deployer tests pass.

- [ ] **Step 3: Run SVE Grandpa's Farm profile scenario headless**

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-15-grandpas-farm tests/sdv/20-sve-grandpas-farm-profile.test.json
```

Expected: scenario passes and report hub exists at:

```text
/tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-15-grandpas-farm/index.html
```

- [ ] **Step 4: Run SVE core smoke to catch default-profile regression**

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/StardewValleyExpanded/scripts/sdv-test --headless --mod-set core --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-15-core-smoke tests/sdv/01-sve-core-loads.test.json
```

Expected: scenario passes using profile metadata `core` or legacy mod-set metadata.

- [ ] **Step 5: Run Starberg smoke from the existing stonks repo**

Use a narrow smoke because the current Starberg worktree may contain unrelated dirty scenario edits from earlier drift investigation:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework /home/fintan/stardewRepos/stonks/scripts/sdv-test --headless --no-build --report-dir /tmp/starberg-frobby-results-0.1.0/slice-15-smoke tests/sdv/01-starberg-core-loads.test.json
```

Expected: the smoke scenario passes, proving default repo-local mod-set behavior still works for a non-SVE suite. Do not commit Starberg files as part of Slice 15.

- [ ] **Step 6: Mark Slice 15 done after live proof**

In `SVE_FROBBY_CAPABILITY_TODO.md`, change:

```markdown
- [ ] Active: Slice 15, config packs and alternate farm variants.
```

to:

```markdown
- [x] Done: Slice 15, config packs and alternate farm variants.
```

Add completion notes:

```markdown
  - Done: repo profiles, inherited profile resolution, profile-specific test Mods caches, scenario `profile` selection, config overlays, profile report metadata, and SVE scenario 20 against Grandpa's Farm.
  - Follow-up candidates: add Immersive Farm 2 Remastered and Frontier Farm profiles, and add config-overlay proofs for low-memory or bridge layout variants.
```

- [ ] **Step 7: Commit final Frobby docs status**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework add SVE_FROBBY_CAPABILITY_TODO.md
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework commit -m "docs: mark sve slice 15 complete"
```

- [ ] **Step 8: Final git status check**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
git -C /home/fintan/stardewRepos/stonks status --short --branch
```

Expected:

- Frobby is on `feature/sve-slice-15-config-pack-profiles` with only committed work.
- SVE is on `feature/frobby-sve-slice-15-config-pack-profiles` with only committed work.
- Starberg may remain dirty from unrelated earlier drift work; do not stage or commit it here.
