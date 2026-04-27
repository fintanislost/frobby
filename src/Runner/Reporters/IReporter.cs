using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// Output adapter for scenario run results. One implementation per supported format
/// (console, TAP, JUnit). Called once per run with the full list of scenario reports.
/// </summary>
public interface IReporter
{
    /// <summary>Serialize the reports to <paramref name="output"/> in the reporter's format.</summary>
    void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output);
}
