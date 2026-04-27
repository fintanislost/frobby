using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>Integration surface for M2 record mode — exercised via T5's live smoke.</summary>
public class RecordModeIntegrationTests
{
    [Fact(Skip = "Requires live SDV + external RPC probe — record-mode smoke (T5) verifies end-to-end capture + replay.")]
    public void RecordMode_LiveSession_EmitsReplayableScenario() { }
}
