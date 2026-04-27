using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

/// <summary>Integration surface for M3 DSL — exercised via the Worked example + manual smoke.</summary>
public class DslIntegrationTests
{
    [Fact(Skip = "Requires live SDV — Worked/ShopMenuDslSmoke covers end-to-end DSL round-trip.")]
    public void DslSession_RoundTrip() { }
}
