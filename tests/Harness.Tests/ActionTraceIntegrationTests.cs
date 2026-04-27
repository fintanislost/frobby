using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration surface for action-trace recording — verified manually.</summary>
public class ActionTraceIntegrationTests
{
    [Fact(Skip = "Requires interactive SDV — record a play session via harness_record_actions/_stop and verify the trace.")]
    public void RecordRealPlaySession_ProducesReplayableTrace() { }
}
