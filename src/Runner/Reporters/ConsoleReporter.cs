using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// Default Playwright-style reporter — one line per scenario, indented failures, and a
/// trailing summary line. Byte-for-byte compatible with the pre-M2-reporters output of
/// <c>RunCommand</c>.
/// </summary>
public sealed class ConsoleReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        int failed = 0;
        foreach (var report in reports)
        {
            var status = report.Passed ? "PASS" : "FAIL";
            output.WriteLine($"  {status} {report.Name} ({report.DurationMs}ms) — {report.Path}");
            foreach (var f in report.Failures)
                output.WriteLine($"        {f}");
            if (!report.Passed) failed++;
        }
        output.WriteLine();
        output.WriteLine($"[run] {reports.Count - failed}/{reports.Count} passed");
    }
}
