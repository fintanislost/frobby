using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration surface for the M2 fixture builder — exercised via T11's smoke run.</summary>
public class FixtureBuilderIntegrationTests
{
    [Fact(Skip = "Requires live SDV + Content Patcher — fixture-builder smoke (T11) verifies this.")]
    public void FixtureCreate_EndToEnd_ProducesValidFixtureDirectory() { }

    [Fact(Skip = "Requires live SDV — smoke confirms derived fixtures load in scenarios.")]
    public void DerivedFixture_LoadsInScenario_RunsToCompletion() { }

    [Fact(Skip = "Requires live SDV — smoke confirms fixture list enumerates m0spike + any newly-built fixtures.")]
    public void FixtureList_EnumeratesCommittedFixtures() { }
}
