using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>Integration surface for M2 watch mode — exercised via T5's live smoke.</summary>
public class WatchModeIntegrationTests
{
    [Fact(Skip = "Requires live SDV — watch-mode smoke (T5) verifies file-change triggers rerun within 500ms without relaunching SDV.")]
    public void WatchMode_FileChange_TriggersRerun() { }
}
