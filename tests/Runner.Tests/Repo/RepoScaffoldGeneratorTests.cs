using System;
using System.IO;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Runner.Repo;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Repo;

[Collection("Console")]
public sealed class RepoScaffoldGeneratorTests : IDisposable
{
    private readonly string _repoRoot = CreateTempDirectory();

    [Fact]
    public void Generate_writes_neutral_scaffold_files()
    {
        RepoScaffoldGenerator.Generate(
            _repoRoot,
            new RepoScaffoldOptions(
                "Example Mod",
                "example-mod",
                "0.1.0",
                "dotnet",
                ["build"],
                ["bin/Release/net6.0"],
                "tests/sdv/baseline.test.json",
                Force: false));

        var expected = new[]
        {
            "sdv-test.config.json",
            "scripts/sdv-test",
            "scripts/sdv-repeat",
            "tests/sdv/baseline.test.json",
            "tests/sdv/fragments/.gitkeep",
            "tests/sdv/baselines/.gitkeep",
            "tests/scripts/sdv-test-dry-run.sh",
            "tests/scripts/sdv-repeat-dry-run.sh",
            "docs/FROBBY.md",
        };

        foreach (var relativePath in expected)
        {
            Assert.True(File.Exists(Path.Combine(_repoRoot, relativePath)), relativePath);
        }

        var configJson = File.ReadAllText(Path.Combine(_repoRoot, "sdv-test.config.json"));
        Assert.Contains(Environment.NewLine, configJson);
        Assert.Contains("\"defaultTarget\": \"tests/sdv\"", configJson);
        Assert.Contains("\"baselineTarget\": \"tests/sdv/baseline.test.json\"", configJson);

        var config = JsonSerializer.Deserialize<RepoTestConfig>(configJson, ProtocolJson.Options)!;
        Assert.Equal("Example Mod", config.Project.Name);
        Assert.Equal("example-mod", config.Project.Slug);
        Assert.Equal("0.1.0", config.Project.Version);
        Assert.Equal("dotnet", config.Build.Command);
        Assert.Equal(["build"], config.Build.Args);
        var modSet = Assert.Single(config.ModSets);
        Assert.Equal("core", modSet.Name);
        Assert.Equal(["bin/Release/net6.0"], modSet.ExtraMods);

        var scenario = File.ReadAllText(Path.Combine(_repoRoot, "tests/sdv/baseline.test.json"));
        Assert.Contains("REPLACE_WITH_MOD_UNIQUE_ID", scenario);
        Assert.Contains("state.mods.unique_ids", scenario);
        Assert.Contains("contains", scenario);
        AssertNeutralGeneratedText();
    }

    [Fact]
    public void Generate_scripts_and_docs_reference_repo_commands_without_project_specific_names()
    {
        RepoScaffoldGenerator.Generate(
            _repoRoot,
            new RepoScaffoldOptions(
                "Neutral Project",
                "neutral-project",
                "1.0.0",
                "dotnet",
                ["build"],
                ["bin/Release/net6.0"],
                BaselineTarget: null,
                Force: false));

        var scriptText = File.ReadAllText(Path.Combine(_repoRoot, "scripts/sdv-test"));
        var repeatText = File.ReadAllText(Path.Combine(_repoRoot, "scripts/sdv-repeat"));
        var docsText = File.ReadAllText(Path.Combine(_repoRoot, "docs/FROBBY.md"));

        Assert.Contains("sdv-test repo run", scriptText);
        Assert.Contains("sdv-test repo repeat", repeatText);
        Assert.Contains("sdv-test repo run", docsText);
        Assert.Contains("sdv-test repo repeat", docsText);
        Assert.Contains("repo deps import", docsText);
        Assert.Contains("repo deps doctor", docsText);
        Assert.Contains("deps", docsText);
        Assert.Contains("extraMods", docsText);
        Assert.Contains("FROBBY_ROOT", scriptText);
        Assert.Contains("../frobby/sdv-test-framework", scriptText);
        Assert.Contains("--visible", docsText);
        Assert.True(File.Exists(Path.Combine(_repoRoot, "tests/sdv/01-example-core-loads.test.json")));
        AssertNeutralGeneratedText();
    }

    [Fact]
    public void Generate_source_wrappers_run_dotnet_directly_from_source_root()
    {
        RepoScaffoldGenerator.Generate(_repoRoot, DefaultOptions());

        var scriptText = File.ReadAllText(Path.Combine(_repoRoot, "scripts/sdv-test"));
        var repeatText = File.ReadAllText(Path.Combine(_repoRoot, "scripts/sdv-repeat"));

        Assert.Contains("FROBBY_SOURCE_ROOT=\"${FROBBY_ROOT:-", scriptText);
        Assert.Contains("FROBBY_SOURCE_ROOT=\"${FROBBY_ROOT:-", repeatText);
        Assert.Contains("cd \"$FROBBY_SOURCE_ROOT\"", scriptText);
        Assert.Contains("cd \"$FROBBY_SOURCE_ROOT\"", repeatText);
        Assert.Contains("exec dotnet run", scriptText);
        Assert.Contains("exec dotnet run", repeatText);
        Assert.DoesNotContain("exec env -u FROBBY_ROOT dotnet run", scriptText);
        Assert.DoesNotContain("exec env -u FROBBY_ROOT dotnet run", repeatText);
        Assert.Contains("--project src/Runner/Runner.csproj", scriptText);
        Assert.Contains("--project src/Runner/Runner.csproj", repeatText);
        Assert.DoesNotContain("dotnet run --project \"$FROBBY_ROOT", scriptText);
        Assert.DoesNotContain("dotnet run --project \"$FROBBY_ROOT", repeatText);
    }

    [Fact]
    public void Generate_dry_run_scripts_report_success_after_wrapper_exits()
    {
        RepoScaffoldGenerator.Generate(_repoRoot, DefaultOptions());

        var testDryRun = File.ReadAllText(Path.Combine(_repoRoot, "tests/scripts/sdv-test-dry-run.sh"));
        var repeatDryRun = File.ReadAllText(Path.Combine(_repoRoot, "tests/scripts/sdv-repeat-dry-run.sh"));

        Assert.Contains("\"$REPO_ROOT/scripts/sdv-test\" --dry-run \"$@\"", testDryRun);
        Assert.Contains("\"$REPO_ROOT/scripts/sdv-repeat\" --dry-run \"$@\"", repeatDryRun);
        Assert.Contains("PASS sdv-test dry-run behavior", testDryRun);
        Assert.Contains("PASS sdv-repeat dry-run behavior", repeatDryRun);
        Assert.DoesNotContain("exec \"$REPO_ROOT/scripts/sdv-test\"", testDryRun);
        Assert.DoesNotContain("exec \"$REPO_ROOT/scripts/sdv-repeat\"", repeatDryRun);
    }

    [Fact]
    public void Generate_existing_file_without_force_throws_io_exception_with_path()
    {
        File.WriteAllText(Path.Combine(_repoRoot, "sdv-test.config.json"), "{}");

        var ex = Assert.Throws<IOException>(() => RepoScaffoldGenerator.Generate(_repoRoot, DefaultOptions()));

        Assert.Contains("sdv-test.config.json", ex.Message);
    }

    [Fact]
    public void Generate_force_overwrites_existing_file()
    {
        File.WriteAllText(Path.Combine(_repoRoot, "sdv-test.config.json"), "{}");

        RepoScaffoldGenerator.Generate(_repoRoot, DefaultOptions() with { Force = true });

        var configJson = File.ReadAllText(Path.Combine(_repoRoot, "sdv-test.config.json"));
        Assert.Contains("\"project\"", configJson);
        Assert.DoesNotContain("{}", configJson);
    }

    [Fact]
    public void Generate_baseline_target_parent_escape_throws_and_does_not_write_outside_root()
    {
        var repoRoot = Path.Combine(_repoRoot, "repo");
        Directory.CreateDirectory(repoRoot);
        var outsidePath = Path.Combine(_repoRoot, "escape.test.json");

        AssertInvalidBaselineTarget(() =>
            RepoScaffoldGenerator.Generate(repoRoot, DefaultOptions() with { BaselineTarget = "../escape.test.json" }));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void Generate_absolute_baseline_target_throws_and_does_not_write_outside_root()
    {
        var repoRoot = Path.Combine(_repoRoot, "repo");
        Directory.CreateDirectory(repoRoot);
        var outsidePath = Path.Combine(_repoRoot, "absolute.test.json");

        AssertInvalidBaselineTarget(() =>
            RepoScaffoldGenerator.Generate(repoRoot, DefaultOptions() with { BaselineTarget = outsidePath }));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void Generate_blank_baseline_target_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RepoScaffoldGenerator.Generate(_repoRoot, DefaultOptions() with { BaselineTarget = " " }));

        Assert.Contains("baseline", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_marks_scripts_executable_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        RepoScaffoldGenerator.Generate(_repoRoot, DefaultOptions());

        Assert.True(
            File.GetUnixFileMode(Path.Combine(_repoRoot, "scripts/sdv-test")).HasFlag(UnixFileMode.UserExecute));
        Assert.True(
            File.GetUnixFileMode(Path.Combine(_repoRoot, "scripts/sdv-repeat")).HasFlag(UnixFileMode.UserExecute));
        Assert.True(
            File.GetUnixFileMode(Path.Combine(_repoRoot, "tests/scripts/sdv-test-dry-run.sh")).HasFlag(UnixFileMode.UserExecute));
        Assert.True(
            File.GetUnixFileMode(Path.Combine(_repoRoot, "tests/scripts/sdv-repeat-dry-run.sh")).HasFlag(UnixFileMode.UserExecute));
    }

    private void AssertNeutralGeneratedText()
    {
        foreach (var path in Directory.EnumerateFiles(_repoRoot, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Star" + "berg", text);
            Assert.DoesNotContain("star" + "berg", text);
            Assert.DoesNotContain("sto" + "nks", text);
        }
    }

    private static RepoScaffoldOptions DefaultOptions()
        => new(
            "Example Mod",
            "example-mod",
            "0.1.0",
            "dotnet",
            ["build"],
            ["bin/Release/net6.0"],
            BaselineTarget: null,
            Force: false);

    private static void AssertInvalidBaselineTarget(Action action)
    {
        var ex = Record.Exception(action);
        Assert.True(
            ex is InvalidOperationException or IOException,
            $"Expected InvalidOperationException or IOException, got {ex?.GetType().FullName ?? "no exception"}.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "repo-scaffold-generator-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        Directory.Delete(_repoRoot, recursive: true);
    }
}
