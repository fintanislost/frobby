using System.IO;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureLoaderTests
{
    private static string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fixture-{System.Guid.NewGuid():N}.fixture.json");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Load_ValidScript_RoundTrips()
    {
        var path = WriteTemp("""
        {
          "name": "test",
          "base": "m0spike_436515781",
          "description": "derived test fixture",
          "steps": [
            { "action": "player.set_money", "args": { "amount": 500 } }
          ]
        }
        """);
        try
        {
            var spec = FixtureLoader.Load(path);
            Assert.Equal("test", spec.Name);
            Assert.Equal("m0spike_436515781", spec.Base);
            Assert.Equal("derived test fixture", spec.Description);
            Assert.Single(spec.Steps);
            Assert.Equal("player.set_money", spec.Steps[0].Action);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingName_Throws()
    {
        var path = WriteTemp("""{"base":"x","description":"y"}""");
        try
        {
            var ex = Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path));
            Assert.Contains("schema validation failed", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingDescription_Throws()
    {
        var path = WriteTemp("""{"name":"x","base":"y"}""");
        try { Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_NullBase_Accepted()
    {
        // Root fixtures (e.g. migrated spike save) have base: null.
        var path = WriteTemp("""{"name":"root","base":null,"description":"root fixture"}""");
        try
        {
            var spec = FixtureLoader.Load(path);
            Assert.Null(spec.Base);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_FileMissing_Throws()
    {
        var ex = Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load("/tmp/does-not-exist.fixture.json"));
        Assert.Contains("file not found", ex.Message);
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        var path = WriteTemp("{ not json");
        try
        {
            var ex = Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path));
            Assert.Contains("invalid JSON", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ExtraFields_Rejected()
    {
        // Schema has additionalProperties: false — tight to catch typos.
        var path = WriteTemp("""{"name":"x","description":"y","extra":"bad"}""");
        try { Assert.Throws<FixtureLoadException>(() => FixtureLoader.Load(path)); }
        finally { File.Delete(path); }
    }
}
