using System;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureReadmeTests
{
    [Fact]
    public void Generate_IncludesDescription_AndRegenerateWith()
    {
        var spec = new FixtureSpec
        {
            Name = "spring_day_5",
            Base = "m0spike_436515781",
            Description = "Spring day 5 with 500g",
        };
        var meta = FixtureMetadata.Generate(
            spec, "1.6.15", "4.5.2", new[] { "Pathoschild.ContentPatcher" },
            "Tester", "female", DateTime.UtcNow);

        var md = FixtureReadme.Generate(spec, meta);

        Assert.Contains("# spring_day_5", md);
        Assert.Contains("Spring day 5 with 500g", md);
        Assert.Contains("m0spike_436515781", md);
        Assert.Contains("## Regenerate", md);
        Assert.Contains("tests/fixtures/spring_day_5/spring_day_5.fixture.json", md);
        Assert.Contains("SDV 1.6.15", md);
        Assert.Contains("SMAPI 4.5.2", md);
        Assert.Contains("Pathoschild.ContentPatcher", md);
    }

    [Fact]
    public void Generate_NullBase_OmitsBaseSection()
    {
        var spec = new FixtureSpec { Name = "root", Base = null, Description = "root fixture" };
        var meta = FixtureMetadata.Generate(
            spec, "1.6.15", "4.5.2", System.Array.Empty<string>(), "Tester", "female", DateTime.UtcNow);
        var md = FixtureReadme.Generate(spec, meta);
        Assert.DoesNotContain("Built from:", md);
    }
}
