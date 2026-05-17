namespace SdvTestFramework.Runner.Mcp;

public sealed class ToolInvocationContext
{
    public ToolInvocationContext(SdvLifecycle? lifecycle, McpProgressReporter progress)
        : this(lifecycle, progress, new McpReportRegistry())
    {
    }

    internal ToolInvocationContext(
        SdvLifecycle? lifecycle,
        McpProgressReporter progress,
        McpReportRegistry reports)
    {
        Lifecycle = lifecycle;
        Progress = progress;
        Reports = reports;
    }

    public SdvLifecycle? Lifecycle { get; }
    public McpProgressReporter Progress { get; }
    internal McpReportRegistry Reports { get; }
}
