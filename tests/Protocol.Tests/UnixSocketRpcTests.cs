using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

/// <summary>
/// Real-socket integration for <see cref="UnixSocketRpc"/>. Uses a tmp path that gets
/// unique-per-test. Only runs on non-Windows (our M1 platform support).
/// </summary>
public class UnixSocketRpcTests
{
    [Fact]
    public async Task ServerSendsReady_ClientReceives()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"sdv-test-{Guid.NewGuid():N}.sock");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            await UnixSocketRpc.RunServerAsync(path, async (session, token) =>
            {
                await session.SendNotificationAsync("ready",
                    JsonDocument.Parse("""{"version":"0.1.0","sdv":"1.6.15"}""").RootElement,
                    token);
                await session.RunAsync(token);
            }, cts.Token);
        }, cts.Token);

        // Give the listener a moment to come up — up to 1 second.
        for (int i = 0; i < 20 && !File.Exists(path); i++)
            await Task.Delay(50, cts.Token);

        using var client = await UnixSocketRpc.ConnectAsync(path, cts.Token);
        var readyReceived = new TaskCompletionSource<JsonRpcNotification>();
        client.NotificationReceived += n => readyReceived.TrySetResult(n);

        var runTask = client.RunAsync(cts.Token);

        var ready = await readyReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("ready", ready.Method);
        Assert.Equal("0.1.0", ready.Params!.Value.GetProperty("version").GetString());

        cts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }
    }
}
