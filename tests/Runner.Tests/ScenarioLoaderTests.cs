using System.IO;
using SdvTestFramework.Protocol.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioLoaderTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scenario-{System.Guid.NewGuid():N}.test.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_Valid_ReturnsSpec()
    {
        var path = WriteTemp("""
{ "name":"smoke","steps":[{"action":"player.warp","args":{"location":"Farm","x":1,"y":1}}] }
""");
        var spec = ScenarioLoader.Load(path);
        Assert.Equal("smoke", spec.Name);
        Assert.Single(spec.Steps);
        Assert.Equal("player.warp", spec.Steps[0].Action);
    }

    [Fact]
    public void Load_MissingRequired_Throws()
    {
        var path = WriteTemp("{ \"steps\":[] }");
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        var path = WriteTemp("{ not json");
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));
        Assert.Contains("invalid JSON", ex.Message);
    }

    [Fact]
    public void Load_UnknownFile_Throws()
    {
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load("/tmp/nope-" + System.Guid.NewGuid()));
        Assert.Contains("file not found", ex.Message);
    }

    [Fact]
    public void Load_WithConfigAndAssertions_RoundTripsAll()
    {
        var path = WriteTemp("""
{
  "name": "full",
  "fixture": "spring_day_5",
  "mods": ["Foo"],
  "config": { "seed": 99, "zoom": 1.5, "resolution": [1280, 720] },
  "steps": [{ "action": "player.set_money", "args": { "amount": 1000 } }],
  "assertions": [
    { "type": "state", "expr": "state.player.money == 1000" },
    { "type": "draw.contains", "filter": { "texture_asset": "Mods/Foo" }, "min_count": 1, "message": "custom" }
  ]
}
""");
        var spec = ScenarioLoader.Load(path);
        Assert.Equal("full", spec.Name);
        Assert.Equal("spring_day_5", spec.Fixture);
        Assert.Single(spec.Mods);
        Assert.Equal(99, spec.Config.Seed);
        Assert.Equal(1.5, spec.Config.Zoom);
        Assert.Single(spec.Steps);
        Assert.Equal(2, spec.Assertions.Count);
        Assert.Equal("state.player.money == 1000", spec.Assertions[0].Expr);
    }

    [Fact]
    public void Load_ExtraTopLevelField_Throws()
    {
        var path = WriteTemp("""{"name":"x","steps":[],"surprise":true}""");
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));
        Assert.Contains("schema validation", ex.Message);
    }
}
