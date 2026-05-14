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
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("repo root is required.");
        }

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            if (config.ModSets.Count > 0)
            {
                return ResolveModSet(repoRoot, config.ModSets[0], environment, requireRepoExtraMods);
            }

            if (config.Profiles.Count == 0)
            {
                throw new InvalidOperationException("sdv-test config must define at least one mod set or profile.");
            }

            var defaultProfileName = config.Profiles.Keys.OrderBy(value => value, StringComparer.Ordinal).First();
            return ResolveProfile(repoRoot, config, defaultProfileName, environment, requireRepoExtraMods, new Stack<string>());
        }

        var name = requestedName!;

        if (config.Profiles.ContainsKey(name))
        {
            return ResolveProfile(repoRoot, config, name, environment, requireRepoExtraMods, new Stack<string>());
        }

        var modSet = config.ModSets.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new InvalidOperationException($"Unknown profile '{name}'.");
        return ResolveModSet(repoRoot, modSet, environment, requireRepoExtraMods);
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
            var cycle = stack.Reverse().Concat(new[] { name });
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
            var source = RepoPathResolver.Resolve(repoRoot, overlay.Source!, environment, requireExists: false);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"overlay source not found: {overlay.Source}", source);
            }

            yield return new ExtraModConfigOverlay(source, overlay.TargetMod!, overlay.TargetPath!);
        }
    }

    private static string RequireName(string? name, string label)
        => !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new InvalidOperationException($"repo {label} name is required.");

    private static string SanitizeCacheNamespace(string value)
    {
        var original = value;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        var sanitized = value.Trim();
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
        {
            throw new InvalidOperationException($"profile cache namespace '{original}' is not valid.");
        }

        return sanitized;
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
