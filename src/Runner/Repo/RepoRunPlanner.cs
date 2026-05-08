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
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("repo root is required.");
        }

        var fullRepoRoot = Path.GetFullPath(repoRoot);
        var modSet = SelectModSet(config, request.ModSet);
        var dependencyMods = modSet.Deps
            .Select(dependency => RepoDependencyCache.ResolveRequired(dependency, environment))
            .ToArray();
        var repoExtraMods = modSet.ExtraMods
            .Select(path => RepoPathResolver.Resolve(fullRepoRoot, path, environment, requireExists: true))
            .ToArray();
        var extraMods = dependencyMods.Concat(repoExtraMods).ToArray();
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

        foreach (var extraMod in extraMods)
        {
            frobbyArgs.Add("--extra-mod");
            frobbyArgs.Add(extraMod);
        }

        frobbyArgs.Add("--report-dir");
        frobbyArgs.Add(reportDir);

        if (request.Baseline)
        {
            frobbyArgs.Add("--update-baselines");
        }

        frobbyArgs.AddRange(targets);

        return new RepoRunPlan(fullRepoRoot, buildCommand, frobbyArgs, reportDir, extraMods);
    }

    private static RepoModSetConfig SelectModSet(RepoTestConfig config, string? requestedName)
    {
        if (config.ModSets.Count == 0)
        {
            throw new InvalidOperationException("sdv-test config must define at least one mod set.");
        }

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return config.ModSets[0];
        }

        return config.ModSets.FirstOrDefault(modSet => modSet.Name == requestedName)
            ?? throw new InvalidOperationException($"Unknown mod set '{requestedName}'.");
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
