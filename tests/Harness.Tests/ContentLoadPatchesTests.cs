using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ContentLoadPatchesTests
{
    [Fact(Skip = "Requires live SDV content pipeline + SMAPI AssetReady event. Covered by the D1.5 smoke test's Tier 1 resolution-rate check (T8).")]
    public void Apply_SubscribesAssetReady_LoadPopulatesRegistry() { /* integration */ }
}
