using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Commands;

namespace SdvTestFramework.Runner.Reports;

internal static class RunMetadataBuilder
{
    public static RunMetadata Build(
        RunCommandOptions opts,
        bool effectiveHeadless,
        string launcher,
        string? command = null,
        string? workingDirectory = null)
    {
        var cwd = NormalizePath(workingDirectory ?? Directory.GetCurrentDirectory());
        var repositories = new List<RunRepositoryMetadata>();
        var seenRoots = new HashSet<string>(StringComparer.Ordinal);

        AddRepository(repositories, seenRoots, "runner", cwd);
        foreach (var extraMod in opts.ExtraMods)
        {
            var path = Path.GetFullPath(extraMod, cwd);
            AddRepository(repositories, seenRoots, "extra-mod", path);
        }

        return new RunMetadata(
            Command: command ?? Environment.CommandLine,
            WorkingDirectory: cwd,
            LaunchMode: effectiveHeadless ? "headless" : "windowed",
            Headless: effectiveHeadless,
            Launcher: launcher,
            Repositories: repositories);
    }

    private static void AddRepository(
        List<RunRepositoryMetadata> repositories,
        HashSet<string> seenRoots,
        string labelPrefix,
        string path)
    {
        var root = FindGitRoot(path);
        if (root is null || !seenRoots.Add(root))
            return;

        var repoName = Path.GetFileName(root);
        if (string.IsNullOrWhiteSpace(repoName))
            repoName = "repo";

        repositories.Add(new RunRepositoryMetadata(
            Label: $"{labelPrefix}:{repoName}",
            Path: root,
            Commit: TryGit(root, "rev-parse", "--short=12", "HEAD"),
            Dirty: IsDirty(root)));
    }

    private static string? FindGitRoot(string path)
    {
        var current = Directory.Exists(path)
            ? NormalizePath(path)
            : Path.GetDirectoryName(NormalizePath(path));

        while (!string.IsNullOrEmpty(current))
        {
            var dotGit = Path.Combine(current, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
                return NormalizePath(current);

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static bool IsDirty(string root)
    {
        var status = TryGit(root, "status", "--porcelain");
        return !string.IsNullOrWhiteSpace(status);
    }

    private static string? TryGit(string root, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                return null;
            }
            if (process.ExitCode != 0)
                return null;

            return stdout.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
