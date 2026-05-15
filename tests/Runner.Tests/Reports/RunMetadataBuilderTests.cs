using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Commands;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class RunMetadataBuilderTests
{
    [Fact]
    public void Build_RecordsHeadlessLauncherAndRepositoryRevisions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"run-meta-{Guid.NewGuid():N}");
        var runnerRepo = Path.Combine(root, "sdv-test-framework");
        var exampleModRepo = Path.Combine(root, "example-mod");
        var extraMod = Path.Combine(exampleModRepo, "src", "Example.Mod", "bin", "Release", "net6.0");

        try
        {
            Directory.CreateDirectory(extraMod);
            var runnerCommit = CreateGitRepo(runnerRepo);
            var exampleModCommit = CreateGitRepo(exampleModRepo);

            var opts = new RunCommandOptions(
                Paths: new[] { "tests/sdv" },
                Filter: null,
                ModsPath: null,
                ExtraMods: new[] { extraMod },
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
                ProfileId: null,
                ProfileCacheNamespace: null,
                ConfigOverlays: Array.Empty<ExtraModConfigOverlay>(),
                PreCreatedRunDir: null);

            var metadata = RunMetadataBuilder.Build(
                opts,
                effectiveHeadless: true,
                launcher: "xvfb-run",
                command: "sdv-test run --headless tests/sdv",
                workingDirectory: runnerRepo);

            Assert.Equal("headless", metadata.LaunchMode);
            Assert.True(metadata.Headless);
            Assert.Equal("xvfb-run", metadata.Launcher);
            Assert.Equal("sdv-test run --headless tests/sdv", metadata.Command);
            Assert.Equal(runnerRepo, metadata.WorkingDirectory);

            Assert.Contains(metadata.Repositories, repo =>
                repo.Label == "runner:sdv-test-framework"
                && repo.Path == runnerRepo
                && repo.Commit == runnerCommit
                && repo.Dirty == false);
            Assert.Contains(metadata.Repositories, repo =>
                repo.Label == "extra-mod:example-mod"
                && repo.Path == exampleModRepo
                && repo.Commit == exampleModCommit
                && repo.Dirty == false);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateGitRepo(string path)
    {
        Directory.CreateDirectory(path);
        RunGit(path, "init");
        File.WriteAllText(Path.Combine(path, "tracked.txt"), "tracked");
        RunGit(path, "add", "tracked.txt");
        RunGit(path, "-c", "user.name=Frobby Tests", "-c", "user.email=frobby@example.invalid", "commit", "-m", "init");
        return RunGit(path, "rev-parse", "--short=12", "HEAD").Trim();
    }

    private static string RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start git");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        return stdout;
    }
}
