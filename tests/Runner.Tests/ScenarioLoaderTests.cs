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

    [Fact]
    public void Load_ExpandsStepIncludesRelativeToScenarioFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scenario-includes-{System.Guid.NewGuid():N}");
        var fragments = Path.Combine(root, "fragments");
        Directory.CreateDirectory(fragments);

        File.WriteAllText(Path.Combine(fragments, "open.steps.json"), """
[
  { "action": "player.warp", "args": { "location": "Farm", "x": 1, "y": 2 } },
  { "action": "wait.ms", "args": { "ms": 100 } }
]
""");

        var path = Path.Combine(root, "with-include.test.json");
        File.WriteAllText(path, """
{
  "name": "with_include",
  "steps": [
    { "include": "fragments/open.steps.json" },
    { "action": "draw.arm", "args": { "ticks": 10 } }
  ]
}
""");

        var spec = ScenarioLoader.Load(path);

        Assert.Equal(3, spec.Steps.Count);
        Assert.Equal("player.warp", spec.Steps[0].Action);
        Assert.Equal("wait.ms", spec.Steps[1].Action);
        Assert.Equal("draw.arm", spec.Steps[2].Action);
    }

    [Fact]
    public void Load_NestedStepIncludesResolveRelativeToIncludingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scenario-includes-{System.Guid.NewGuid():N}");
        var fragments = Path.Combine(root, "fragments");
        Directory.CreateDirectory(fragments);

        File.WriteAllText(Path.Combine(fragments, "leaf.steps.json"), """
[
  { "action": "world.interact_tile", "args": { "x": 8, "y": 9 } }
]
""");
        File.WriteAllText(Path.Combine(fragments, "outer.steps.json"), """
[
  { "include": "leaf.steps.json" },
  { "action": "wait.ms", "args": { "ms": 500 } }
]
""");

        var path = Path.Combine(root, "nested-include.test.json");
        File.WriteAllText(path, """
{
  "name": "nested_include",
  "steps": [
    { "include": "fragments/outer.steps.json" }
  ]
}
""");

        var spec = ScenarioLoader.Load(path);

        Assert.Equal(2, spec.Steps.Count);
        Assert.Equal("world.interact_tile", spec.Steps[0].Action);
        Assert.Equal("wait.ms", spec.Steps[1].Action);
    }

    [Fact]
    public void Load_IncludeCycle_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scenario-includes-{System.Guid.NewGuid():N}");
        var fragments = Path.Combine(root, "fragments");
        Directory.CreateDirectory(fragments);

        File.WriteAllText(Path.Combine(fragments, "a.steps.json"), """[{ "include": "b.steps.json" }]""");
        File.WriteAllText(Path.Combine(fragments, "b.steps.json"), """[{ "include": "a.steps.json" }]""");

        var path = Path.Combine(root, "cycle.test.json");
        File.WriteAllText(path, """
{
  "name": "cycle",
  "steps": [
    { "include": "fragments/a.steps.json" }
  ]
}
""");

        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Load(path));
        Assert.Contains("include cycle", ex.Message);
    }
}
