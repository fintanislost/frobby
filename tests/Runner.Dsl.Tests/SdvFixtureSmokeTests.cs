using System;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class SdvFixtureSmokeTests
{
    [Fact]
    public async Task InitializeAsync_WithDslSkipSet_NoOps()
    {
        // Verifies the DSL_SKIP_SDV_LAUNCH bypass — needed for CI to run without live SDV.
        var original = Environment.GetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH");
        Environment.SetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH", "1");
        try
        {
            SdvTestSession.ResetForTests();
            var fx = new SdvFixture();
            await fx.InitializeAsync();
            Assert.Null(SdvTestSession.Current);   // skip path → nothing initialized
            await fx.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DSL_SKIP_SDV_LAUNCH", original);
        }
    }
}
