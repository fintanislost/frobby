# Repo-Local Mod Dependency Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Frobby-local, gitignored dependency cache so repo test runs can stage Content Patcher, Farm Type Manager, SpaceCore, and other dependency mods without reading the user's playable Stardew `Mods` folder.

**Architecture:** Extend `sdv-test.config.json` mod sets with a `deps` array keyed by SMAPI `UniqueID`. A new repo dependency cache service imports local mod folders into `.cache/deps/<UniqueID>`, validates cached manifests, and feeds resolved dependency paths into `RepoRunPlanner` before repo-owned `extraMods`.

**Tech Stack:** C#/.NET 10 runner, System.Text.Json, existing `ExtraModDeployer`, xUnit, Frobby repo wrappers, SVE core smoke scenarios.

---

## File Structure

Create these Frobby files:

- `src/Runner/Repo/RepoDependencyCache.cs` - cache root resolution, manifest reading, dependency import, dependency validation, and run-time dependency path resolution.
- `tests/Runner.Tests/Repo/RepoDependencyCacheTests.cs` - unit coverage for import, missing deps, bad manifests, ID mismatch, version mismatch, env override, and default root discovery.

Modify these Frobby files:

- `src/Runner/Repo/RepoTestConfig.cs` - add `RepoModDependencyConfig` and `RepoModSetConfig.Deps`.
- `tests/Runner.Tests/Repo/RepoTestConfigTests.cs` - verify `deps` parsing and required `id` validation.
- `src/Runner/Repo/RepoRunPlanner.cs` - prepend cached dependency paths before repo-owned `extraMods`.
- `tests/Runner.Tests/Repo/RepoRunPlannerTests.cs` - verify dependency ordering, missing dependency failure, and version mismatch failure.
- `src/Runner/Commands/RepoCommand.cs` - add `repo deps import` and `repo deps doctor`.
- `tests/Runner.Tests/Repo/RepoCommandTests.cs` - cover import, doctor success, doctor missing, doctor version mismatch, and dry-run output.
- `src/Runner/Program.cs` - update CLI help for `repo deps import` and `repo deps doctor`.
- `src/Runner/Repo/RepoScaffoldGenerator.cs` - document `deps` in generated `docs/FROBBY.md`.
- `tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs` - verify generated docs mention dependency import and doctor.
- `README.md` - document cache location, import flow, doctor flow, `deps` versus `extraMods`, and the fact that normal `repo run` does not touch the live Mods folder.
- `.gitignore` - add `.cache/`.

Modify these SVE files after Frobby support lands:

- `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json` - move external dependency mods from `${SDV_GAME_MODS}` `extraMods` into `deps`.
- `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md` - document SVE's dependency import flow.

Do not merge SVE changes into SVE `master` unless the user explicitly asks for that merge.

---

### Task 1: Config DTOs For `modSets[].deps`

**Files:**
- Modify: `src/Runner/Repo/RepoTestConfig.cs`
- Test: `tests/Runner.Tests/Repo/RepoTestConfigTests.cs`

- [ ] **Step 1: Write the failing deps parsing test**

Add this test to `tests/Runner.Tests/Repo/RepoTestConfigTests.cs`:

```csharp
[Fact]
public void Load_reads_mod_set_deps()
{
    WriteConfig(
        """
        {
          "project": { "name": "Frobby", "slug": "frobby", "version": "1.2.3" },
          "build": { "command": "dotnet" },
          "defaultTarget": "smoke",
          "modSets": [
            {
              "name": "core",
              "deps": [
                { "id": "Pathoschild.ContentPatcher", "version": "2.7.0" },
                { "id": "Esca.FarmTypeManager" }
              ],
              "extraMods": ["mods/Frobby"]
            }
          ]
        }
        """);

    var config = RepoTestConfig.Load(_repoRoot);

    var modSet = Assert.Single(config.ModSets);
    Assert.Equal("core", modSet.Name);
    Assert.Equal(2, modSet.Deps.Count);
    Assert.Equal("Pathoschild.ContentPatcher", modSet.Deps[0].Id);
    Assert.Equal("2.7.0", modSet.Deps[0].Version);
    Assert.Equal("Esca.FarmTypeManager", modSet.Deps[1].Id);
    Assert.Null(modSet.Deps[1].Version);
}
```

- [ ] **Step 2: Run the test red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoTestConfigTests
```

Expected: compile failure because `RepoModSetConfig` has no `Deps` property.

- [ ] **Step 3: Add the dependency config DTO**

Modify `src/Runner/Repo/RepoTestConfig.cs`:

```csharp
public sealed class RepoModSetConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("deps")]
    public IReadOnlyList<RepoModDependencyConfig> Deps { get; init; } = Array.Empty<RepoModDependencyConfig>();

    [JsonPropertyName("extraMods")]
    public IReadOnlyList<string> ExtraMods { get; init; } = Array.Empty<string>();
}

public sealed class RepoModDependencyConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}
```

- [ ] **Step 4: Run the parsing test green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoTestConfigTests
```

Expected: `RepoTestConfigTests` passes.

- [ ] **Step 5: Write the failing deps validation tests**

Add these inline data cases to `Load_validates_required_fields`:

```csharp
[InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","deps":[{}],"extraMods":["mods/a"]}]}""", "modSets[0].deps[0].id")]
[InlineData("""{"project":{"name":"Frobby","slug":"frobby","version":"1.0.0"},"build":{"command":"dotnet"},"defaultTarget":"smoke","modSets":[{"name":"smoke","deps":[{"id":" "}],"extraMods":["mods/a"]}]}""", "modSets[0].deps[0].id")]
```

Add this new test:

```csharp
[Fact]
public void Load_validates_dep_version_entry_when_present()
{
    WriteConfig(
        """
        {
          "project": { "name": "Frobby", "slug": "frobby", "version": "1.0.0" },
          "build": { "command": "dotnet" },
          "defaultTarget": "smoke",
          "modSets": [
            {
              "name": "smoke",
              "deps": [{ "id": "Pathoschild.ContentPatcher", "version": " " }],
              "extraMods": ["mods/a"]
            }
          ]
        }
        """);

    var ex = Assert.Throws<InvalidOperationException>(() => RepoTestConfig.Load(_repoRoot));

    Assert.Contains("modSets[0].deps[0].version", ex.Message);
    Assert.Contains("sdv-test.config.json", ex.Message);
}
```

- [ ] **Step 6: Run validation tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoTestConfigTests
```

Expected: validation tests fail because empty dependency IDs and blank versions are accepted.

- [ ] **Step 7: Validate dependency entries**

Add this call inside the existing `for` loop in `Validate`:

```csharp
ValidateDependencies(modSet.Deps, path, $"modSets[{i}].deps");
```

Add this method to `RepoTestConfig`:

```csharp
private static void ValidateDependencies(
    IReadOnlyList<RepoModDependencyConfig>? dependencies,
    string path,
    string field)
{
    if (dependencies is null)
    {
        return;
    }

    for (var i = 0; i < dependencies.Count; i++)
    {
        if (dependencies[i] is not { } dependency)
        {
            throw Missing(path, $"{field}[{i}]");
        }

        RequireText(dependency.Id, path, $"{field}[{i}].id");
        if (dependency.Version is not null)
        {
            RequireText(dependency.Version, path, $"{field}[{i}].version");
        }
    }
}
```

- [ ] **Step 8: Run validation tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoTestConfigTests
```

Expected: `RepoTestConfigTests` passes.

- [ ] **Step 9: Commit Task 1**

Run:

```bash
git add src/Runner/Repo/RepoTestConfig.cs tests/Runner.Tests/Repo/RepoTestConfigTests.cs
git commit -m "feat: parse repo mod dependency config"
```

---

### Task 2: Dependency Cache Service

**Files:**
- Create: `src/Runner/Repo/RepoDependencyCache.cs`
- Test: `tests/Runner.Tests/Repo/RepoDependencyCacheTests.cs`

- [ ] **Step 1: Write cache import and manifest tests**

Create `tests/Runner.Tests/Repo/RepoDependencyCacheTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public sealed class RepoDependencyCacheTests : IDisposable
{
    private readonly string _root = CreateTempDirectory();

    [Fact]
    public void Import_copies_mod_folder_to_cache_folder_named_by_unique_id()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var source = CreateMod("SourceContentPatcher", "Pathoschild.ContentPatcher", "2.7.0");
        File.WriteAllText(Path.Combine(source, "assets.txt"), "copied");

        var manifest = RepoDependencyCache.Import(source, Env(cacheRoot));

        Assert.Equal("Pathoschild.ContentPatcher", manifest.UniqueId);
        Assert.Equal("2.7.0", manifest.Version);
        var cached = Path.Combine(cacheRoot, "Pathoschild.ContentPatcher");
        Assert.True(File.Exists(Path.Combine(cached, "manifest.json")));
        Assert.Equal("copied", File.ReadAllText(Path.Combine(cached, "assets.txt")));
    }

    [Fact]
    public void Check_returns_missing_when_configured_dependency_is_not_cached()
    {
        var cacheRoot = Path.Combine(_root, "deps");

        var check = RepoDependencyCache.Check(
            new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher" },
            Env(cacheRoot));

        Assert.Equal(RepoDependencyStatus.Missing, check.Status);
        Assert.Contains("Pathoschild.ContentPatcher", check.Message);
        Assert.Contains("repo deps import --from", check.Message);
    }

    [Fact]
    public void Check_detects_unique_id_mismatch()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var cached = Path.Combine(cacheRoot, "Pathoschild.ContentPatcher");
        Directory.CreateDirectory(cached);
        WriteManifest(cached, "Other.Mod", "1.0.0");

        var check = RepoDependencyCache.Check(
            new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher" },
            Env(cacheRoot));

        Assert.Equal(RepoDependencyStatus.UniqueIdMismatch, check.Status);
        Assert.Contains("expected Pathoschild.ContentPatcher", check.Message);
        Assert.Contains("found Other.Mod", check.Message);
    }

    [Fact]
    public void Check_detects_version_mismatch()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var cached = Path.Combine(cacheRoot, "Pathoschild.ContentPatcher");
        Directory.CreateDirectory(cached);
        WriteManifest(cached, "Pathoschild.ContentPatcher", "2.6.0");

        var check = RepoDependencyCache.Check(
            new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher", Version = "2.7.0" },
            Env(cacheRoot));

        Assert.Equal(RepoDependencyStatus.VersionMismatch, check.Status);
        Assert.Contains("expected 2.7.0", check.Message);
        Assert.Contains("found 2.6.0", check.Message);
    }

    [Fact]
    public void Resolve_required_dependency_returns_cached_path_when_manifest_matches()
    {
        var cacheRoot = Path.Combine(_root, "deps");
        var cached = Path.Combine(cacheRoot, "Esca.FarmTypeManager");
        Directory.CreateDirectory(cached);
        WriteManifest(cached, "Esca.FarmTypeManager", "1.23.0");

        var path = RepoDependencyCache.ResolveRequired(
            new RepoModDependencyConfig { Id = "Esca.FarmTypeManager", Version = "1.23.0" },
            Env(cacheRoot));

        Assert.Equal(cached, path);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string CreateMod(string folderName, string uniqueId, string version)
    {
        var path = Path.Combine(_root, folderName);
        Directory.CreateDirectory(path);
        WriteManifest(path, uniqueId, version);
        return path;
    }

    private static void WriteManifest(string directory, string uniqueId, string version)
        => File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");

    private static IReadOnlyDictionary<string, string?> Env(string cacheRoot)
        => new Dictionary<string, string?> { [RepoDependencyCache.CacheEnvironmentVariable] = cacheRoot };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sdv-repo-deps-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [ ] **Step 2: Run cache tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoDependencyCacheTests
```

Expected: compile failure because `RepoDependencyCache`, `RepoDependencyStatus`, and related records do not exist.

- [ ] **Step 3: Implement the cache service**

Create `src/Runner/Repo/RepoDependencyCache.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Repo;

public enum RepoDependencyStatus
{
    Ok,
    Missing,
    BadManifest,
    UniqueIdMismatch,
    VersionMismatch,
}

public sealed record RepoDependencyManifest(string UniqueId, string? Version);

public sealed record RepoDependencyCheck(
    RepoDependencyStatus Status,
    string DependencyId,
    string ExpectedPath,
    RepoDependencyManifest? Manifest,
    string Message);

public static class RepoDependencyCache
{
    public const string CacheEnvironmentVariable = "SDV_TEST_MOD_CACHE";

    public static RepoDependencyManifest Import(
        string sourcePath,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var manifest = ReadManifest(sourcePath);
        var cacheRoot = ResolveCacheRoot(environment);
        Directory.CreateDirectory(cacheRoot);
        ExtraModDeployer.Deploy(cacheRoot, sourcePath);
        return manifest;
    }

    public static string ResolveRequired(
        RepoModDependencyConfig dependency,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var check = Check(dependency, environment);
        if (check.Status != RepoDependencyStatus.Ok)
        {
            throw new InvalidOperationException(check.Message);
        }

        return check.ExpectedPath;
    }

    public static RepoDependencyCheck Check(
        RepoModDependencyConfig dependency,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var id = RequireDependencyId(dependency);
        var path = Path.Combine(ResolveCacheRoot(environment), SanitizeFolderName(id));
        if (!Directory.Exists(path))
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.Missing,
                id,
                path,
                null,
                $"[repo deps] missing {id} in {Path.GetDirectoryName(path)}. Import it with: sdv-test repo deps import --from <path-to-{id}>");
        }

        RepoDependencyManifest manifest;
        try
        {
            manifest = ReadManifest(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.BadManifest,
                id,
                path,
                null,
                $"[repo deps] {Path.Combine(path, "manifest.json")} is invalid: {ex.Message}");
        }

        if (!string.Equals(manifest.UniqueId, id, StringComparison.Ordinal))
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.UniqueIdMismatch,
                id,
                path,
                manifest,
                $"[repo deps] {id} UniqueID mismatch: expected {id}, found {manifest.UniqueId}.");
        }

        if (!string.IsNullOrWhiteSpace(dependency.Version)
            && !string.Equals(manifest.Version, dependency.Version, StringComparison.Ordinal))
        {
            return new RepoDependencyCheck(
                RepoDependencyStatus.VersionMismatch,
                id,
                path,
                manifest,
                $"[repo deps] {id} version mismatch: expected {dependency.Version}, found {manifest.Version ?? "<missing>"}.");
        }

        return new RepoDependencyCheck(
            RepoDependencyStatus.Ok,
            id,
            path,
            manifest,
            $"[repo deps] {id} {manifest.Version ?? "<unknown>"} ok at {path}");
    }

    public static string ResolveCacheRoot(IReadOnlyDictionary<string, string?>? environment = null)
    {
        var configured = GetEnvironmentValue(CacheEnvironmentVariable, environment);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var frameworkRoot = FindFrameworkRoot(Directory.GetCurrentDirectory())
            ?? FindFrameworkRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(
                $"Unable to locate sdv-test-framework.slnx. Set {CacheEnvironmentVariable} to a dependency cache directory.");
        return Path.Combine(frameworkRoot, ".cache", "deps");
    }

    private static RepoDependencyManifest ReadManifest(string modPath)
    {
        if (string.IsNullOrWhiteSpace(modPath))
        {
            throw new InvalidOperationException("dependency mod path is required.");
        }

        var manifestPath = Path.Combine(Path.GetFullPath(modPath), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"dependency manifest not found: {manifestPath}", manifestPath);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("UniqueID", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new InvalidOperationException($"manifest missing non-empty UniqueID: {manifestPath}");
        }

        string? version = null;
        if (doc.RootElement.TryGetProperty("Version", out var versionElement)
            && versionElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(versionElement.GetString()))
        {
            version = versionElement.GetString();
        }

        return new RepoDependencyManifest(idElement.GetString()!, version);
    }

    private static string? FindFrameworkRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        if (File.Exists(directory.FullName))
        {
            directory = directory.Parent!;
        }

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "sdv-test-framework.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string RequireDependencyId(RepoModDependencyConfig dependency)
        => !string.IsNullOrWhiteSpace(dependency.Id)
            ? dependency.Id
            : throw new InvalidOperationException("repo dependency id is required.");

    private static string? GetEnvironmentValue(
        string name,
        IReadOnlyDictionary<string, string?>? environment)
        => environment is not null
            ? environment.TryGetValue(name, out var value) ? value : null
            : Environment.GetEnvironmentVariable(name);

    private static string SanitizeFolderName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }
}
```

- [ ] **Step 4: Run cache tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoDependencyCacheTests
```

Expected: `RepoDependencyCacheTests` passes.

- [ ] **Step 5: Commit Task 2**

Run:

```bash
git add src/Runner/Repo/RepoDependencyCache.cs tests/Runner.Tests/Repo/RepoDependencyCacheTests.cs
git commit -m "feat: add repo dependency cache service"
```

---

### Task 3: `repo deps import` And `repo deps doctor`

**Files:**
- Modify: `src/Runner/Commands/RepoCommand.cs`
- Modify: `src/Runner/Program.cs`
- Test: `tests/Runner.Tests/Repo/RepoCommandTests.cs`

- [ ] **Step 1: Write failing import command test**

Add this test to `tests/Runner.Tests/Repo/RepoCommandTests.cs`:

```csharp
[Fact]
public async Task RepoDepsImport_copies_source_mod_into_dependency_cache()
{
    var source = CreateMod("SourceContentPatcher", "Pathoschild.ContentPatcher", "2.7.0");
    var cacheRoot = Path.Combine(_repoRoot, ".cache", "deps");
    var previousCache = Environment.GetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable);
    Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, cacheRoot);
    var output = new StringWriter();
    var previousOut = Console.Out;
    Console.SetOut(output);
    try
    {
        var exit = await RepoCommand.RunAsync(
            new[] { "deps", "import", "--from", source }.AsMemory(),
            CancellationToken.None);

        Assert.Equal(0, exit);
    }
    finally
    {
        Console.SetOut(previousOut);
        Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, previousCache);
    }

    Assert.True(File.Exists(Path.Combine(cacheRoot, "Pathoschild.ContentPatcher", "manifest.json")));
    Assert.Contains("Pathoschild.ContentPatcher", output.ToString());
    Assert.Contains("2.7.0", output.ToString());
}
```

Add these helpers near the bottom of the test class:

```csharp
private string CreateMod(string folderName, string uniqueId, string version)
{
    var path = Path.Combine(_repoRoot, folderName);
    Directory.CreateDirectory(path);
    File.WriteAllText(
        Path.Combine(path, "manifest.json"),
        $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");
    File.WriteAllText(Path.Combine(path, "Test.dll"), "not a real dll");
    return path;
}
```

- [ ] **Step 2: Run import test red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoDepsImport_copies_source_mod_into_dependency_cache
```

Expected: exit is `64` or unknown subcommand because `repo deps` is absent from the command switch.

- [ ] **Step 3: Implement `repo deps import`**

In `RunAsync`, change usage and switch cases:

```csharp
Console.Error.WriteLine("usage: sdv-test repo <run|repeat|init|deps> [args...]");
```

```csharp
"deps" => RunRepoDeps(rest),
```

Add these methods to `RepoCommand`:

```csharp
private static int RunRepoDeps(ReadOnlyMemory<string> args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("usage: sdv-test repo deps <import|doctor> [args...]");
        return 64;
    }

    return args.Span[0] switch
    {
        "import" => RunRepoDepsImport(args[1..]),
        _ => Unknown("deps " + args.Span[0]),
    };
}

private static int RunRepoDepsImport(ReadOnlyMemory<string> args)
{
    string? source = null;
    for (var i = 0; i < args.Length; i++)
    {
        var value = args.Span[i];
        if (value == "--from")
        {
            source = ReadRequiredValue(args, ref i, value);
            continue;
        }

        throw new InvalidOperationException($"unknown repo deps import option: {value}");
    }

    if (string.IsNullOrWhiteSpace(source))
    {
        throw new InvalidOperationException("repo deps import requires --from <path>.");
    }

    var manifest = RepoDependencyCache.Import(source, BuildRepoEnvironment());
    var cacheRoot = RepoDependencyCache.ResolveCacheRoot(BuildRepoEnvironment());
    Console.Out.WriteLine($"[repo deps] imported {manifest.UniqueId} {manifest.Version ?? "<unknown>"}");
    Console.Out.WriteLine($"[repo deps] from {Path.GetFullPath(source)}");
    Console.Out.WriteLine($"[repo deps] to {Path.Combine(cacheRoot, manifest.UniqueId)}");
    return 0;
}
```

- [ ] **Step 4: Run import test green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoDepsImport_copies_source_mod_into_dependency_cache
```

Expected: test passes.

- [ ] **Step 5: Write failing doctor tests**

Add these tests to `RepoCommandTests`:

```csharp
[Fact]
public async Task RepoDepsDoctor_returns_zero_when_configured_deps_are_cached()
{
    Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
    Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
    WriteConfig(
        defaultTarget: "tests/scenarios",
        modSetsJson:
            """
            [
              {
                "name": "core",
                "deps": [{ "id": "Pathoschild.ContentPatcher", "version": "2.7.0" }],
                "extraMods": ["mods/Frobby"]
              }
            ]
            """);
    var cacheRoot = Path.Combine(_repoRoot, ".cache", "deps");
    CreateCachedMod(cacheRoot, "Pathoschild.ContentPatcher", "2.7.0");
    var previousCache = Environment.GetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable);
    Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, cacheRoot);
    var output = new StringWriter();
    var previousOut = Console.Out;
    Console.SetOut(output);
    try
    {
        var exit = await RepoCommand.RunAsync(
            new[] { "deps", "doctor", "--repo-root", _repoRoot, "--mod-set", "core" }.AsMemory(),
            CancellationToken.None);

        Assert.Equal(0, exit);
    }
    finally
    {
        Console.SetOut(previousOut);
        Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, previousCache);
    }

    Assert.Contains("Pathoschild.ContentPatcher", output.ToString());
    Assert.Contains("ok", output.ToString(), StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task RepoDepsDoctor_returns_one_for_missing_dependency()
{
    Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
    Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
    WriteConfig(
        defaultTarget: "tests/scenarios",
        modSetsJson:
            """
            [
              {
                "name": "core",
                "deps": [{ "id": "Pathoschild.ContentPatcher" }],
                "extraMods": ["mods/Frobby"]
              }
            ]
            """);
    var previousCache = Environment.GetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable);
    Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, Path.Combine(_repoRoot, ".cache", "deps"));
    var error = new StringWriter();
    var previousError = Console.Error;
    Console.SetError(error);
    try
    {
        var exit = await RepoCommand.RunAsync(
            new[] { "deps", "doctor", "--repo-root", _repoRoot }.AsMemory(),
            CancellationToken.None);

        Assert.Equal(1, exit);
    }
    finally
    {
        Console.SetError(previousError);
        Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, previousCache);
    }

    Assert.Contains("missing Pathoschild.ContentPatcher", error.ToString());
    Assert.Contains("repo deps import --from", error.ToString());
}
```

Add this helper:

```csharp
private static void CreateCachedMod(string cacheRoot, string uniqueId, string version)
{
    var path = Path.Combine(cacheRoot, uniqueId);
    Directory.CreateDirectory(path);
    File.WriteAllText(
        Path.Combine(path, "manifest.json"),
        $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");
}
```

- [ ] **Step 6: Run doctor tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "RepoDepsDoctor"
```

Expected: tests fail because `repo deps doctor` is an unknown subcommand.

- [ ] **Step 7: Implement `repo deps doctor`**

Add the doctor switch case in `RunRepoDeps`:

```csharp
"doctor" => RunRepoDepsDoctor(args[1..]),
```

Add the doctor implementation:

```csharp
private static int RunRepoDepsDoctor(ReadOnlyMemory<string> args)
{
    var repoRoot = Directory.GetCurrentDirectory();
    string? modSetName = null;
    for (var i = 0; i < args.Length; i++)
    {
        var value = args.Span[i];
        switch (value)
        {
            case "--repo-root":
                repoRoot = ReadRequiredValue(args, ref i, value);
                continue;
            case "--mod-set":
                modSetName = ReadRequiredValue(args, ref i, value);
                continue;
            default:
                throw new InvalidOperationException($"unknown repo deps doctor option: {value}");
        }
    }

    RepoTestConfig config;
    try
    {
        config = RepoTestConfig.Load(repoRoot);
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
    {
        Console.Error.WriteLine("[repo deps] " + ex.Message);
        return 2;
    }

    var modSet = SelectModSetForCommand(config, modSetName);
    var environment = BuildRepoEnvironment();
    var hadFailures = false;
    foreach (var dependency in modSet.Deps)
    {
        var check = RepoDependencyCache.Check(dependency, environment);
        if (check.Status == RepoDependencyStatus.Ok)
        {
            Console.Out.WriteLine(check.Message);
        }
        else
        {
            hadFailures = true;
            Console.Error.WriteLine(check.Message);
        }
    }

    foreach (var extraMod in modSet.ExtraMods.Where(value => value.Contains("SDV_GAME_MODS", StringComparison.Ordinal)))
    {
        Console.Error.WriteLine($"[repo deps] warning: extraMods entry '{extraMod}' still reads from SDV_GAME_MODS; move external dependencies to deps.");
    }

    if (modSet.Deps.Count == 0)
    {
        Console.Out.WriteLine($"[repo deps] mod set '{modSet.Name}' declares no deps.");
    }

    return hadFailures ? 1 : 0;
}

private static RepoModSetConfig SelectModSetForCommand(RepoTestConfig config, string? requestedName)
{
    if (string.IsNullOrWhiteSpace(requestedName))
    {
        return config.ModSets[0];
    }

    return config.ModSets.FirstOrDefault(modSet => modSet.Name == requestedName)
        ?? throw new InvalidOperationException($"Unknown mod set '{requestedName}'.");
}
```

- [ ] **Step 8: Run repo command tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SdvTestFramework.Runner.Tests.Repo.RepoCommandTests"
```

Expected: `RepoCommandTests` passes.

- [ ] **Step 9: Update CLI help**

Modify `src/Runner/Program.cs` repo help block:

```csharp
w.WriteLine("  repo deps import --from <path>");
w.WriteLine("                    Copy a local SMAPI dependency mod into .cache/deps/<UniqueID>.");
w.WriteLine("  repo deps doctor [--repo-root <path>] [--mod-set <name>]");
w.WriteLine("                    Validate configured repo dependency mods against the local cache.");
```

- [ ] **Step 10: Commit Task 3**

Run:

```bash
git add src/Runner/Commands/RepoCommand.cs src/Runner/Program.cs tests/Runner.Tests/Repo/RepoCommandTests.cs
git commit -m "feat: add repo dependency cache commands"
```

---

### Task 4: Wire Dependencies Into `repo run`

**Files:**
- Modify: `src/Runner/Repo/RepoRunPlanner.cs`
- Test: `tests/Runner.Tests/Repo/RepoRunPlannerTests.cs`
- Test: `tests/Runner.Tests/Repo/RepoCommandTests.cs`

- [ ] **Step 1: Write failing planner ordering test**

Add this test to `RepoRunPlannerTests`:

```csharp
[Fact]
public void BuildRunPlan_prepends_cached_deps_before_repo_extra_mods()
{
    Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
    var cacheRoot = Path.Combine(_repoRoot, ".cache", "deps");
    var contentPatcher = CreateCachedMod(cacheRoot, "Pathoschild.ContentPatcher", "2.7.0");
    var frobbyMod = Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby")).FullName;
    var config = Config(
        defaultTarget: "tests/scenarios",
        modSets:
        [
            new RepoModSetConfig
            {
                Name = "core",
                Deps =
                [
                    new RepoModDependencyConfig { Id = "Pathoschild.ContentPatcher", Version = "2.7.0" },
                ],
                ExtraMods = ["mods/Frobby"],
            },
        ]);
    var environment = new System.Collections.Generic.Dictionary<string, string?>
    {
        [RepoDependencyCache.CacheEnvironmentVariable] = cacheRoot,
    };

    var plan = RepoRunPlanner.BuildRunPlan(
        _repoRoot,
        config,
        new RepoRunRequest(false, false, false, false, "core", null, Array.Empty<string>()),
        environment);

    Assert.Equal(new[] { contentPatcher, frobbyMod }, plan.ExtraMods);
    var firstExtraFlag = plan.FrobbyArgs.IndexOf("--extra-mod");
    Assert.Equal(contentPatcher, plan.FrobbyArgs[firstExtraFlag + 1]);
    var secondExtraFlag = plan.FrobbyArgs.IndexOf("--extra-mod", firstExtraFlag + 1);
    Assert.Equal(frobbyMod, plan.FrobbyArgs[secondExtraFlag + 1]);
}
```

Add helper:

```csharp
private string CreateCachedMod(string cacheRoot, string uniqueId, string version)
{
    var path = Path.Combine(cacheRoot, uniqueId);
    Directory.CreateDirectory(path);
    File.WriteAllText(
        Path.Combine(path, "manifest.json"),
        $$"""{"Name":"Test","UniqueID":"{{uniqueId}}","Version":"{{version}}","EntryDll":"Test.dll"}""");
    return path;
}
```

- [ ] **Step 2: Run planner ordering test red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter BuildRunPlan_prepends_cached_deps_before_repo_extra_mods
```

Expected: test fails because dependency paths are not added to `ExtraMods`.

- [ ] **Step 3: Resolve dependencies in `RepoRunPlanner`**

Modify the extra mod construction in `BuildRunPlan`:

```csharp
var dependencyMods = modSet.Deps
    .Select(dependency => RepoDependencyCache.ResolveRequired(dependency, environment))
    .ToArray();
var repoExtraMods = modSet.ExtraMods
    .Select(path => RepoPathResolver.Resolve(fullRepoRoot, path, environment, requireExists: true))
    .ToArray();
var extraMods = dependencyMods.Concat(repoExtraMods).ToArray();
```

The existing loop over `extraMods` stays unchanged, so dependencies become repeated `--extra-mod` flags before repo-owned mod folders.

- [ ] **Step 4: Run planner tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoRunPlannerTests
```

Expected: `RepoRunPlannerTests` passes.

- [ ] **Step 5: Write failing repo run missing dependency test**

Add this test to `RepoCommandTests`:

```csharp
[Fact]
public async Task RepoRun_dry_run_returns_exit_2_when_dependency_is_missing()
{
    Directory.CreateDirectory(Path.Combine(_repoRoot, "mods", "Frobby"));
    Directory.CreateDirectory(Path.Combine(_repoRoot, "tests", "scenarios"));
    WriteConfig(
        defaultTarget: "tests/scenarios",
        modSetsJson:
            """
            [
              {
                "name": "core",
                "deps": [{ "id": "Pathoschild.ContentPatcher" }],
                "extraMods": ["mods/Frobby"]
              }
            ]
            """);
    var previousCache = Environment.GetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable);
    Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, Path.Combine(_repoRoot, ".cache", "deps"));
    var error = new StringWriter();
    var previousError = Console.Error;
    Console.SetError(error);
    try
    {
        var exit = await RepoCommand.RunAsync(
            new[] { "run", "--repo-root", _repoRoot, "--dry-run" }.AsMemory(),
            CancellationToken.None);

        Assert.Equal(2, exit);
    }
    finally
    {
        Console.SetError(previousError);
        Environment.SetEnvironmentVariable(RepoDependencyCache.CacheEnvironmentVariable, previousCache);
    }

    Assert.Contains("missing Pathoschild.ContentPatcher", error.ToString());
    Assert.Contains("repo deps import --from", error.ToString());
}
```

- [ ] **Step 6: Run repo run missing dependency test green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoRun_dry_run_returns_exit_2_when_dependency_is_missing
```

Expected: test passes because `RepoRunPlanner` throws before dry-run output.

- [ ] **Step 7: Commit Task 4**

Run:

```bash
git add src/Runner/Repo/RepoRunPlanner.cs tests/Runner.Tests/Repo/RepoRunPlannerTests.cs tests/Runner.Tests/Repo/RepoCommandTests.cs
git commit -m "feat: stage repo dependency cache in runs"
```

---

### Task 5: Docs, Gitignore, And Scaffold Guidance

**Files:**
- Modify: `.gitignore`
- Modify: `README.md`
- Modify: `src/Runner/Repo/RepoScaffoldGenerator.cs`
- Test: `tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs`

- [ ] **Step 1: Write failing scaffold docs test**

Add this assertion to the scaffold docs test that reads `docs/FROBBY.md`:

```csharp
Assert.Contains("repo deps import", docs);
Assert.Contains("repo deps doctor", docs);
Assert.Contains("deps", docs);
Assert.Contains("extraMods", docs);
```

If the existing test does not read `docs/FROBBY.md`, add:

```csharp
var docs = File.ReadAllText(Path.Combine(_repoRoot, "docs", "FROBBY.md"));
```

- [ ] **Step 2: Run scaffold docs test red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoScaffoldGeneratorTests
```

Expected: test fails because generated docs do not mention repo dependency cache commands.

- [ ] **Step 3: Update generated scaffold docs**

Modify the `Docs()` string in `src/Runner/Repo/RepoScaffoldGenerator.cs`:

```markdown
## Dependency mods

Use `modSets[].deps` for external SMAPI dependency mods such as Content Patcher,
Farm Type Manager, SpaceCore, or framework mods downloaded outside this repo.
Use `modSets[].extraMods` for mod folders built or owned by this repo.

Import dependencies into Frobby's local cache before running:

```sh
sdv-test repo deps import --from /path/to/ContentPatcher
sdv-test repo deps doctor --repo-root .
```

Normal `sdv-test repo run` reads cached dependency copies from `.cache/deps` or
`$SDV_TEST_MOD_CACHE`; it does not read your playable Stardew `Mods` folder
unless this repo explicitly keeps `${SDV_GAME_MODS}` paths in `extraMods`.
```

- [ ] **Step 4: Update `.gitignore`**

Add under local run outputs:

```gitignore
.cache/
```

- [ ] **Step 5: Update README**

Add this section after the repo scaffold quickstart:

```markdown
### Repo Dependency Cache

For repo-local test suites, keep external dependency mods in Frobby's local cache
instead of pointing at your playable Stardew `Mods` folder:

```bash
sdv-test repo deps import --from "/path/to/ContentPatcher"
sdv-test repo deps import --from "/path/to/FarmTypeManager"
sdv-test repo deps doctor --repo-root .
```

The default cache lives at `sdv-test-framework/.cache/deps/` and is gitignored.
Set `SDV_TEST_MOD_CACHE=/path/to/deps` when a repo needs a shared or CI-provided
cache. Use `modSets[].deps` for external dependency mods keyed by SMAPI
`UniqueID`; keep `modSets[].extraMods` for repo-owned mod folders and content
packs. Normal `sdv-test repo run` stages cached copies into the isolated test
mods directory and does not read the user's live game `Mods` folder unless the
repo config still contains explicit `${SDV_GAME_MODS}` paths.
```

- [ ] **Step 6: Run scaffold and repo tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "RepoScaffoldGeneratorTests|RepoCommandTests|RepoRunPlannerTests|RepoTestConfigTests|RepoDependencyCacheTests"
```

Expected: selected repo tests pass.

- [ ] **Step 7: Commit Task 5**

Run:

```bash
git add .gitignore README.md src/Runner/Repo/RepoScaffoldGenerator.cs tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs
git commit -m "docs: document repo dependency cache workflow"
```

---

### Task 6: SVE Core Config Migration And Smoke

**Files:**
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Inspect current SVE config**

Run:

```bash
sed -n '1,220p' /home/fintan/stardewRepos/StardewValleyExpanded/sdv-test.config.json
```

Expected: the `core` mod set still has `${SDV_GAME_MODS}/ContentPatcher` and `${SDV_GAME_MODS}/FarmTypeManager` inside `extraMods`.

- [ ] **Step 2: Update SVE core config**

Change the `core` mod set to this shape, preserving existing repo-owned SVE paths:

```json
{
  "name": "core",
  "deps": [
    { "id": "Pathoschild.ContentPatcher" },
    { "id": "Esca.FarmTypeManager" }
  ],
  "extraMods": [
    "Stardew Valley Expanded/StardewValleyExpanded/bin/Release/net6.0",
    "Stardew Valley Expanded/[CP] Stardew Valley Expanded",
    "Stardew Valley Expanded/[FTM] Stardew Valley Expanded"
  ]
}
```

- [ ] **Step 3: Update SVE Frobby docs**

Add this section to `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`:

```markdown
## Test dependency cache

The SVE core suite uses Frobby's repo dependency cache for external SMAPI mods:

```sh
sdv-test repo deps import --from "/path/to/ContentPatcher"
sdv-test repo deps import --from "/path/to/FarmTypeManager"
scripts/sdv-test --no-build --headless --mod-set core --dry-run
```

Cached dependencies live in Frobby's `.cache/deps/` directory or in
`$SDV_TEST_MOD_CACHE` when that environment variable is set. SVE-owned mods and
content packs stay in `extraMods`.
```

- [ ] **Step 4: Run SVE dry run red or green based on cache state**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless --mod-set core --no-build --dry-run tests/sdv/01-sve-core-loads.test.json
```

Expected when deps have not been imported: exit `2` with `repo deps import --from` guidance.

Expected when deps are already imported: exit `0` and dry-run output lists cached dependency paths before SVE-owned `extraMods`.

- [ ] **Step 5: Import local dependency copies if needed**

Run these commands only when the dry run reports missing dependencies and the local game install contains those mods:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
dotnet run --project src/Runner/Runner.csproj -- repo deps import --from "$SDV_GAME_MODS/ContentPatcher"
dotnet run --project src/Runner/Runner.csproj -- repo deps import --from "$SDV_GAME_MODS/FarmTypeManager"
```

Expected: each command prints imported `UniqueID`, version, source, and destination.

- [ ] **Step 6: Run SVE core smoke headless**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless --mod-set core --no-build tests/sdv/01-sve-core-loads.test.json
```

Expected: scenario passes, report hub path prints, and the run does not require `${SDV_GAME_MODS}` dependency entries.

- [ ] **Step 7: Commit SVE migration on the SVE feature branch**

Run:

```bash
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded add sdv-test.config.json docs/FROBBY.md
git -C /home/fintan/stardewRepos/StardewValleyExpanded commit -m "test: use frobby dependency cache for core deps"
```

---

## Final Verification

- [ ] **Step 1: Run focused Frobby repo tests**

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SdvTestFramework.Runner.Tests.Repo"
```

Expected: all repo tests pass.

- [ ] **Step 2: Run full Frobby unit suite**

```bash
dotnet test sdv-test-framework.slnx --configuration Debug
```

Expected: all projects pass.

- [ ] **Step 3: Run SVE core headless smoke**

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
./scripts/sdv-test --headless --mod-set core --no-build tests/sdv/01-sve-core-loads.test.json
```

Expected: SVE core load scenario passes.

- [ ] **Step 4: Check git state**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected: Frobby feature branch contains committed implementation work. SVE feature branch contains committed config/docs migration. SVE `master` remains untouched.

---

## Self-Review Notes

- Spec goal coverage: config `deps`, `.cache/deps`, import command, doctor command, run-time staging, version mismatch failure, `.cache/` gitignore, README docs, neutral scaffold docs, and SVE migration are covered.
- Non-goals preserved: no internet download support, no automatic updates during test runs, no global default cache, and no replacement of repo-owned `extraMods`.
- Test strategy: every production change has a red-green unit test before implementation; SVE smoke runs after Frobby support exists.
