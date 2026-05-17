namespace SdvTestFramework.Runner.Mcp;

public sealed class ToolInvocationContext
{
    public ToolInvocationContext(SdvLifecycle? lifecycle, McpProgressReporter progress)
    {
        Lifecycle = lifecycle;
        Progress = progress;
    }

    public SdvLifecycle? Lifecycle { get; }
    public McpProgressReporter Progress { get; }
}
