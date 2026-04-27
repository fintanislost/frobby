using System.IO;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Recording;
using SdvTestFramework.Protocol.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class RpcTraceRecorderTests
{
    private static JsonRpcRequest Req(string method, string paramsJson = "{}")
    {
        var p = JsonDocument.Parse(paramsJson).RootElement;
        return new JsonRpcRequest { Id = 1, Method = method, Params = p };
    }

    [Fact]
    public void RecordsMutator_ButNotReads()
    {
        var rec = new RpcTraceRecorder();
        rec.OnRequest(Req("player.warp", "{\"location\":\"Farm\",\"x\":64,\"y\":15}"));
        rec.OnRequest(Req("state.player"));  // skipped
        rec.OnRequest(Req("time.advance", "{\"minutes\":120}"));

        Assert.Equal(2, rec.Count);
        var steps = rec.Steps.ToList();
        Assert.Equal("player.warp", steps[0].Method);
        Assert.Equal("time.advance", steps[1].Method);
    }

    [Fact]
    public void SkipsScenarioLifecycle()
    {
        var rec = new RpcTraceRecorder();
        rec.OnRequest(Req("scenario.begin", "{\"name\":\"x\",\"seed\":42}"));
        rec.OnRequest(Req("fixture.load", "{\"name\":\"m0spike_436515781\"}"));
        rec.OnRequest(Req("scenario.end"));

        Assert.Equal(1, rec.Count);
        Assert.Equal("fixture.load", rec.Steps.First().Method);
    }

    [Fact]
    public void EmitsValidScenarioJson()
    {
        var rec = new RpcTraceRecorder();
        rec.OnRequest(Req("player.set_money", "{\"amount\":500}"));
        rec.OnRequest(Req("time.advance", "{\"minutes\":60}"));

        var path = Path.Combine(Path.GetTempPath(), $"rec-{System.Guid.NewGuid():N}.test.json");
        try
        {
            rec.WriteToFile(path, name: "test_trace", seed: 42);
            // ScenarioLoader.Load validates against schemas/scenario.schema.json.
            var spec = ScenarioLoader.Load(path);
            Assert.Equal("test_trace", spec.Name);
            Assert.Equal(2, spec.Steps.Count);
            Assert.Equal("player.set_money", spec.Steps[0].Action);
            Assert.Equal("time.advance", spec.Steps[1].Action);
        }
        finally { File.Delete(path); }
    }
}
