using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Reports;

/// <summary>Top-level run summary. Serialized as <c>summary.json</c> in the run directory.</summary>
public sealed record RunSummary(
    string RunId,
    string Started,        // ISO 8601 UTC
    int DurationMs,
    IReadOnlyList<ScenarioOutcome> Scenarios);

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
