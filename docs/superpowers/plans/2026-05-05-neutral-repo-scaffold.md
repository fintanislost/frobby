# Neutral Repo Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Frobby-owned, mod-neutral repo scaffold and prove it against the core Stardew Valley Expanded checkout.

**Architecture:** Frobby gains a `repo` command family that reads `sdv-test.config.json`, resolves paths safely, and delegates to the existing `run` / `run-suite` commands. Generated repo scripts stay thin and generic. SVE Core uses the generated scaffold as the first non-Starberg consumer.

**Tech Stack:** C#/.NET 10 runner, .NET 6 harness/protocol, System.Text.Json, xUnit, Bash wrappers, existing Frobby JSON scenarios.

---

## File Structure

Create these Frobby files:

- `src/Runner/Repo/RepoTestConfig.cs` - config DTOs and JSON load entry point.
- `src/Runner/Repo/RepoPathResolver.cs` - repo-relative, absolute, `~`, `$VAR`, and `${VAR}` path expansion.
- `src/Runner/Repo/RepoRunPlanner.cs` - converts config + CLI options into build and Frobby command arguments.
- `src/Runner/Repo/RepoScaffoldGenerator.cs` - writes `sdv-test.config.json`, scripts, dry-run tests, docs, and sample scenarios.
- `src/Runner/Commands/RepoCommand.cs` - parses `sdv-test repo init|run|repeat` and invokes planner/generator.
- `tests/Runner.Tests/Repo/RepoTestConfigTests.cs`
- `tests/Runner.Tests/Repo/RepoPathResolverTests.cs`
- `tests/Runner.Tests/Repo/RepoRunPlannerTests.cs`
- `tests/Runner.Tests/Repo/RepoCommandTests.cs`
- `tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs`

Modify these Frobby files:

- `src/Runner/Program.cs` - add `repo` command and help text.
- `src/Protocol/Models/ModsState.cs` - add `unique_ids` and expand `mods` to metadata records.
- `src/Harness/Handlers/StateModsHandler.cs` - populate metadata from SMAPI.
- `tests/Harness.Tests/StateModsHandlerTests.cs` - cover metadata.
- `src/Runner/Scenarios/ScenarioRunner.cs` - add a generic `contains` state assertion for string arrays and arrays of objects.
- `tests/Runner.Tests/ScenarioRunnerDslTests.cs` - cover `contains`.
- `docs/rpc-schema.md` - document expanded `state.mods`.
- `README.md` - document repo-local scaffold command.
- `docs/mcp-quickstart.md` - mention repo scaffold as the recommended mod-local shape.

Create these SVE Core files under `/home/fintan/stardewRepos/StardewValleyExpanded` using the new generator:

- `sdv-test.config.json`
- `scripts/sdv-test`
- `scripts/sdv-repeat`
- `tests/sdv/01-sve-core-loads.test.json`
- `tests/sdv/fragments/.gitkeep`
- `tests/sdv/baselines/.gitkeep`
- `tests/scripts/sdv-test-dry-run.sh`
- `tests/scripts/sdv-repeat-dry-run.sh`
- `docs/FROBBY.md`

Do not modify Starberg in this first slice.

---

### Task 1: Config DTOs And Path Resolution

**Files:**
- Create: `src/Runner/Repo/RepoTestConfig.cs`
- Create: `src/Runner/Repo/RepoPathResolver.cs`
- Test: `tests/Runner.Tests/Repo/RepoTestConfigTests.cs`
- Test: `tests/Runner.Tests/Repo/RepoPathResolverTests.cs`

- [ ] **Step 1: Write config parsing tests**

Create `tests/Runner.Tests/Repo/RepoTestConfigTests.cs`:

```csharp
using System;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public class RepoTestConfigTests
{
    [Fact]
    public void Load_ReadsProjectBuildTargetsAndModSet()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repo-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "sdv-test.config.json"), """
            {
              "project": { "name": "Example Mod", "slug": "example-mod", "version": "0.1.0" },
              "frobbyRoot": "../frobby/sdv-test-framework",
              "build": { "command": "dotnet", "args": ["build", "Example.sln", "--configuration", "Release"] },
              "defaultTarget": "tests/sdv",
              "baselineTarget": "tests/sdv/01-example-loads.test.json",
              "modSets": [
                {
                  "name": "core",
                  "extraMods": ["bin/Release/net6.0", "${SDV_GAME_MODS}/ContentPatcher"]
                }
              ]
            }
            """);

            var config = RepoTestConfig.Load(root);

            Assert.Equal("Example Mod", config.Project.Name);
            Assert.Equal("example-mod", config.Project.Slug);
            Assert.Equal("0.1.0", config.Project.Version);
            Assert.Equal("../frobby/sdv-test-framework", config.FrobbyRoot);
            Assert.Equal("dotnet", config.Build.Command);
            Assert.Equal(new[] { "build", "Example.sln", "--configuration", "Release" }, config.Build.Args);
            Assert.Equal("tests/sdv", config.DefaultTarget);
            Assert.Equal("tests/sdv/01-example-loads.test.json", config.BaselineTarget);
            var modSet = Assert.Single(config.ModSets);
            Assert.Equal("core", modSet.Name);
            Assert.Equal(new[] { "bin/Release/net6.0", "${SDV_GAME_MODS}/ContentPatcher" }, modSet.ExtraMods);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingConfig_ThrowsFileNotFound()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repo-config-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => RepoTestConfig.Load(root));
            Assert.Contains("sdv-test.config.json", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run config parsing tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoTestConfigTests
```

Expected: compile fails because `SdvTestFramework.Runner.Repo.RepoTestConfig` does not exist.

- [ ] **Step 3: Implement config DTOs**

Create `src/Runner/Repo/RepoTestConfig.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Runner.Repo;

public sealed class RepoTestConfig
{
    public RepoProjectConfig Project { get; set; } = new();
    public string FrobbyRoot { get; set; } = "../frobby/sdv-test-framework";
    public RepoBuildConfig Build { get; set; } = new();
    public string DefaultTarget { get; set; } = "tests/sdv";
    public string? BaselineTarget { get; set; }
    public List<RepoModSetConfig> ModSets { get; set; } = new();

    public static RepoTestConfig Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "sdv-test.config.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"repo config not found: {path}", path);

        var config = JsonSerializer.Deserialize<RepoTestConfig>(
            File.ReadAllText(path),
            ProtocolJson.Options);
        if (config is null)
            throw new InvalidOperationException($"repo config is empty: {path}");

        config.Validate(path);
        return config;
    }

    private void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Project.Name))
            throw new InvalidOperationException($"{path}: project.name is required");
        if (string.IsNullOrWhiteSpace(Project.Slug))
            throw new InvalidOperationException($"{path}: project.slug is required");
        if (string.IsNullOrWhiteSpace(Project.Version))
            throw new InvalidOperationException($"{path}: project.version is required");
        if (string.IsNullOrWhiteSpace(Build.Command))
            throw new InvalidOperationException($"{path}: build.command is required");
        if (string.IsNullOrWhiteSpace(DefaultTarget))
            throw new InvalidOperationException($"{path}: default_target is required");
        if (ModSets.Count == 0)
            throw new InvalidOperationException($"{path}: at least one mod_set is required");
        foreach (var modSet in ModSets)
        {
            if (string.IsNullOrWhiteSpace(modSet.Name))
                throw new InvalidOperationException($"{path}: mod_set.name is required");
            if (modSet.ExtraMods.Count == 0)
                throw new InvalidOperationException($"{path}: mod_set '{modSet.Name}' needs at least one extra_mod");
        }
    }
}

public sealed class RepoProjectConfig
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public sealed class RepoBuildConfig
{
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
}

public sealed class RepoModSetConfig
{
    public string Name { get; set; } = string.Empty;
    public List<string> ExtraMods { get; set; } = new();
}
```

- [ ] **Step 4: Run config parsing tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoTestConfigTests
```

Expected: `Passed!` for `RepoTestConfigTests`.

- [ ] **Step 5: Write path resolver tests**

Create `tests/Runner.Tests/Repo/RepoPathResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public class RepoPathResolverTests
{
    [Fact]
    public void Resolve_HandlesRelativeAbsoluteHomeAndEnvironmentVariables()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repo-paths-{Guid.NewGuid():N}");
        var envRoot = Path.Combine(root, "env mods");
        var relative = Path.Combine(root, "path with spaces", "[CP] Example");
        Directory.CreateDirectory(envRoot);
        Directory.CreateDirectory(relative);

        var env = new Dictionary<string, string>
        {
            ["SDV_GAME_MODS"] = envRoot,
            ["HOME"] = root
        };

        try
        {
            Assert.Equal(relative, RepoPathResolver.Resolve(root, "path with spaces/[CP] Example", env, requireExists: true));
            Assert.Equal(envRoot, RepoPathResolver.Resolve(root, "${SDV_GAME_MODS}", env, requireExists: true));
            Assert.Equal(Path.Combine(envRoot, "ContentPatcher"), RepoPathResolver.Resolve(root, "$SDV_GAME_MODS/ContentPatcher", env, requireExists: false));
            Assert.Equal(Path.Combine(root, "cache"), RepoPathResolver.Resolve(root, "~/cache", env, requireExists: false));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_MissingEnvironmentVariable_ThrowsActionableError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RepoPathResolver.Resolve("/tmp/repo", "${MISSING_MODS}/ContentPatcher", new Dictionary<string, string>(), requireExists: false));

        Assert.Contains("MISSING_MODS", ex.Message);
    }

    [Fact]
    public void Resolve_MissingPathWhenRequired_ThrowsDirectoryNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"repo-missing-{Guid.NewGuid():N}");

        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            RepoPathResolver.Resolve("/tmp/repo", path, new Dictionary<string, string>(), requireExists: true));

        Assert.Contains(path, ex.Message);
    }
}
```

- [ ] **Step 6: Run path resolver tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoPathResolverTests
```

Expected: compile fails because `RepoPathResolver` does not exist.

- [ ] **Step 7: Implement path resolver**

Create `src/Runner/Repo/RepoPathResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SdvTestFramework.Runner.Repo;

public static class RepoPathResolver
{
    private static readonly Regex EnvPattern = new(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?<bare>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    public static string Resolve(
        string repoRoot,
        string rawPath,
        IReadOnlyDictionary<string, string>? environment = null,
        bool requireExists = true)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new InvalidOperationException("path value is required");

        environment ??= Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => e.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);

        var expanded = ExpandHome(ExpandEnvironment(rawPath, environment), environment);
        var full = Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(repoRoot, expanded));

        if (requireExists && !Directory.Exists(full) && !File.Exists(full))
            throw new DirectoryNotFoundException($"configured path does not exist: {full}");

        return full;
    }

    private static string ExpandHome(string path, IReadOnlyDictionary<string, string> environment)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
        {
            if (!environment.TryGetValue("HOME", out var home) || string.IsNullOrWhiteSpace(home))
                throw new InvalidOperationException("HOME is required to expand '~'");
            return path == "~" ? home : Path.Combine(home, path[2..]);
        }

        return path;
    }

    private static string ExpandEnvironment(string path, IReadOnlyDictionary<string, string> environment)
    {
        var result = new StringBuilder();
        var index = 0;
        foreach (Match match in EnvPattern.Matches(path))
        {
            result.Append(path, index, match.Index - index);
            var name = match.Groups["name"].Success
                ? match.Groups["name"].Value
                : match.Groups["bare"].Value;
            if (!environment.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
                throw new InvalidOperationException($"environment variable '{name}' is required for path '{path}'");

            result.Append(value);
            index = match.Index + match.Length;
        }
        result.Append(path, index, path.Length - index);
        return result.ToString();
    }
}
```

- [ ] **Step 8: Run path resolver tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "RepoTestConfigTests|RepoPathResolverTests"
```

Expected: `Passed!` for both test classes.

- [ ] **Step 9: Commit config and path resolution**

```bash
git add src/Runner/Repo/RepoTestConfig.cs src/Runner/Repo/RepoPathResolver.cs tests/Runner.Tests/Repo/RepoTestConfigTests.cs tests/Runner.Tests/Repo/RepoPathResolverTests.cs
git commit -m "feat: parse repo scaffold config"
```

---

### Task 2: Repo Run Planner And Command

**Files:**
- Create: `src/Runner/Repo/RepoRunPlanner.cs`
- Create: `src/Runner/Commands/RepoCommand.cs`
- Modify: `src/Runner/Program.cs`
- Test: `tests/Runner.Tests/Repo/RepoRunPlannerTests.cs`
- Test: `tests/Runner.Tests/Repo/RepoCommandTests.cs`

- [ ] **Step 1: Write planner tests**

Create `tests/Runner.Tests/Repo/RepoRunPlannerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public class RepoRunPlannerTests
{
    [Fact]
    public void BuildRunPlan_DirectoryTarget_UsesRunSuiteAndRepeatedExtraMods()
    {
        var root = CreateRepo();
        try
        {
            var plan = RepoRunPlanner.BuildRunPlan(
                root,
                RepoTestConfig.Load(root),
                new RepoRunRequest(Visible: false, NoBuild: false, DryRun: true, Baseline: false, ModSet: "core", ReportDir: null, Targets: Array.Empty<string>()),
                new Dictionary<string, string> { ["SDV_GAME_MODS"] = Path.Combine(root, "game-mods"), ["HOME"] = root });

            Assert.Equal("dotnet", plan.BuildCommand![0]);
            Assert.Equal("run-suite", plan.FrobbyArgs[0]);
            Assert.Contains("--fresh-process-per-scenario", plan.FrobbyArgs);
            Assert.Contains("--headless", plan.FrobbyArgs);
            Assert.Equal(3, plan.FrobbyArgs.FindAll(x => x == "--extra-mod").Count);
            Assert.Contains(Path.Combine(root, "tests", "sdv"), plan.FrobbyArgs);
            Assert.Contains(Path.Combine(Path.GetTempPath(), "example-frobby-results-0.1.0"), plan.FrobbyArgs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildRunPlan_SingleScenario_UsesRun()
    {
        var root = CreateRepo();
        try
        {
            var scenario = Path.Combine(root, "tests", "sdv", "01-example.test.json");
            File.WriteAllText(scenario, """{"name":"example","steps":[]}""");

            var plan = RepoRunPlanner.BuildRunPlan(
                root,
                RepoTestConfig.Load(root),
                new RepoRunRequest(Visible: false, NoBuild: true, DryRun: true, Baseline: false, ModSet: null, ReportDir: null, Targets: new[] { "tests/sdv/01-example.test.json" }),
                new Dictionary<string, string> { ["SDV_GAME_MODS"] = Path.Combine(root, "game-mods"), ["HOME"] = root });

            Assert.Null(plan.BuildCommand);
            Assert.Equal("run", plan.FrobbyArgs[0]);
            Assert.Contains(scenario, plan.FrobbyArgs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildRunPlan_Baseline_UsesConfiguredBaselineTargetAndUpdateBaselines()
    {
        var root = CreateRepo();
        try
        {
            var plan = RepoRunPlanner.BuildRunPlan(
                root,
                RepoTestConfig.Load(root),
                new RepoRunRequest(Visible: false, NoBuild: true, DryRun: true, Baseline: true, ModSet: null, ReportDir: "/tmp/custom-report", Targets: Array.Empty<string>()),
                new Dictionary<string, string> { ["SDV_GAME_MODS"] = Path.Combine(root, "game-mods"), ["HOME"] = root });

            Assert.Equal("run", plan.FrobbyArgs[0]);
            Assert.Contains("--update-baselines", plan.FrobbyArgs);
            Assert.Contains("/tmp/custom-report", plan.FrobbyArgs);
            Assert.Contains(Path.Combine(root, "tests", "sdv", "01-example.test.json"), plan.FrobbyArgs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repo-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "tests", "sdv"));
        Directory.CreateDirectory(Path.Combine(root, "game-mods", "ContentPatcher"));
        Directory.CreateDirectory(Path.Combine(root, "game-mods", "FarmTypeManager"));
        Directory.CreateDirectory(Path.Combine(root, "bin", "Release", "net6.0"));
        File.WriteAllText(Path.Combine(root, "sdv-test.config.json"), """
        {
          "project": { "name": "Example", "slug": "example", "version": "0.1.0" },
          "frobbyRoot": "../frobby/sdv-test-framework",
          "build": { "command": "dotnet", "args": ["build", "Example.sln", "--configuration", "Release"] },
          "defaultTarget": "tests/sdv",
          "baselineTarget": "tests/sdv/01-example.test.json",
          "modSets": [
            {
              "name": "core",
              "extraMods": [
                "${SDV_GAME_MODS}/ContentPatcher",
                "${SDV_GAME_MODS}/FarmTypeManager",
                "bin/Release/net6.0"
              ]
            }
          ]
        }
        """);
        return root;
    }
}
```

- [ ] **Step 2: Run planner tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoRunPlannerTests
```

Expected: compile fails because `RepoRunPlanner`, `RepoRunRequest`, and `RepoRunPlan` do not exist.

- [ ] **Step 3: Implement planner**

Create `src/Runner/Repo/RepoRunPlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SdvTestFramework.Runner.Repo;

public sealed record RepoRunRequest(
    bool Visible,
    bool NoBuild,
    bool DryRun,
    bool Baseline,
    string? ModSet,
    string? ReportDir,
    IReadOnlyList<string> Targets);

public sealed record RepoRunPlan(
    string RepoRoot,
    IReadOnlyList<string>? BuildCommand,
    List<string> FrobbyArgs,
    string ReportDir,
    IReadOnlyList<string> ExtraMods);

public static class RepoRunPlanner
{
    public static RepoRunPlan BuildRunPlan(
        string repoRoot,
        RepoTestConfig config,
        RepoRunRequest request,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        var modSet = SelectModSet(config, request.ModSet);
        var extraMods = modSet.ExtraMods
            .Select(p => RepoPathResolver.Resolve(repoRoot, p, environment, requireExists: true))
            .ToList();

        var targets = ResolveTargets(repoRoot, config, request);
        var reportDir = request.ReportDir
            ?? Path.Combine(Path.GetTempPath(), $"{config.Project.Slug}-frobby-results-{config.Project.Version}");

        List<string>? buildCommand = null;
        if (!request.NoBuild)
        {
            buildCommand = new List<string> { config.Build.Command };
            buildCommand.AddRange(config.Build.Args);
        }

        var args = new List<string>();
        var singleScenario = targets.Count == 1
            && targets[0].EndsWith(".test.json", StringComparison.OrdinalIgnoreCase);
        if (request.Baseline || singleScenario)
        {
            args.Add("run");
            if (request.Baseline)
                args.Add("--update-baselines");
        }
        else
        {
            args.Add("run-suite");
            args.Add("--fresh-process-per-scenario");
        }

        if (!request.Visible)
            args.Add("--headless");

        foreach (var extraMod in extraMods)
        {
            args.Add("--extra-mod");
            args.Add(extraMod);
        }

        args.Add("--report-dir");
        args.Add(reportDir);
        args.AddRange(targets);

        return new RepoRunPlan(repoRoot, buildCommand, args, reportDir, extraMods);
    }

    private static RepoModSetConfig SelectModSet(RepoTestConfig config, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return config.ModSets[0];

        var match = config.ModSets.FirstOrDefault(m => string.Equals(m.Name, requested, StringComparison.Ordinal));
        if (match is null)
            throw new InvalidOperationException($"mod set '{requested}' was not found");

        return match;
    }

    private static List<string> ResolveTargets(string repoRoot, RepoTestConfig config, RepoRunRequest request)
    {
        var rawTargets = new List<string>();
        if (request.Baseline)
        {
            if (request.Targets.Count > 1)
                throw new InvalidOperationException("--baseline accepts at most one scenario target");
            rawTargets.Add(request.Targets.Count == 1
                ? request.Targets[0]
                : config.BaselineTarget ?? throw new InvalidOperationException("baseline_target is required for --baseline"));
        }
        else if (request.Targets.Count == 0)
        {
            rawTargets.Add(config.DefaultTarget);
        }
        else
        {
            rawTargets.AddRange(request.Targets);
        }

        return rawTargets
            .Select(t => RepoPathResolver.Resolve(repoRoot, t, environment: null, requireExists: true))
            .ToList();
    }
}
```

- [ ] **Step 4: Run planner tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoRunPlannerTests
```

Expected: `Passed!` for `RepoRunPlannerTests`.

- [ ] **Step 5: Write repo command tests**

Create `tests/Runner.Tests/Repo/RepoCommandTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

[Collection("Console")]
public class RepoCommandTests
{
    [Fact]
    public async Task RepoRun_DryRun_PrintsBuildCommandFrobbyCommandAndReportHub()
    {
        var root = CreateRepo();
        try
        {
            var outWriter = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outWriter);
            try
            {
                var exit = await RepoCommand.RunAsync(
                    new[] { "run", "--repo-root", root, "--dry-run" }.AsMemory(),
                    CancellationToken.None);

                Assert.Equal(0, exit);
            }
            finally
            {
                Console.SetOut(priorOut);
            }

            var output = outWriter.ToString();
            Assert.Contains("dotnet build Example.sln", output);
            Assert.Contains("run-suite", output);
            Assert.Contains("--headless", output);
            Assert.Contains("--extra-mod", output);
            Assert.Contains("report hub:", output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepoRun_UsesExecutorForNonDryRun()
    {
        var root = CreateRepo();
        var calls = new List<string[]>();
        var original = RepoCommand.RunExecutor;
        RepoCommand.RunExecutor = (args, _) =>
        {
            calls.Add(args.ToArray());
            return Task.FromResult(0);
        };

        try
        {
            var exit = await RepoCommand.RunAsync(
                new[] { "run", "--repo-root", root, "--no-build", "--visible" }.AsMemory(),
                CancellationToken.None);

            Assert.Equal(0, exit);
            var call = Assert.Single(calls);
            Assert.Equal("run-suite", call[0]);
            Assert.DoesNotContain("--headless", call);
        }
        finally
        {
            RepoCommand.RunExecutor = original;
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repo-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "tests", "sdv"));
        Directory.CreateDirectory(Path.Combine(root, "game-mods", "ContentPatcher"));
        Directory.CreateDirectory(Path.Combine(root, "game-mods", "FarmTypeManager"));
        Directory.CreateDirectory(Path.Combine(root, "bin", "Release", "net6.0"));
        File.WriteAllText(Path.Combine(root, "sdv-test.config.json"), $$"""
        {
          "project": { "name": "Example", "slug": "example", "version": "0.1.0" },
          "frobbyRoot": "../frobby/sdv-test-framework",
          "build": { "command": "dotnet", "args": ["build", "Example.sln", "--configuration", "Release"] },
          "defaultTarget": "tests/sdv",
          "modSets": [
            {
              "name": "core",
              "extraMods": [
                "{{Path.Combine(root, "game-mods", "ContentPatcher").Replace("\\", "\\\\")}}",
                "{{Path.Combine(root, "game-mods", "FarmTypeManager").Replace("\\", "\\\\")}}",
                "bin/Release/net6.0"
              ]
            }
          ]
        }
        """);
        return root;
    }
}
```

- [ ] **Step 6: Run repo command tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoCommandTests
```

Expected: compile fails because `RepoCommand` does not exist.

- [ ] **Step 7: Implement repo command and register it**

Create `src/Runner/Commands/RepoCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Repo;

namespace SdvTestFramework.Runner.Commands;

public static class RepoCommand
{
    public static Func<ReadOnlyMemory<string>, CancellationToken, Task<int>> RunExecutor { get; set; }
        = RunCommand.RunAsync;

    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("[repo] expected init, run, or repeat");
            return 2;
        }

        return args.Span[0] switch
        {
            "run" => await RunRepoAsync(args[1..], ct),
            "repeat" => await RepeatRepoAsync(args[1..], ct),
            "init" => InitCommandUnavailable(),
            _ => Unknown(args.Span[0]),
        };
    }

    private static int InitCommandUnavailable()
    {
        Console.Error.WriteLine("[repo] init command is registered by the scaffold generator task");
        return 2;
    }

    private static int Unknown(string value)
    {
        Console.Error.WriteLine($"[repo] unknown subcommand: {value}");
        return 2;
    }

    private static async Task<int> RunRepoAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var parsed = ParseRunArgs(args);
        if (!parsed.Ok)
        {
            Console.Error.WriteLine(parsed.Error);
            return 2;
        }

        try
        {
            var config = RepoTestConfig.Load(parsed.RepoRoot);
            var plan = RepoRunPlanner.BuildRunPlan(parsed.RepoRoot, config, parsed.Request);

            if (parsed.Request.DryRun)
            {
                PrintDryRun(plan);
                return 0;
            }

            if (!parsed.Request.NoBuild && plan.BuildCommand is { } build)
            {
                var exit = await RunProcessAsync(parsed.RepoRoot, build, ct);
                if (exit != 0)
                    return exit;
            }

            return await RunExecutor(plan.FrobbyArgs.ToArray().AsMemory(), ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"[repo] {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RepeatRepoAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var count = 3;
        var remaining = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            if ((value == "--count" || value == "-n") && i + 1 < args.Length)
            {
                if (!int.TryParse(args.Span[++i], out count) || count < 1)
                {
                    Console.Error.WriteLine("[repo repeat] --count must be a positive integer");
                    return 2;
                }
                continue;
            }
            remaining.Add(value);
        }

        var parsed = ParseRunArgs(remaining.ToArray().AsMemory());
        if (!parsed.Ok)
        {
            Console.Error.WriteLine(parsed.Error);
            return 2;
        }

        var config = RepoTestConfig.Load(parsed.RepoRoot);
        var passed = 0;
        var worst = 0;
        for (var run = 1; run <= count; run++)
        {
            var runName = $"run-{run:00}";
            var repeatReport = parsed.Request.ReportDir
                ?? Path.Combine(Path.GetTempPath(), $"{config.Project.Slug}-frobby-repeat-{config.Project.Version}", runName);
            var request = parsed.Request with
            {
                NoBuild = parsed.Request.NoBuild || run > 1,
                ReportDir = repeatReport
            };
            var plan = RepoRunPlanner.BuildRunPlan(parsed.RepoRoot, config, request);
            Console.WriteLine($"[repo repeat] {run}/{count} {runName}");
            if (request.DryRun)
            {
                PrintDryRun(plan);
                continue;
            }
            if (!request.NoBuild && plan.BuildCommand is { } build)
            {
                var buildExit = await RunProcessAsync(parsed.RepoRoot, build, ct);
                if (buildExit != 0)
                {
                    worst = Math.Max(worst, buildExit);
                    continue;
                }
            }
            var exit = await RunExecutor(plan.FrobbyArgs.ToArray().AsMemory(), ct);
            if (exit == 0) passed++;
            else worst = Math.Max(worst, exit);
        }

        if (!parsed.Request.DryRun)
            Console.WriteLine($"[repo repeat] {passed}/{count} passed");
        return worst;
    }

    private static async Task<int> RunProcessAsync(string workingDirectory, IReadOnlyList<string> command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(command[0])
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var arg in command.Skip(1))
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {command[0]}");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private static void PrintDryRun(RepoRunPlan plan)
    {
        Console.WriteLine($"cd {Quote(plan.RepoRoot)}");
        if (plan.BuildCommand is { } build)
            Console.WriteLine(Format(build));
        Console.WriteLine(Format(new[] { "sdv-test" }.Concat(plan.FrobbyArgs)));
        Console.WriteLine($"report hub: {Path.Combine(plan.ReportDir, "index.html")}");
    }

    private static string Format(IEnumerable<string> args)
        => string.Join(" ", args.Select(Quote));

    private static string Quote(string value)
        => value.Contains(' ') || value.Contains('[') || value.Contains(']') || value.Contains('\'')
            ? "'" + value.Replace("'", "'\\''") + "'"
            : value;

    private static ParsedRunArgs ParseRunArgs(ReadOnlyMemory<string> args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var visible = false;
        var noBuild = false;
        var dryRun = false;
        var baseline = false;
        string? modSet = null;
        string? reportDir = null;
        var targets = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            switch (value)
            {
                case "--repo-root":
                    if (++i >= args.Length) return ParsedRunArgs.Fail("--repo-root requires a value");
                    repoRoot = args.Span[i];
                    continue;
                case "--visible":
                    visible = true;
                    continue;
                case "--headless":
                    visible = false;
                    continue;
                case "--no-build":
                    noBuild = true;
                    continue;
                case "--dry-run":
                    dryRun = true;
                    continue;
                case "--baseline":
                    baseline = true;
                    continue;
                case "--mod-set":
                    if (++i >= args.Length) return ParsedRunArgs.Fail("--mod-set requires a value");
                    modSet = args.Span[i];
                    continue;
                case "--report-dir":
                    if (++i >= args.Length) return ParsedRunArgs.Fail("--report-dir requires a value");
                    reportDir = args.Span[i];
                    continue;
            }

            if (value.StartsWith("-", StringComparison.Ordinal))
                return ParsedRunArgs.Fail($"unknown option: {value}");
            targets.Add(value);
        }

        return ParsedRunArgs.Success(
            Path.GetFullPath(repoRoot),
            new RepoRunRequest(visible, noBuild, dryRun, baseline, modSet, reportDir, targets));
    }

    private sealed record ParsedRunArgs(bool Ok, string RepoRoot, RepoRunRequest Request, string Error)
    {
        public static ParsedRunArgs Success(string repoRoot, RepoRunRequest request) => new(true, repoRoot, request, string.Empty);
        public static ParsedRunArgs Fail(string error) => new(false, string.Empty, new RepoRunRequest(false, false, false, false, null, null, Array.Empty<string>()), error);
    }
}
```

Modify `src/Runner/Program.cs`:

```csharp
"repo" => await RepoCommand.RunAsync(args.AsMemory()[1..], cts.Token),
```

Add help lines near the other commands:

```csharp
w.WriteLine("  repo <init|run|repeat> [args]");
w.WriteLine("                    Manage and run repo-local Frobby scaffolds from sdv-test.config.json.");
```

- [ ] **Step 8: Run repo command tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "RepoRunPlannerTests|RepoCommandTests"
```

Expected: `Passed!` for both test classes.

- [ ] **Step 9: Commit repo run command**

```bash
git add src/Runner/Repo/RepoRunPlanner.cs src/Runner/Commands/RepoCommand.cs src/Runner/Program.cs tests/Runner.Tests/Repo/RepoRunPlannerTests.cs tests/Runner.Tests/Repo/RepoCommandTests.cs
git commit -m "feat: run repo-local frobby scaffolds"
```

---

### Task 3: Neutral Scaffold Generator

**Files:**
- Create: `src/Runner/Repo/RepoScaffoldGenerator.cs`
- Test: `tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs`

- [ ] **Step 1: Write scaffold generator tests**

Create `tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs`:

```csharp
using System;
using System.IO;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

public class RepoScaffoldGeneratorTests
{
    [Fact]
    public void Generate_WritesNeutralScriptsConfigDocsAndSampleScenario()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repo-scaffold-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            RepoScaffoldGenerator.Generate(root, new RepoScaffoldOptions(
                ProjectName: "Example Mod",
                Slug: "example-mod",
                Version: "0.1.0",
                BuildCommand: "dotnet",
                BuildArgs: new[] { "build", "Example.sln", "--configuration", "Release" },
                ExtraMods: new[] { "bin/Release/net6.0", "${SDV_GAME_MODS}/ContentPatcher" },
                BaselineTarget: "tests/sdv/01-example-core-loads.test.json",
                Force: false));

            Assert.True(File.Exists(Path.Combine(root, "sdv-test.config.json")));
            Assert.True(File.Exists(Path.Combine(root, "scripts", "sdv-test")));
            Assert.True(File.Exists(Path.Combine(root, "scripts", "sdv-repeat")));
            Assert.True(File.Exists(Path.Combine(root, "tests", "sdv", "01-example-core-loads.test.json")));
            Assert.True(File.Exists(Path.Combine(root, "tests", "sdv", "fragments", ".gitkeep")));
            Assert.True(File.Exists(Path.Combine(root, "tests", "sdv", "baselines", ".gitkeep")));
            Assert.True(File.Exists(Path.Combine(root, "tests", "scripts", "sdv-test-dry-run.sh")));
            Assert.True(File.Exists(Path.Combine(root, "tests", "scripts", "sdv-repeat-dry-run.sh")));
            Assert.True(File.Exists(Path.Combine(root, "docs", "FROBBY.md")));

            var allText = string.Join("\n",
                File.ReadAllText(Path.Combine(root, "scripts", "sdv-test")),
                File.ReadAllText(Path.Combine(root, "scripts", "sdv-repeat")),
                File.ReadAllText(Path.Combine(root, "docs", "FROBBY.md")));

            Assert.DoesNotContain("Starberg", allText);
            Assert.DoesNotContain("starberg", allText);
            Assert.DoesNotContain("stonks", allText);
            Assert.Contains("sdv-test repo run", allText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_ExistingFileWithoutForce_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), $"repo-scaffold-existing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sdv-test.config.json"), "{}");
        try
        {
            var ex = Assert.Throws<IOException>(() => RepoScaffoldGenerator.Generate(root, new RepoScaffoldOptions(
                ProjectName: "Example Mod",
                Slug: "example-mod",
                Version: "0.1.0",
                BuildCommand: "dotnet",
                BuildArgs: new[] { "build", "Example.sln" },
                ExtraMods: new[] { "bin/Release/net6.0" },
                BaselineTarget: null,
                Force: false)));

            Assert.Contains("sdv-test.config.json", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run scaffold tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoScaffoldGeneratorTests
```

Expected: compile fails because `RepoScaffoldGenerator` and `RepoScaffoldOptions` do not exist.

- [ ] **Step 3: Implement scaffold generator**

Create `src/Runner/Repo/RepoScaffoldGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Runner.Repo;

public sealed record RepoScaffoldOptions(
    string ProjectName,
    string Slug,
    string Version,
    string BuildCommand,
    IReadOnlyList<string> BuildArgs,
    IReadOnlyList<string> ExtraMods,
    string? BaselineTarget,
    bool Force);

public static class RepoScaffoldGenerator
{
    public static int RunInit(ReadOnlyMemory<string> args)
    {
        var parsed = ParseInit(args);
        if (!parsed.Ok)
        {
            Console.Error.WriteLine(parsed.Error);
            return 2;
        }

        try
        {
            Generate(parsed.RepoRoot, parsed.Options);
            Console.WriteLine($"[repo init] wrote scaffold to {parsed.RepoRoot}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"[repo init] {ex.Message}");
            return 2;
        }
    }

    public static void Generate(string repoRoot, RepoScaffoldOptions options)
    {
        Directory.CreateDirectory(repoRoot);
        var sampleScenarioPath = options.BaselineTarget ?? "tests/sdv/01-example-core-loads.test.json";
        var config = new RepoTestConfig
        {
            Project = new RepoProjectConfig
            {
                Name = options.ProjectName,
                Slug = options.Slug,
                Version = options.Version,
            },
            Build = new RepoBuildConfig
            {
                Command = options.BuildCommand,
                Args = options.BuildArgs.ToList(),
            },
            DefaultTarget = "tests/sdv",
            BaselineTarget = options.BaselineTarget,
            ModSets = new List<RepoModSetConfig>
            {
                new() { Name = "core", ExtraMods = options.ExtraMods.ToList() },
            },
        };

        WriteFile(repoRoot, "sdv-test.config.json", JsonSerializer.Serialize(config, new JsonSerializerOptions(ProtocolJson.Options) { WriteIndented = true }) + Environment.NewLine, options.Force);
        WriteFile(repoRoot, "scripts/sdv-test", SdvTestScript(), options.Force, executable: true);
        WriteFile(repoRoot, "scripts/sdv-repeat", SdvRepeatScript(sampleScenarioPath), options.Force, executable: true);
        WriteFile(repoRoot, "tests/sdv/fragments/.gitkeep", string.Empty, options.Force);
        WriteFile(repoRoot, "tests/sdv/baselines/.gitkeep", string.Empty, options.Force);
        WriteFile(repoRoot, sampleScenarioPath, SampleScenario(), options.Force);
        WriteFile(repoRoot, "tests/scripts/sdv-test-dry-run.sh", SdvTestDryRunScript(), options.Force, executable: true);
        WriteFile(repoRoot, "tests/scripts/sdv-repeat-dry-run.sh", SdvRepeatDryRunScript(sampleScenarioPath), options.Force, executable: true);
        WriteFile(repoRoot, "docs/FROBBY.md", FrobbyDocs(options.ProjectName, options.Slug, options.Version, sampleScenarioPath), options.Force);
    }

    private static void WriteFile(string root, string relative, string content, bool force, bool executable = false)
    {
        var path = Path.Combine(root, relative);
        if (!force && File.Exists(path))
            throw new IOException($"refusing to overwrite existing file: {path}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (executable && OperatingSystem.IsLinux())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static string SdvTestScript() => """
    #!/usr/bin/env bash
    set -euo pipefail
    SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
    REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
    FROBBY_ROOT="${FROBBY_ROOT:-"$REPO_ROOT/../frobby/sdv-test-framework"}"
    if [[ -d "$FROBBY_ROOT/src/Runner" ]]; then
      cd "$FROBBY_ROOT"
      exec dotnet run --project src/Runner -- repo run --repo-root "$REPO_ROOT" "$@"
    fi
    exec sdv-test repo run --repo-root "$REPO_ROOT" "$@"
    """;

    private static string SdvRepeatScript(string sampleScenarioPath) => """
    #!/usr/bin/env bash
    set -euo pipefail
    SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
    REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
    FROBBY_ROOT="${FROBBY_ROOT:-"$REPO_ROOT/../frobby/sdv-test-framework"}"
    if [[ -d "$FROBBY_ROOT/src/Runner" ]]; then
      cd "$FROBBY_ROOT"
      exec dotnet run --project src/Runner -- repo repeat --repo-root "$REPO_ROOT" "$@"
    fi
    exec sdv-test repo repeat --repo-root "$REPO_ROOT" "$@"
    """;

    private static string SampleScenario() => """
    {
      "name": "example_core_loads",
      "fixture": "m0spike_436515781",
      "config": { "seed": 42 },
      "steps": [
        { "action": "wait.ms", "args": { "ms": 500 } },
        { "action": "draw.arm", "args": { "ticks": 10 } },
        { "action": "freeze.begin", "args": {} },
        { "action": "screenshot.capture", "args": { "name": "final" } }
      ],
      "assertions": [
        {
          "type": "state",
          "expr": "state.mods.unique_ids contains 'REPLACE_WITH_MOD_UNIQUE_ID'",
          "message": "Expected mod should be loaded"
        }
      ]
    }
    """;

    private static string SdvTestDryRunScript() => """
    #!/usr/bin/env bash
    set -euo pipefail
    ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
    output="$("$ROOT/scripts/sdv-test" --dry-run)"
    [[ "$output" == *"run-suite"* ]] || { echo "expected run-suite"; exit 1; }
    [[ "$output" == *"--headless"* ]] || { echo "expected --headless"; exit 1; }
    [[ "$output" == *"--extra-mod"* ]] || { echo "expected --extra-mod"; exit 1; }
    echo "PASS sdv-test dry-run behavior"
    """;

    private static string SdvRepeatDryRunScript(string sampleScenarioPath) => $$"""
    #!/usr/bin/env bash
    set -euo pipefail
    ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
    output="$("$ROOT/scripts/sdv-repeat" --dry-run --count 2 {{sampleScenarioPath}})"
    [[ "$output" == *"run-01"* ]] || { echo "expected run-01"; exit 1; }
    [[ "$output" == *"run-02"* ]] || { echo "expected run-02"; exit 1; }
    echo "PASS sdv-repeat dry-run behavior"
    """;

    private static string FrobbyDocs(string projectName, string slug, string version, string sampleScenarioPath) => $"""
    # Frobby Validation

    This repo uses Frobby for Stardew Valley mod UI and smoke validation.

    ```bash
    ./scripts/sdv-test
    ./scripts/sdv-test --visible
    ./scripts/sdv-test --dry-run
    ./scripts/sdv-repeat --count 3 {sampleScenarioPath}
    ```

    Default reports:

    - `/tmp/{slug}-frobby-results-{version}/index.html`
    - `/tmp/{slug}-frobby-repeat-{version}/run-NN/index.html`

    The wrapper defaults to headless mode. Use `--visible` only while actively debugging.
    Project: {projectName}
    """;

    private static ParsedInit ParseInit(ReadOnlyMemory<string> args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var projectName = "Example Mod";
        var slug = "example-mod";
        var version = "0.1.0";
        var buildCommand = "dotnet";
        var buildArgs = new List<string>();
        var extraMods = new List<string>();
        string? baselineTarget = null;
        var force = false;

        for (var i = 0; i < args.Length; i++)
        {
            var value = args.Span[i];
            switch (value)
            {
                case "--repo-root":
                    if (++i >= args.Length) return ParsedInit.Fail("--repo-root requires a value");
                    repoRoot = args.Span[i];
                    continue;
                case "--project-name":
                    if (++i >= args.Length) return ParsedInit.Fail("--project-name requires a value");
                    projectName = args.Span[i];
                    continue;
                case "--slug":
                    if (++i >= args.Length) return ParsedInit.Fail("--slug requires a value");
                    slug = args.Span[i];
                    continue;
                case "--version":
                    if (++i >= args.Length) return ParsedInit.Fail("--version requires a value");
                    version = args.Span[i];
                    continue;
                case "--build-command":
                    if (++i >= args.Length) return ParsedInit.Fail("--build-command requires a value");
                    buildCommand = args.Span[i];
                    continue;
                case "--build-arg":
                    if (++i >= args.Length) return ParsedInit.Fail("--build-arg requires a value");
                    buildArgs.Add(args.Span[i]);
                    continue;
                case "--extra-mod":
                    if (++i >= args.Length) return ParsedInit.Fail("--extra-mod requires a value");
                    extraMods.Add(args.Span[i]);
                    continue;
                case "--baseline-target":
                    if (++i >= args.Length) return ParsedInit.Fail("--baseline-target requires a value");
                    baselineTarget = args.Span[i];
                    continue;
                case "--force":
                    force = true;
                    continue;
            }
            return ParsedInit.Fail($"unknown option: {value}");
        }

        if (buildArgs.Count == 0)
            buildArgs.AddRange(new[] { "build" });
        if (extraMods.Count == 0)
            extraMods.Add("bin/Release/net6.0");

        return ParsedInit.Success(
            Path.GetFullPath(repoRoot),
            new RepoScaffoldOptions(projectName, slug, version, buildCommand, buildArgs, extraMods, baselineTarget, force));
    }

    private sealed record ParsedInit(bool Ok, string RepoRoot, RepoScaffoldOptions Options, string Error)
    {
        public static ParsedInit Success(string repoRoot, RepoScaffoldOptions options) => new(true, repoRoot, options, string.Empty);
        public static ParsedInit Fail(string error) => new(false, string.Empty, new RepoScaffoldOptions("Example Mod", "example-mod", "0.1.0", "dotnet", Array.Empty<string>(), Array.Empty<string>(), null, false), error);
    }
}
```

- [ ] **Step 4: Run scaffold tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter RepoScaffoldGeneratorTests
```

Expected: `Passed!` for `RepoScaffoldGeneratorTests`.

- [ ] **Step 5: Register `repo init` with the command**

Modify `src/Runner/Commands/RepoCommand.cs`, replacing `InitCommandUnavailable()` with:

```csharp
RepoScaffoldGenerator.RunInit(args[1..])
```

Remove the `InitCommandUnavailable` method from `RepoCommand`.

- [ ] **Step 6: Run repo command and scaffold tests together**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "RepoCommandTests|RepoScaffoldGeneratorTests"
```

Expected: both test classes pass.

- [ ] **Step 7: Commit scaffold generator**

```bash
git add src/Runner/Repo/RepoScaffoldGenerator.cs src/Runner/Commands/RepoCommand.cs tests/Runner.Tests/Repo/RepoScaffoldGeneratorTests.cs
git commit -m "feat: generate neutral repo scaffold"
```

---

### Task 4: Rich Loaded-Mod State And Contains Assertions

**Files:**
- Modify: `src/Protocol/Models/ModsState.cs`
- Modify: `src/Harness/Handlers/StateModsHandler.cs`
- Modify: `tests/Harness.Tests/StateModsHandlerTests.cs`
- Modify: `src/Runner/Scenarios/ScenarioRunner.cs`
- Modify: `tests/Runner.Tests/ScenarioRunnerDslTests.cs`

- [ ] **Step 1: Write loaded-mod metadata tests**

Modify `tests/Harness.Tests/StateModsHandlerTests.cs` so `FakeModInfo` and `FakeManifest` carry metadata:

```csharp
private sealed class FakeModInfo : IModInfo
{
    public FakeModInfo(string uniqueId, bool isContentPack = false, string? contentPackFor = null)
    {
        Manifest = new FakeManifest(uniqueId)
        {
            Name = uniqueId + " Name",
            Version = new SemanticVersion(1, 2, 3),
            ContentPackFor = contentPackFor is null ? null : new FakeContentPackFor(contentPackFor),
        };
        IsContentPack = isContentPack;
    }

    public IManifest Manifest { get; }
    public bool IsContentPack { get; }
}

private sealed class FakeContentPackFor : IManifestContentPackFor
{
    public FakeContentPackFor(string uniqueId) { UniqueID = uniqueId; }
    public string UniqueID { get; }
    public ISemanticVersion? MinimumVersion { get; set; }
}
```

Add this assertion to `Handle_RegistryWithMods_ReturnsAllUniqueIds`:

```csharp
Assert.Equal(new[] { "A.B", "C.D", "E.F" }, state!.UniqueIds);
Assert.Equal("A.B", state.Mods[0].UniqueId);
Assert.Equal("A.B Name", state.Mods[0].Name);
Assert.Equal("1.2.3", state.Mods[0].Version);
```

Add a new test:

```csharp
[Fact]
public void Handle_ContentPack_IncludesContentPackTarget()
{
    try
    {
        StateModsHandler.Registry = new FakeRegistry(
            new FakeModInfo("Pathoschild.ContentPatcher"),
            new FakeModInfo("Example.CP", isContentPack: true, contentPackFor: "Pathoschild.ContentPatcher"));

        var resp = StateModsHandler.Handle(null);
        var state = System.Text.Json.JsonSerializer.Deserialize<ModsState>(
            resp.GetRawText(), SdvTestFramework.Protocol.Json.ProtocolJson.Options);

        Assert.NotNull(state);
        Assert.Equal("Example.CP", state!.Mods[1].UniqueId);
        Assert.True(state.Mods[1].IsContentPack);
        Assert.Equal("Pathoschild.ContentPatcher", state.Mods[1].ContentPackFor);
    }
    finally { StateModsHandler.Registry = null; }
}
```

Change `FakeRegistry` to accept `FakeModInfo` directly:

```csharp
public FakeRegistry(params FakeModInfo[] mods)
{
    _mods = new List<IModInfo>(mods);
}

public FakeRegistry(params string[] uniqueIds)
{
    _mods = uniqueIds.Select(id => new FakeModInfo(id)).Cast<IModInfo>().ToList();
}
```

- [ ] **Step 2: Run metadata tests red**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter StateModsHandlerTests
```

Expected: compile fails because `ModsState.UniqueIds`, `ModsState.Mods`, and `LoadedModSummary` do not exist.

- [ ] **Step 3: Implement metadata shape**

Replace `src/Protocol/Models/ModsState.cs` with:

```csharp
using System;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>state.mods</c>.</summary>
public sealed class ModsState
{
    /// <summary>Loaded mod UniqueIDs, in SMAPI load order. Kept for compact assertions and fixture metadata.</summary>
    public string[] UniqueIds { get; set; } = Array.Empty<string>();

    /// <summary>Loaded mod metadata, in SMAPI load order.</summary>
    public LoadedModSummary[] Mods { get; set; } = Array.Empty<LoadedModSummary>();
}

public sealed class LoadedModSummary
{
    public string UniqueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsContentPack { get; set; }
    public string? ContentPackFor { get; set; }
}
```

Modify `src/Harness/Handlers/StateModsHandler.cs`:

```csharp
public static JsonElement Handle(JsonElement? paramsElement)
{
    var mods = new List<LoadedModSummary>();
    if (Registry is { } reg)
    {
        foreach (var mod in reg.GetAll())
        {
            var manifest = mod.Manifest;
            if (string.IsNullOrEmpty(manifest?.UniqueID))
                continue;

            mods.Add(new LoadedModSummary
            {
                UniqueId = manifest.UniqueID,
                Name = manifest.Name ?? string.Empty,
                Version = manifest.Version?.ToString() ?? string.Empty,
                IsContentPack = mod.IsContentPack,
                ContentPackFor = manifest.ContentPackFor?.UniqueID,
            });
        }
    }

    return ProtocolJson.ToElement(new ModsState
    {
        UniqueIds = mods.Select(m => m.UniqueId).ToArray(),
        Mods = mods.ToArray(),
    });
}
```

- [ ] **Step 4: Update fixture builder for `unique_ids`**

Modify `src/Runner/Fixtures/FixtureBuilder.cs` where it reads `ModsState`:

```csharp
var modsState = JsonSerializer.Deserialize<ModsState>(modsResp.Result?.GetRawText() ?? "{}", ProtocolJson.Options);
var mods = modsState?.UniqueIds ?? Array.Empty<string>();
```

Modify `tests/Runner.Tests/FixtureBuilderTests.cs` fake response:

```csharp
"state.mods" => JsonDocument.Parse("{\"unique_ids\":[\"A.B\",\"C.D\"],\"mods\":[{\"unique_id\":\"A.B\",\"name\":\"A\",\"version\":\"1.0.0\",\"is_content_pack\":false},{\"unique_id\":\"C.D\",\"name\":\"C\",\"version\":\"1.0.0\",\"is_content_pack\":true,\"content_pack_for\":\"Pathoschild.ContentPatcher\"}]}").RootElement,
```

- [ ] **Step 5: Run metadata tests green**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter StateModsHandlerTests
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter FixtureBuilderTests
```

Expected: both commands pass.

- [ ] **Step 6: Write contains assertion tests**

Append these tests to `tests/Runner.Tests/ScenarioRunnerDslTests.cs`:

```csharp
[Fact]
public async Task StateAssertion_StringArrayContains_Matches()
{
    var socket = SocketPath();
    var (cts, server, client) = await StartFakeHarnessWithMethodJson(socket, "state.mods",
        "{\"unique_ids\":[\"Pathoschild.ContentPatcher\",\"FlashShifter.SVECode\"],\"mods\":[]}");
    using var _ = cts; using var __ = client;

    var runner = new ScenarioRunner(client);
    var spec = new ScenarioSpec
    {
        Name = "mods_contains",
        Assertions = new()
        {
            new ScenarioAssertion { Type = "state", Expr = "state.mods.unique_ids contains 'FlashShifter.SVECode'" },
            new ScenarioAssertion { Type = "state", Expr = "state.mods.unique_ids contains 'Missing.Mod'" },
        },
    };

    var report = await runner.RunAsync(spec, cts.Token);

    Assert.Equal(2, report.AssertionsRun);
    Assert.Equal(1, report.AssertionsPassed);
    cts.Cancel();
    try { await server; } catch (OperationCanceledException) { }
}

[Fact]
public async Task StateAssertion_ObjectArrayContains_MatchesField()
{
    var socket = SocketPath();
    var (cts, server, client) = await StartFakeHarnessWithMethodJson(socket, "state.mods",
        "{\"unique_ids\":[\"FlashShifter.SVECode\"],\"mods\":[{\"unique_id\":\"FlashShifter.SVECode\",\"name\":\"Stardew Valley Expanded\",\"version\":\"1.15.11\",\"is_content_pack\":false}]}");
    using var _ = cts; using var __ = client;

    var runner = new ScenarioRunner(client);
    var spec = new ScenarioSpec
    {
        Name = "mods_object_contains",
        Assertions = new()
        {
            new ScenarioAssertion { Type = "state", Expr = "state.mods.mods contains unique_id 'FlashShifter.SVECode'" },
            new ScenarioAssertion { Type = "state", Expr = "state.mods.mods contains name 'Missing'" },
        },
    };

    var report = await runner.RunAsync(spec, cts.Token);

    Assert.Equal(2, report.AssertionsRun);
    Assert.Equal(1, report.AssertionsPassed);
    cts.Cancel();
    try { await server; } catch (OperationCanceledException) { }
}
```

Add this helper near the existing fake harness helpers:

```csharp
private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client)> StartFakeHarnessWithMethodJson(
    string socket, string method, string methodJson)
{
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "scenario.end" => JsonDocument.Parse("{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ when req.Method == method => JsonDocument.Parse(methodJson).RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready",
                JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);
    for (int i = 0; i < 40 && !File.Exists(socket); i++) await Task.Delay(50, cts.Token);
    var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);
    return (cts, serverTask, client);
}
```

- [ ] **Step 7: Run contains tests red**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "StateAssertion_StringArrayContains_Matches|StateAssertion_ObjectArrayContains_MatchesField"
```

Expected: tests fail because the state assertion DSL does not understand `contains`.

- [ ] **Step 8: Implement contains operator**

In `src/Runner/Scenarios/ScenarioRunner.cs`, before the existing `!=` / `==` parse block in the `"state"` case, add:

```csharp
var containsMatch = System.Text.RegularExpressions.Regex.Match(
    a.Expr.Trim(),
    @"^state\.([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s+contains(?:\s+([A-Za-z_][A-Za-z0-9_]*))?\s+(['""])(.*?)\4$");
if (containsMatch.Success)
{
    var method = $"state.{containsMatch.Groups[1].Value}";
    var arrayProperty = containsMatch.Groups[2].Value;
    var objectField = containsMatch.Groups[3].Success ? containsMatch.Groups[3].Value : null;
    var literal = containsMatch.Groups[5].Value;

    var resp = await _session.InvokeAsync(method, params_: null, ct);
    if (resp.Error is not null || resp.Result is not { } root)
    {
        await TryCaptureAssertionFailureAsync(ct);
        return (false, resp.Error?.Message);
    }

    if (!root.TryGetProperty(arrayProperty, out var array) || array.ValueKind != JsonValueKind.Array)
        return (false, $"state.{containsMatch.Groups[1].Value}.{arrayProperty} was not an array");

    var matched = false;
    foreach (var element in array.EnumerateArray())
    {
        if (objectField is null)
        {
            matched = element.ValueKind == JsonValueKind.String && string.Equals(element.GetString(), literal, StringComparison.Ordinal);
        }
        else
        {
            matched = element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(objectField, out var field)
                && field.ValueKind == JsonValueKind.String
                && string.Equals(field.GetString(), literal, StringComparison.Ordinal);
        }

        if (matched)
            break;
    }

    if (!matched) await TryCaptureAssertionFailureAsync(ct);
    return (matched, matched ? null : $"expected {arrayProperty} to contain '{literal}'");
}
```

- [ ] **Step 9: Run contains tests green**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "StateAssertion_StringArrayContains_Matches|StateAssertion_ObjectArrayContains_MatchesField"
```

Expected: both tests pass.

- [ ] **Step 10: Commit mod state and contains assertions**

```bash
git add src/Protocol/Models/ModsState.cs src/Harness/Handlers/StateModsHandler.cs tests/Harness.Tests/StateModsHandlerTests.cs src/Runner/Fixtures/FixtureBuilder.cs tests/Runner.Tests/FixtureBuilderTests.cs src/Runner/Scenarios/ScenarioRunner.cs tests/Runner.Tests/ScenarioRunnerDslTests.cs
git commit -m "feat: expose loaded mod metadata"
```

---

### Task 5: Docs For Repo Scaffold And Expanded `state.mods`

**Files:**
- Modify: `README.md`
- Modify: `docs/mcp-quickstart.md`
- Modify: `docs/rpc-schema.md`

- [ ] **Step 1: Update `state.mods` RPC docs**

Replace the `state.mods` response section in `docs/rpc-schema.md` with:

```markdown
### state.mods

Return loaded SMAPI mod metadata in load order. `unique_ids` is a compact list for
state assertions and fixture metadata; `mods` contains richer per-mod information.

**Params:** none.

**Response:**

```json
{
  "unique_ids": ["Pathoschild.ContentPatcher", "SdvTestFramework.Harness"],
  "mods": [
    {
      "unique_id": "Pathoschild.ContentPatcher",
      "name": "Content Patcher",
      "version": "2.7.0",
      "is_content_pack": false
    },
    {
      "unique_id": "Example.Mod.CP",
      "name": "Example Content Pack",
      "version": "1.0.0",
      "is_content_pack": true,
      "content_pack_for": "Pathoschild.ContentPatcher"
    }
  ]
}
```
```

- [ ] **Step 2: Update README quickstart**

Add this paragraph to `README.md` after the mod-local workflow paragraph:

```markdown
For new mod repos, use the repo scaffold flow:

```bash
sdv-test repo init --project-name "Example Mod" --slug example-mod \
  --build-command dotnet --build-arg build --build-arg Example.sln \
  --extra-mod bin/Release/net6.0
./scripts/sdv-test --dry-run
```

The generated scripts read `sdv-test.config.json`, default to headless execution,
stage every configured `extra_mod`, and write a stable `/tmp/<slug>-frobby-results-<version>/`
report hub.
```

- [ ] **Step 3: Update MCP quickstart**

Add this note to `docs/mcp-quickstart.md` after the `SDV_EXTRA_MODS` environment bullet:

```markdown
- For repo-local workflows, prefer `sdv-test repo init` and the generated
  `scripts/sdv-test` wrapper over hand-written mod-specific shell scripts. The
  generated wrapper keeps headless defaults, extra mod staging, report paths, and
  repeat runs consistent across projects.
```

- [ ] **Step 4: Run docs whitespace check**

Run:

```bash
git diff --check
```

Expected: no output and exit code `0`.

- [ ] **Step 5: Commit docs**

```bash
git add README.md docs/mcp-quickstart.md docs/rpc-schema.md
git commit -m "docs: document repo scaffold workflow"
```

---

### Task 6: Generate And Adjust SVE Core Scaffold

**Files:**
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `sdv-test.config.json`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `scripts/sdv-test`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `scripts/sdv-repeat`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `tests/sdv/01-sve-core-loads.test.json`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `tests/sdv/fragments/.gitkeep`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `tests/sdv/baselines/.gitkeep`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `tests/scripts/sdv-test-dry-run.sh`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `tests/scripts/sdv-repeat-dry-run.sh`
- Create in `/home/fintan/stardewRepos/StardewValleyExpanded`: `docs/FROBBY.md`

- [ ] **Step 1: Generate the scaffold in SVE Core**

Run from Frobby:

```bash
dotnet run --project src/Runner -- repo init \
  --repo-root /home/fintan/stardewRepos/StardewValleyExpanded \
  --project-name "Stardew Valley Expanded" \
  --slug stardew-valley-expanded \
  --version 0.1.0 \
  --build-command dotnet \
  --build-arg build \
  --build-arg "Stardew Valley Expanded/StardewValleyExpanded.sln" \
  --build-arg --configuration \
  --build-arg Release \
  --extra-mod '${SDV_GAME_MODS}/ContentPatcher' \
  --extra-mod '${SDV_GAME_MODS}/FarmTypeManager' \
  --extra-mod "Stardew Valley Expanded/StardewValleyExpanded/bin/Release/net6.0" \
  --extra-mod "Stardew Valley Expanded/[CP] Stardew Valley Expanded" \
  --extra-mod "Stardew Valley Expanded/[FTM] Stardew Valley Expanded" \
  --baseline-target tests/sdv/01-sve-core-loads.test.json
```

Expected output contains:

```text
[repo init] wrote scaffold to /home/fintan/stardewRepos/StardewValleyExpanded
```

- [ ] **Step 2: Replace SVE sample scenario with Core assertions**

Edit `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/01-sve-core-loads.test.json`:

```json
{
  "name": "sve_core_loads",
  "fixture": "m0spike_436515781",
  "config": { "seed": 42 },
  "steps": [
    { "action": "wait.ms", "args": { "ms": 1000 } },
    { "action": "draw.arm", "args": { "ticks": 10 } },
    { "action": "freeze.begin", "args": {} },
    { "action": "screenshot.capture", "args": { "name": "final" } }
  ],
  "assertions": [
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'Pathoschild.ContentPatcher'",
      "message": "Content Patcher should be loaded for SVE Core"
    },
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'Esca.FarmTypeManager'",
      "message": "Farm Type Manager should be loaded for SVE Core"
    },
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'FlashShifter.SVECode'",
      "message": "SVE code mod should be loaded"
    },
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'FlashShifter.StardewValleyExpandedCP'",
      "message": "SVE Content Patcher pack should be loaded"
    },
    {
      "type": "state",
      "expr": "state.mods.unique_ids contains 'FlashShifter.SVE-FTM'",
      "message": "SVE Farm Type Manager pack should be loaded"
    },
    {
      "type": "state",
      "expr": "state.mods.mods contains content_pack_for 'Pathoschild.ContentPatcher'",
      "message": "Loaded mod metadata should identify a Content Patcher pack"
    },
    {
      "type": "state",
      "expr": "state.mods.mods contains content_pack_for 'Esca.FarmTypeManager'",
      "message": "Loaded mod metadata should identify a Farm Type Manager pack"
    }
  ]
}
```

- [ ] **Step 3: Replace SVE dry-run scripts with SVE scenario names**

In `/home/fintan/stardewRepos/StardewValleyExpanded/tests/scripts/sdv-repeat-dry-run.sh`, replace `tests/sdv/01-example-core-loads.test.json` with:

```bash
tests/sdv/01-sve-core-loads.test.json
```

In `/home/fintan/stardewRepos/StardewValleyExpanded/tests/scripts/sdv-test-dry-run.sh`, add checks:

```bash
[[ "$output" == *"Stardew Valley Expanded/[CP] Stardew Valley Expanded"* ]] || { echo "expected SVE CP path"; exit 1; }
[[ "$output" == *"Stardew Valley Expanded/[FTM] Stardew Valley Expanded"* ]] || { echo "expected SVE FTM path"; exit 1; }
```

- [ ] **Step 4: Run SVE dry-run tests**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework tests/scripts/sdv-test-dry-run.sh
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework tests/scripts/sdv-repeat-dry-run.sh
```

Expected:

```text
PASS sdv-test dry-run behavior
PASS sdv-repeat dry-run behavior
```

- [ ] **Step 5: Commit SVE scaffold**

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
git add sdv-test.config.json scripts/sdv-test scripts/sdv-repeat tests/sdv tests/scripts docs/FROBBY.md
git commit -m "test: add frobby repo scaffold"
```

---

### Task 7: Verification And Regression

**Files:**
- No source edits unless a verification failure identifies a real defect.

- [ ] **Step 1: Run focused Frobby unit tests**

Run:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
dotnet test tests/Runner.Tests/Runner.Tests.csproj --configuration Debug --filter "Repo|StateAssertion_StringArrayContains_Matches|StateAssertion_ObjectArrayContains_MatchesField|FixtureBuilderTests"
dotnet test tests/Harness.Tests/Harness.Tests.csproj --configuration Debug --filter StateModsHandlerTests
```

Expected: both commands pass.

- [ ] **Step 2: Run full Frobby suite**

Run:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
dotnet test sdv-test-framework.slnx --configuration Debug
```

Expected: all non-skipped tests pass.

- [ ] **Step 3: Run SVE Core live smoke headless**

Run:

```bash
cd /home/fintan/stardewRepos/StardewValleyExpanded
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework ./scripts/sdv-test tests/sdv/01-sve-core-loads.test.json
```

Expected:

```text
1/1 passed
```

The report hub should be:

```text
/tmp/stardew-valley-expanded-frobby-results-0.1.0/index.html
```

- [ ] **Step 4: Run Starberg dry-run regression**

Run:

```bash
cd /home/fintan/stardewRepos/stonks
./scripts/sdv-test --dry-run
```

Expected: output still contains:

```text
run-suite
--fresh-process-per-scenario
--headless
/tmp/starberg-frobby-results-0.1.0
```

- [ ] **Step 5: Check mod-specific scaffold leaks in Frobby**

Run:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
rg -n "Starberg|starberg|STONKS|stonks|/home/fintan/stardewRepos/stonks|starberg-frobby|stonks_" README.md docs nuget src tests schemas -g '!docs/superpowers/plans/**' -g '!docs/superpowers/specs/**'
```

Expected: no output.

- [ ] **Step 6: Final whitespace and status checks**

Run:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
git diff --check
git status --short
cd /home/fintan/stardewRepos/StardewValleyExpanded
git status --short
```

Expected: no whitespace errors. Frobby should be clean after commits. SVE should be clean after the scaffold commit.

- [ ] **Step 7: Commit final Frobby verification doc note if needed**

If the live SVE smoke reveals a dependency-path note that belongs in Frobby docs, edit `docs/rpc-schema.md` or `README.md` with that exact note, then run:

```bash
cd /home/fintan/stardewRepos/frobby/sdv-test-framework
git add README.md docs/rpc-schema.md
git commit -m "docs: clarify repo scaffold dependency paths"
```

If no doc note is needed, skip this commit step and leave the repo clean.

---

## Self-Review Checklist

- Spec coverage: Tasks 1-3 implement config, path resolution, repo commands, generated scripts, and dry-run behavior. Task 4 implements the neutral loaded-mod smoke surface. Task 5 documents it. Task 6 proves SVE Core. Task 7 verifies Frobby and Starberg.
- Type consistency: `RepoTestConfig`, `RepoRunPlanner`, `RepoRunRequest`, `RepoRunPlan`, `RepoScaffoldGenerator`, and `RepoCommand` are named consistently across tasks.
- Scope: Optional SVE farm packs and Starberg migration are excluded from this plan.
