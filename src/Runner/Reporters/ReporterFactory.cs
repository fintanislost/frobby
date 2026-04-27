using System;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>Factory mapping reporter-name strings (CLI input) to <see cref="IReporter"/> instances.</summary>
public static class ReporterFactory
{
    /// <summary>
    /// Create a reporter for the given name. Names are case-insensitive: "console", "tap", "junit".
    /// Throws <see cref="ArgumentException"/> for unknown names so RunCommand can surface
    /// a usage error with exit code 2.
    /// </summary>
    public static IReporter Create(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "console" => new ConsoleReporter(),
            "tap" => new TapReporter(),
            "junit" => new JunitReporter(),
            _ => throw new ArgumentException(
                $"unknown reporter: {name} (known: console, tap, junit)", nameof(name)),
        };
    }
}
