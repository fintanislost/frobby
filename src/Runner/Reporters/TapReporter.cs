using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// TAP 13 reporter. Emits "TAP version 13", an "1..N" plan, and one
/// "ok|not ok" line per scenario. Failures get a YAML diagnostic block indented 2 spaces
/// with duration + failure list.
/// </summary>
/// <remarks>
/// TAP 13 spec: https://testanything.org/tap-version-13-specification.html. Near-universal
/// CI consumer support (GitHub Actions, GitLab, Jenkins). TAP 14's subtests aren't useful
/// here — each scenario is a flat test case, not a hierarchy.
/// </remarks>
public sealed class TapReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        output.WriteLine("TAP version 13");
        output.WriteLine($"1..{reports.Count}");

        for (int i = 0; i < reports.Count; i++)
        {
            var r = reports[i];
            var status = r.Passed ? "ok" : "not ok";
            output.WriteLine($"{status} {i + 1} - {r.Name}");

            if (!r.Passed)
            {
                // YAML diagnostic block per TAP 13. Two-space indent, "---" / "..." delimiters.
                output.WriteLine("  ---");
                output.WriteLine($"  duration_ms: {r.DurationMs}");
                if (r.Failures.Count > 0)
                {
                    output.WriteLine("  failures:");
                    foreach (var f in r.Failures)
                    {
                        // Quote the value to handle colons/special chars safely. TAP 13
                        // uses YAML 1.1, so escape embedded double-quotes and backslashes.
                        var escaped = f.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        output.WriteLine($"    - \"{escaped}\"");
                    }
                }
                output.WriteLine("  ...");
            }
        }
    }
}
