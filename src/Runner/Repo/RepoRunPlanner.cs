using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Repo;

public sealed record RepoRunRequest(
    bool Visible,
    bool NoBuild,
    bool DryRun,
    bool Baseline,
    string? ModSet,
    string? ReportDir,
    IReadOnlyList<string> Targets,
    string? Filter = null);

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

public static class RepoRunPlanner
{
    public static RepoRunPlan BuildRunPlan(
        string repoRoot,
        RepoTestConfig config,
        RepoRunRequest request,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("repo root is required.");
        }

        var fullRepoRoot = Path.GetFullPath(repoRoot);
        var profile = RepoProfileResolver.Resolve(
            fullRepoRoot,
            config,
            request.ModSet,
            environment,
            requireRepoExtraMods: request.NoBuild);
        var extraMods = profile.ExtraMods;
        var modsPath = Path.Combine(fullRepoRoot, ".cache", "frobby-test-mods", profile.CacheNamespace);
        var buildCommand = request.NoBuild
            ? null
            : new[] { RequireText(config.Build.Command, "build.command") }
                .Concat(config.Build.Args)
                .ToArray();
        var targets = ResolveTargets(fullRepoRoot, config, request, environment);
        var reportDir = ResolveReportDir(fullRepoRoot, config, request.ReportDir, environment);
        var usesSingleRun = request.Baseline || (targets.Count == 1 && IsScenarioFile(targets[0]));
        var frobbyArgs = new List<string>
        {
            usesSingleRun ? "run" : "run-suite",
        };

        if (!usesSingleRun)
        {
            frobbyArgs.Add("--fresh-process-per-scenario");
        }

        if (!request.Visible)
        {
            frobbyArgs.Add("--headless");
        }

        frobbyArgs.Add("--mods-path");
        frobbyArgs.Add(modsPath);
        frobbyArgs.Add("--profile-id");
        frobbyArgs.Add(profile.Id);
        frobbyArgs.Add("--profile-cache-namespace");
        frobbyArgs.Add(profile.CacheNamespace);

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            frobbyArgs.Add("--filter");
            frobbyArgs.Add(request.Filter!);
        }

        foreach (var extraMod in extraMods)
        {
            frobbyArgs.Add("--extra-mod");
            frobbyArgs.Add(extraMod);
        }

        foreach (var overlay in profile.ConfigOverlays)
        {
            frobbyArgs.Add("--config-overlay");
            frobbyArgs.Add(overlay.SourcePath);
            frobbyArgs.Add(overlay.TargetModUniqueId);
            frobbyArgs.Add(overlay.TargetRelativePath);
        }

        frobbyArgs.Add("--report-dir");
        frobbyArgs.Add(reportDir);

        if (request.Baseline)
        {
            frobbyArgs.Add("--update-baselines");
        }

        frobbyArgs.AddRange(targets);

        return new RepoRunPlan(
            fullRepoRoot,
            buildCommand,
            frobbyArgs,
            reportDir,
            extraMods,
            modsPath,
            profile.Id,
            profile.CacheNamespace,
            profile.ConfigOverlays);
    }

    private static IReadOnlyList<string> ResolveTargets(
        string repoRoot,
        RepoTestConfig config,
        RepoRunRequest request,
        IReadOnlyDictionary<string, string?>? environment)
    {
        if (request.Baseline && request.Targets.Count > 1)
        {
            throw new InvalidOperationException("baseline mode accepts at most one target.");
        }

        var rawTargets = request.Baseline
            ? request.Targets.Count == 1
                ? request.Targets
                : new[] { RequireText(config.BaselineTarget, "baselineTarget") }
            : request.Targets.Count > 0
                ? request.Targets
                : new[] { RequireText(config.DefaultTarget, "defaultTarget") };

        return rawTargets
            .Select(path => RepoPathResolver.Resolve(repoRoot, path, environment, requireExists: true))
            .ToArray();
    }

    private static string ResolveReportDir(
        string repoRoot,
        RepoTestConfig config,
        string? requestedReportDir,
        IReadOnlyDictionary<string, string?>? environment)
    {
        if (!string.IsNullOrWhiteSpace(requestedReportDir))
        {
            return RepoPathResolver.Resolve(repoRoot, requestedReportDir, environment, requireExists: false);
        }

        var slug = RequireText(config.Project.Slug, "project.slug");
        var version = RequireText(config.Project.Version, "project.version");
        return Path.Combine(Path.GetTempPath(), $"{slug}-frobby-results-{version}");
    }

    private static bool IsScenarioFile(string path)
        => path.EndsWith(".test.json", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"sdv-test config requires '{field}'.");
        }

        return value;
    }
}
