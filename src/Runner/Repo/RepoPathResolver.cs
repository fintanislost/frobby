using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SdvTestFramework.Runner.Repo;

public static class RepoPathResolver
{
    public static string Resolve(
        string repoRoot,
        string rawPath,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool requireExists = true)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("repoRoot is required when resolving repo paths.");
        }

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidOperationException("Path is required when resolving repo paths.");
        }

        var expanded = ExpandHome(rawPath, environment);
        expanded = ExpandEnvironmentVariables(expanded, rawPath, environment);

        var resolved = Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(repoRoot, expanded));

        if (requireExists && !File.Exists(resolved) && !Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException(
                $"Resolved path '{rawPath}' to '{resolved}', but no file or directory exists there.");
        }

        return resolved;
    }

    private static string ExpandHome(string rawPath, IReadOnlyDictionary<string, string?>? environment)
    {
        if (rawPath == "~")
        {
            return GetRequiredHomePath(rawPath, environment);
        }

        if (rawPath.StartsWith("~/", StringComparison.Ordinal) || rawPath.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return Path.Combine(GetRequiredHomePath(rawPath, environment), rawPath[2..]);
        }

        return rawPath;
    }

    private static string ExpandEnvironmentVariables(
        string path,
        string originalPath,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var result = new StringBuilder(path.Length);

        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] != '$')
            {
                result.Append(path[i]);
                continue;
            }

            if (i + 1 >= path.Length)
            {
                result.Append('$');
                continue;
            }

            if (path[i + 1] == '{')
            {
                var end = path.IndexOf('}', i + 2);
                if (end < 0)
                {
                    throw new InvalidOperationException(
                        $"Path '{originalPath}' contains an unterminated environment variable reference.");
                }

                var name = path[(i + 2)..end];
                if (name.Length == 0)
                {
                    throw new InvalidOperationException($"Path '{originalPath}' contains an empty environment variable reference.");
                }

                result.Append(GetRequiredEnvironmentValue(name, originalPath, environment));
                i = end;
                continue;
            }

            if (!IsVariableStart(path[i + 1]))
            {
                result.Append('$');
                continue;
            }

            var start = i + 1;
            var cursor = start + 1;
            while (cursor < path.Length && IsVariablePart(path[cursor]))
            {
                cursor++;
            }

            var variableName = path[start..cursor];
            result.Append(GetRequiredEnvironmentValue(variableName, originalPath, environment));
            i = cursor - 1;
        }

        return result.ToString();
    }

    private static string GetRequiredHomePath(string rawPath, IReadOnlyDictionary<string, string?>? environment)
    {
        if (environment is not null)
        {
            if (environment.TryGetValue("HOME", out var suppliedHome) && !string.IsNullOrWhiteSpace(suppliedHome))
            {
                return suppliedHome;
            }

            if (environment.TryGetValue("USERPROFILE", out var suppliedProfile) && !string.IsNullOrWhiteSpace(suppliedProfile))
            {
                return suppliedProfile;
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException(
                $"Path '{rawPath}' requires a home directory, but HOME, USERPROFILE, and the OS user profile path were unavailable.");
        }

        return userProfile;
    }

    private static string GetRequiredEnvironmentValue(
        string name,
        string rawPath,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var value = environment is not null
            ? environment.TryGetValue(name, out var suppliedValue) ? suppliedValue : null
            : Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Path '{rawPath}' requires environment variable '{name}', but it was not set.");
        }

        return value;
    }

    private static bool IsVariableStart(char value)
        => value == '_' || char.IsAsciiLetter(value);

    private static bool IsVariablePart(char value)
        => value == '_' || char.IsAsciiLetterOrDigit(value);
}
