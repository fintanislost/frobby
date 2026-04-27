using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureBuilderTests
{
    private static string SocketPath() => Path.Combine(Path.GetTempPath(), $"fxb-{System.Guid.NewGuid():N}.sock");

    [Fact]
    public async Task BuildAsync_InvokesFixtureLoadThenSteps_ThenFixtureSave()
    {
        // Minimal fake harness that records every incoming RPC, responds 200-OK to each.
        var socket = SocketPath();
        var log = new System.Collections.Generic.List<string>();
        var cts = new System.Threading.CancellationTokenSource();
        var serverTask = RunFakeServer(socket, log, cts.Token);
        await WaitForSocket(socket);

        using var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
        _ = client.RunAsync(cts.Token);

        var spec = new FixtureSpec
        {
            Name = "derived_test",
            Base = "m0spike_436515781",
            Description = "test fixture",
            Steps = new[]
            {
                new FixtureStep { Action = "player.set_money", Args = JsonDocument.Parse("{\"amount\":500}").RootElement },
            },
        };

        var result = await FixtureBuilder.BuildAsync(spec, client, cts.Token);

        Assert.True(result.Success);
        Assert.Equal("1.6.15", result.SdvVersion);
        Assert.Equal("4.5.2", result.SmapiVersion);
        Assert.Contains("fixture.load", log);
        Assert.Contains("player.set_money", log);
        Assert.Contains("fixture.save", log);
        // Order: fixture.load first, fixture.save last
        Assert.Equal(0, log.IndexOf("fixture.load"));
        Assert.Equal(log.Count - 1, log.LastIndexOf("fixture.save"));

        cts.Cancel();
        try { await serverTask; } catch { /* cancellation */ }
    }

    // Runs a tiny JSON-RPC server that canned-answers every method in the builder's flow.
    private static Task RunFakeServer(string socket, System.Collections.Generic.List<string> log, System.Threading.CancellationToken ct)
    {
        return UnixSocketRpc.RunServerAsync(socket, async (session, sessCt) =>
        {
            session.RequestReceived += req =>
            {
                log.Add(req.Method);
                JsonElement result = req.Method switch
                {
                    "fixture.load" => JsonDocument.Parse("{\"ok\":true,\"tick\":1}").RootElement,
                    "state.player" => JsonDocument.Parse(
                        "{\"name\":\"Tester\",\"gender\":\"female\",\"money\":0,\"stamina\":0,\"max_stamina\":0,\"health\":0,\"location\":\"Farm\",\"tile\":{\"x\":0,\"y\":0}}").RootElement,
                    "state.time" => JsonDocument.Parse(
                        "{\"in_save\":true,\"season\":\"spring\",\"day_of_month\":1,\"year\":1,\"time_of_day\":600,\"day_of_week\":\"monday\"}").RootElement,
                    "state.mods" => JsonDocument.Parse("{\"mods\":[\"A.B\",\"C.D\"]}").RootElement,
                    "fixture.save" => JsonDocument.Parse("{\"ok\":true,\"tick\":10,\"save_path\":\"/tmp/fake\"}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true,\"tick\":2}").RootElement,
                };
                _ = session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, result), sessCt);
            };
            await session.RunAsync(sessCt);
        }, ct);
    }

    private static async Task WaitForSocket(string path)
    {
        for (int i = 0; i < 50; i++)
        {
            if (File.Exists(path)) return;
            await Task.Delay(50);
        }
    }
}
