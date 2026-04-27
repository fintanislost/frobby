using System.Collections.Generic;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Scenarios;

/// <summary>
/// Result summary emitted by <see cref="ScenarioRunner"/>. Aggregates pass/fail across
/// a scenario's assertions plus any step-level failures that aborted execution early.
/// </summary>
public sealed class ScenarioReport
{
    /// <summary>Scenario name, copied from the <c>ScenarioSpec</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute or repo-relative path to the scenario file that produced this report.
    /// Populated by <c>RunCommand</c> after <c>ScenarioRunner.RunAsync</c> returns. Consumed by
    /// reporters (JUnit uses it as <c>classname</c>; console appends it after the scenario name).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Overall pass flag — true iff no assertions failed and no step errored.</summary>
    public bool Passed { get; set; }

    /// <summary>Wall-clock duration of the run, rounded to milliseconds.</summary>
    public int DurationMs { get; set; }

    /// <summary>Total number of assertions evaluated (may be less than spec count if a step aborted).</summary>
    public int AssertionsRun { get; set; }

    /// <summary>Subset of <see cref="AssertionsRun"/> that passed.</summary>
    public int AssertionsPassed { get; set; }

    /// <summary>Per-assertion outcomes with labels and details suitable for HTML reports.</summary>
    public List<AssertionOutcome> Assertions { get; set; } = new();

    /// <summary>Human-readable failure messages — one per failed assertion or aborted step.</summary>
    public List<string> Failures { get; set; } = new();

    /// <summary>Per-step outcomes, in execution order. Populated when a <c>RunDirectory</c> is
    /// provided to <see cref="ScenarioRunner"/>.</summary>
    public List<StepOutcome> Steps { get; set; } = new();

    /// <summary>Run-dir-relative paths to screenshots captured during the run (after
    /// <c>freeze.begin</c> and on assertion failures). Populated when a <c>RunDirectory</c> is
    /// provided to <see cref="ScenarioRunner"/>.</summary>
    public List<string> Screenshots { get; set; } = new();

    /// <summary>Forensics PNG paths produced by failed bitmap assertions, indexed by assertion order.
    /// Populated when a <c>RunDirectory</c> is provided to <see cref="ScenarioRunner"/>.</summary>
    public List<DiffSet> Diffs { get; set; } = new();
}
