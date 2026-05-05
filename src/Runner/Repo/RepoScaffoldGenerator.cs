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
        var scenarioTarget = string.IsNullOrWhiteSpace(options.BaselineTarget)
            ? DefaultScenarioTarget
            : options.BaselineTarget!;
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RepoTestConfig.FileName] = ConfigJson(options),
            ["scripts/sdv-test"] = RunWrapper(),
            ["scripts/sdv-repeat"] = RepeatWrapper(),
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
                    throw new InvalidOperationException($"unknown repo init option: {value}");
            }
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
            string.IsNullOrWhiteSpace(baselineTarget) ? null : baselineTarget,
            force);
        return new InitOptions(repoRoot, scaffold);
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
            FROBBY_ROOT="${FROBBY_ROOT:-"$REPO_ROOT/../frobby/sdv-test-framework"}"

            if [ -f "$FROBBY_ROOT/src/Runner/Runner.csproj" ]; then
              exec dotnet run --project "$FROBBY_ROOT/src/Runner/Runner.csproj" -- repo run --repo-root "$REPO_ROOT" "$@"
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
            FROBBY_ROOT="${FROBBY_ROOT:-"$REPO_ROOT/../frobby/sdv-test-framework"}"

            if [ -f "$FROBBY_ROOT/src/Runner/Runner.csproj" ]; then
              exec dotnet run --project "$FROBBY_ROOT/src/Runner/Runner.csproj" -- repo repeat --repo-root "$REPO_ROOT" "$@"
            fi

            exec sdv-test repo repeat --repo-root "$REPO_ROOT" "$@"
            """;

    private static string DryRunScript(string wrapper)
        =>
            $$"""
            #!/usr/bin/env bash
            set -euo pipefail

            SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
            REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

            exec "$REPO_ROOT/scripts/{{wrapper}}" --dry-run "$@"
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
            """
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

            The wrappers use a source checkout from `FROBBY_ROOT` when available, defaulting to `$REPO_ROOT/../frobby/sdv-test-framework`, and otherwise fall back to an installed `sdv-test`.

            Repo commands default to headless runs. Pass `--visible` when debugging locally.
            """;

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
        if (!OperatingSystem.IsLinux())
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
    }

    private sealed record InitOptions(string RepoRoot, RepoScaffoldOptions Scaffold);
}
