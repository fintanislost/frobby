using System.IO;

namespace SdvTestFramework.Runner.Mcp;

internal sealed class McpReportRegistry
{
    private readonly object _sync = new();
    private McpReportSnapshot? _latest;

    internal void RecordLatestReport(string reportDir, string? summaryJson = null)
    {
        lock (_sync)
        {
            _latest = new McpReportSnapshot(Path.GetFullPath(reportDir), summaryJson);
        }
    }

    internal bool TryGetLatestReport(out McpReportSnapshot report)
    {
        lock (_sync)
        {
            if (_latest is null)
            {
                report = default!;
                return false;
            }

            report = _latest;
            return true;
        }
    }
}

internal sealed record McpReportSnapshot(string ReportDir, string? SummaryJson);
