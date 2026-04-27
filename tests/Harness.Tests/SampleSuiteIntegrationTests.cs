using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>D1.7 integration surface — exercised by <c>./scripts/run-samples.sh</c>. Documented
/// here so the behavior is visible at test-discovery time.</summary>
public class SampleSuiteIntegrationTests
{
    [Fact(Skip = "Requires live SDV + Content Patcher — sample-suite smoke (scripts/run-samples.sh) verifies this.")]
    public void SampleCpMod_Loads_UnderSmapi() { }

    [Fact(Skip = "Requires live SDV — sample-suite smoke runs all ten scenarios to completion.")]
    public void SampleSuite_AllTenScenariosPass() { }

    [Fact(Skip = "Requires live SDV at Beach — sample-suite smoke confirms parallax scroll doesn't advance while frozen (M0 residual).")]
    public void FreezeParallaxRegression_HashesMatch() { }
}
