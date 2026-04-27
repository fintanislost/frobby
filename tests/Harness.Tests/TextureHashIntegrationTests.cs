using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration surface for Tier 2 texture-hash fallback — verified manually (Step 2 of T5).</summary>
public class TextureHashIntegrationTests
{
    [Fact(Skip = "Requires live SDV — manifest build + Tier 2 resolution verified manually.")]
    public void Tier2HashResolution_ResolvesPortraitMissedByTier1() { }
}
