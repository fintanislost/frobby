using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Reports;

/// <summary>Top-level run summary. Serialized as <c>summary.json</c> in the run directory.</summary>
public sealed record RunSummary(
    string RunId,
    string Started,        // ISO 8601 UTC
    int DurationMs,
    IReadOnlyList<ScenarioOutcome> Scenarios)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunMetadata? Metadata { get; init; }
}

/// <summary>Environment and revision metadata for a run.</summary>
public sealed record RunMetadata(
    string Command,
    string WorkingDirectory,
    string LaunchMode,
    bool Headless,
    string Launcher,
    IReadOnlyList<RunRepositoryMetadata> Repositories);

/// <summary>Git revision metadata for a repository that influenced the run.</summary>
public sealed record RunRepositoryMetadata(
    string Label,
    string Path,
    string? Commit,
    bool Dirty);

/// <summary>One scenario's outcome.</summary>
public sealed record ScenarioOutcome(
    string Name,
    string? Path,
    bool Passed,
    int DurationMs,
    IReadOnlyList<StepOutcome> Steps,
    IReadOnlyList<AssertionOutcome> Assertions,
    IReadOnlyList<string> Screenshots,
    IReadOnlyList<DiffSet> Diffs);

/// <summary>One scenario step.</summary>
public sealed record StepOutcome(string Action, bool Passed, int DurationMs, string? Detail);

/// <summary>One scenario assertion.</summary>
public sealed record AssertionOutcome(string Type, bool Passed, string? Detail);
