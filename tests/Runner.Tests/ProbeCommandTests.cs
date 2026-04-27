using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>
/// Exercises the probe command against an in-proc Unix socket server so it doesn't require
/// a running SDV. Proves the runner-side RPC pipe works end-to-end.
/// </summary>
[Collection("Console")]
public class ProbeCommandTests
{
    [Fact]
    public async Task Probe_ReadyThenStatePlayer_ExitZero()
    {
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-test-{Guid.NewGuid():N}.sock");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Fake harness: on connect, send ready + answer state.player with a fixed payload.
        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
            {
                session.RequestReceived += async req =>
                {
                    if (req.Method == "state.player")
                    {
                        var result = JsonDocument.Parse("""{"name":"Tester","money":100}""").RootElement;
                        await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, result), tok);
                    }
                };

                var ready = JsonDocument.Parse("""{"version":"0.1.0","sdv":"1.6.15","smapi":"4.5.2"}""").RootElement;
                await session.SendNotificationAsync("ready", ready, tok);
                await session.RunAsync(tok);
            }, cts.Token);
        }, cts.Token);

        // Wait for listener.
        for (int i = 0; i < 40 && !File.Exists(socket); i++)
            await Task.Delay(50, cts.Token);

        // Capture stdout/stderr.
        var outW = new StringWriter();
        var errW = new StringWriter();
        var priorOut = Console.Out; var priorErr = Console.Error;
        Console.SetOut(outW); Console.SetError(errW);

        int exit;
        try
        {
            exit = await ProbeCommand.RunAsync(new ReadOnlyMemory<string>(new[] { socket }), cts.Token);
        }
        finally
        {
            Console.SetOut(priorOut); Console.SetError(priorErr);
        }

        var stdout = outW.ToString();
        Assert.Equal(0, exit);
        Assert.Contains("ready   version=0.1.0", stdout);
        Assert.Contains("state.player: ", stdout);
        Assert.Contains("\"name\":\"Tester\"", stdout);

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Probe_NoSocket_ReturnsTwo()
    {
        var priorErr = Console.Error;
        var err = new StringWriter();
        Console.SetError(err);
        try
        {
            var exit = await ProbeCommand.RunAsync(
                ReadOnlyMemory<string>.Empty,
                new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
            Assert.Equal(2, exit);
        }
        finally { Console.SetError(priorErr); }

        Assert.Contains("no socket path", err.ToString());
    }
}
