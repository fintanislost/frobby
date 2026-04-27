using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>Integration surface for M2 bitmap fallback — exercised via T7's live smoke.</summary>
public class BitmapFallbackIntegrationTests
{
    [Fact(Skip = "Requires live SDV — bitmap-fallback smoke (T7) verifies capture + baseline + diff.")]
    public void BitmapAssertion_LiveSession_ProducesAndMatchesBaseline() { }
}
