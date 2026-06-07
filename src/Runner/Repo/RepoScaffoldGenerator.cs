using System;
using System.Collections.Generic;
using System.IO;
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
    private const string DefaultScenarioTarget = "tests/sdv/01-example-core-loads.test.json";

    public static void Generate(string repoRoot, RepoScaffoldOptions options)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("repo root is required.");
        }

        var fullRoot = Path.GetFullPath(repoRoot);
        var scenarioTarget = ValidateBaselineTarget(fullRoot, options.BaselineTarget);
        var scaffoldOptions = options with { BaselineTarget = scenarioTarget };
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RepoTestConfig.FileName] = ConfigJson(scaffoldOptions),
            ["scripts/sdv-test"] = RunWrapper(),
            ["scripts/sdv-repeat"] = RepeatWrapper(),
            ["scripts/sdv-preflight"] = PreflightWrapper(),
            [scenarioTarget] = SampleScenario(),
            ["tests/sdv/fragments/.gitkeep"] = string.Empty,
            ["tests/sdv/baselines/.gitkeep"] = string.Empty,
            ["tests/scripts/sdv-test-dry-run.sh"] = DryRunScript("sdv-test"),
            ["tests/scripts/sdv-repeat-dry-run.sh"] = DryRunScript("sdv-repeat"),
            ["docs/FROBBY.md"] = Docs(),
        };

        foreach (var relativePath in files.Keys)
        {
            var path = Path.Combine(fullRoot, relativePath);
            if (!options.Force && File.Exists(path))
            {
                throw new IOException($"Refusing to overwrite existing scaffold file: {path}");
            }
        }

        foreach (var (relativePath, text) in files)
        {
            var path = Path.Combine(fullRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        MarkExecutableIfPossible(Path.Combine(fullRoot, "scripts/sdv-test"));
        MarkExecutableIfPossible(Path.Combine(fullRoot, "scripts/sdv-repeat"));
        MarkExecutableIfPossible(Path.Combine(fullRoot, "scripts/sdv-preflight"));
        MarkExecutableIfPossible(Path.Combine(fullRoot, "tests/scripts/sdv-test-dry-run.sh"));
        MarkExecutableIfPossible(Path.Combine(fullRoot, "tests/scripts/sdv-repeat-dry-run.sh"));
    }

    public static int RunInit(ReadOnlyMemory<string> args)
    {
        try
        {
            var options = ParseInitOptions(args);
            Generate(options.RepoRoot, options.Scaffold);
            Console.Out.WriteLine("repo scaffold created at " + Path.GetFullPath(options.RepoRoot));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine("[repo] " + ex.Message);
            Console.Error.WriteLine("[repo] usage: sdv-test repo init [--repo-root PATH] [--project-name NAME] [--slug SLUG] [--version VERSION] [--build-command COMMAND] [--build-arg ARG ...] [--extra-mod PATH ...] [--baseline-target PATH] [--force]");
            return 2;
        }
    }

    private static InitOptions ParseInitOptions(ReadOnlyMemory<string> args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var repoRootFromOption = false;
        string? positionalRepoRoot = null;
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
                    repoRoot = ReadRequiredValue(args, ref i, value);
                    repoRootFromOption = true;
                    continue;
                case "--project-name":
                    projectName = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--slug":
                    slug = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--version":
                    version = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--build-command":
                    buildCommand = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--build-arg":
                    buildArgs.Add(ReadRequiredValue(args, ref i, value));
                    continue;
                case "--extra-mod":
                    extraMods.Add(ReadRequiredValue(args, ref i, value));
                    continue;
                case "--baseline-target":
                    baselineTarget = ReadRequiredValue(args, ref i, value);
                    continue;
                case "--force":
                    force = true;
                    continue;
                default:
                    if (value.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"unknown repo init option: {value}");
                    }

                    if (positionalRepoRoot is not null)
                    {
                        throw new InvalidOperationException("repo init accepts at most one positional repo path.");
                    }

                    positionalRepoRoot = RequireText(value, "repo path");
                    continue;
            }
        }

        if (repoRootFromOption && positionalRepoRoot is not null)
        {
            throw new InvalidOperationException("repo init repo path is ambiguous; pass either positional repo path or --repo-root, not both.");
        }

        if (positionalRepoRoot is not null)
        {
            repoRoot = positionalRepoRoot;
        }

        if (buildArgs.Count == 0)
        {
            buildArgs.Add("build");
        }

        if (extraMods.Count == 0)
        {
            extraMods.Add("bin/Release/net6.0");
        }

        var scaffold = new RepoScaffoldOptions(
            RequireText(projectName, "--project-name"),
            RequireText(slug, "--slug"),
            RequireText(version, "--version"),
            RequireText(buildCommand, "--build-command"),
            buildArgs,
            extraMods,
            baselineTarget,
            force);
        return new InitOptions(repoRoot, scaffold);
    }

    private static string ValidateBaselineTarget(string fullRoot, string? baselineTarget)
    {
        if (baselineTarget is null)
        {
            return DefaultScenarioTarget;
        }

        if (string.IsNullOrWhiteSpace(baselineTarget))
        {
            throw new InvalidOperationException("--baseline-target requires a non-empty relative path.");
        }

        if (Path.IsPathRooted(baselineTarget))
        {
            throw new InvalidOperationException("--baseline-target must be relative to the repo root.");
        }

        var candidate = Path.GetFullPath(Path.Combine(fullRoot, baselineTarget));
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException("--baseline-target must stay inside the repo root.");
        }

        return Path.GetRelativePath(fullRoot, candidate)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ConfigJson(RepoScaffoldOptions options)
    {
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
                Args = options.BuildArgs,
            },
            DefaultTarget = "tests/sdv",
            BaselineTarget = options.BaselineTarget,
            ModSets =
            [
                new RepoModSetConfig
                {
                    Name = "core",
                    ExtraMods = options.ExtraMods,
                },
            ],
            Profiles = null!,
        };
        var jsonOptions = new JsonSerializerOptions(ProtocolJson.Options) { WriteIndented = true };
        return JsonSerializer.Serialize(config, jsonOptions) + Environment.NewLine;
    }

    private static string RunWrapper()
        =>
            """
            #!/usr/bin/env bash
            set -euo pipefail

            SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
            REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
            FROBBY_ROOT_WAS_SET=0
            if [ -n "${FROBBY_ROOT+x}" ]; then
              FROBBY_ROOT_WAS_SET=1
            fi
            FROBBY_SOURCE_ROOT="${FROBBY_ROOT:-"$REPO_ROOT/../frobby/sdv-test-framework"}"

            if [ -f "$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj" ]; then
              FROBBY_SOURCE_ROOT="$(cd "$FROBBY_SOURCE_ROOT" && pwd -P)"
              unset FROBBY_ROOT
              cd "$FROBBY_SOURCE_ROOT"
              if [ "$FROBBY_ROOT_WAS_SET" -eq 1 ]; then
                exec dotnet run --no-build --project "$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj" -- repo run --repo-root "$REPO_ROOT" "$@"
              fi
              exec dotnet run --project "$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj" -- repo run --repo-root "$REPO_ROOT" "$@"
            fi

            exec sdv-test repo run --repo-root "$REPO_ROOT" "$@"
            """;

    private static string RepeatWrapper()
        =>
            """
            #!/usr/bin/env bash
            set -euo pipefail

            SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
            REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
            FROBBY_ROOT_WAS_SET=0
            if [ -n "${FROBBY_ROOT+x}" ]; then
              FROBBY_ROOT_WAS_SET=1
            fi
            FROBBY_SOURCE_ROOT="${FROBBY_ROOT:-"$REPO_ROOT/../frobby/sdv-test-framework"}"

            if [ -f "$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj" ]; then
              FROBBY_SOURCE_ROOT="$(cd "$FROBBY_SOURCE_ROOT" && pwd -P)"
              unset FROBBY_ROOT
              cd "$FROBBY_SOURCE_ROOT"
              if [ "$FROBBY_ROOT_WAS_SET" -eq 1 ]; then
                exec dotnet run --no-build --project "$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj" -- repo repeat --repo-root "$REPO_ROOT" "$@"
              fi
              exec dotnet run --project "$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj" -- repo repeat --repo-root "$REPO_ROOT" "$@"
            fi

            exec sdv-test repo repeat --repo-root "$REPO_ROOT" "$@"
            """;

    private static string PreflightWrapper()
        =>
            """
            #!/usr/bin/env bash
            set -euo pipefail

            SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
            REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
            FROBBY_SOURCE_ROOT="${FROBBY_ROOT:-"$REPO_ROOT/../frobby/sdv-test-framework"}"
            FROBBY_RUN_ARGS=()

            if [ -f "$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj" ]; then
              FROBBY_SOURCE_ROOT="$(cd "$FROBBY_SOURCE_ROOT" && pwd -P)"
              unset FROBBY_ROOT
              cd "$FROBBY_SOURCE_ROOT"
              RUNNER_DLL="$FROBBY_SOURCE_ROOT/src/Runner/bin/Debug/net10.0/sdv-test.dll"
              if [ ! -f "$RUNNER_DLL" ]; then
                echo "[preflight] Frobby source runner is not built: $RUNNER_DLL" >&2
                echo "[preflight] Run dotnet build \"$FROBBY_SOURCE_ROOT/src/Runner/Runner.csproj\" before preflight." >&2
                exit 2
              fi
              FROBBY_RUN_ARGS=(dotnet "$RUNNER_DLL")
            fi

            RUN_TARGETS=()
            if [ "$#" -eq 0 ]; then
              RUN_TARGETS=("$REPO_ROOT/tests/sdv")
            else
              for target in "$@"; do
                case "$target" in
                  /*) RUN_TARGETS+=("$target") ;;
                  *) RUN_TARGETS+=("$REPO_ROOT/$target") ;;
                esac
              done
            fi

            run_sdv_test() {
              if [ "${#FROBBY_RUN_ARGS[@]}" -ne 0 ]; then
                "${FROBBY_RUN_ARGS[@]}" "$@"
                return
              fi

              sdv-test "$@"
            }

            run_sdv_test list "$REPO_ROOT/tests/sdv"
            run_sdv_test repo deps doctor --repo-root "$REPO_ROOT"
            run_sdv_test repo run --repo-root "$REPO_ROOT" --dry-run "${RUN_TARGETS[@]}"
            echo "PASS preflight checks"
            """;

    private static string DryRunScript(string wrapper)
        =>
            $$"""
            #!/usr/bin/env bash
            set -euo pipefail

            SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
            REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

            "$REPO_ROOT/scripts/{{wrapper}}" --dry-run "$@"
            echo "PASS {{wrapper}} dry-run behavior"
            """;

    private static string SampleScenario()
        =>
            """
            {
              "name": "example_core_loads",
              "steps": [],
              "assertions": [
                {
                  "type": "state",
                  "expr": "state.mods.unique_ids contains 'REPLACE_WITH_MOD_UNIQUE_ID'"
                }
              ]
            }
            """;

    private static string Docs()
        =>
            """""
            # FROBBY repo scaffold

            This repository is configured for `sdv-test repo run` and `sdv-test repo repeat`.

            Use `scripts/sdv-test` to run the default scenario target:

            ```sh
            scripts/sdv-test
            ```

            Use `scripts/sdv-repeat` to repeat the suite:

            ```sh
            scripts/sdv-repeat --count 3
            ```

            Before launching SDV, use `scripts/sdv-preflight` to validate scenario JSON,
            check cached dependency mods, and print the resolved dry-run command:

            ```sh
            scripts/sdv-preflight
            scripts/sdv-preflight tests/sdv/01-example-core-loads.test.json
            ```

            The wrappers use a source checkout from `FROBBY_ROOT` when available, defaulting to `$REPO_ROOT/../frobby/sdv-test-framework`, and otherwise fall back to an installed `sdv-test`.

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

            Repo commands default to headless runs. Pass `--visible` when debugging locally.
            """"";

    private static string ReadRequiredValue(ReadOnlyMemory<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"{option} requires a value.");
        }

        var value = args.Span[++index];
        return RequireText(value, option);
    }

    private static string RequireText(string value, string option)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{option} requires a non-empty value.")
            : value;

    private static void MarkExecutableIfPossible(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private sealed record InitOptions(string RepoRoot, RepoScaffoldOptions Scaffold);
}
