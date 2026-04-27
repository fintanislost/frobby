using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Watch;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class WatchLoopTests
{
    [Fact]
    public async Task RunAsync_CallsRerunOnTrigger_PrintsBanners()
    {
        var output = new StringWriter();
        int rerunCount = 0;
        var cts = new CancellationTokenSource();
        ScenarioWatcher? captured = null;

        Func<Action, ScenarioWatcher> factory = onTriggered =>
        {
            captured = new ScenarioWatcher(
                Array.Empty<string>(),
                onTriggered,
                debounce: TimeSpan.FromMilliseconds(10));
            return captured;
        };

        var loopTask = WatchLoop.RunAsyncForTests(
            paths: new[] { "/fake/path" },
            rerun: async _ => { Interlocked.Increment(ref rerunCount); await Task.Yield(); },
            output: output,
            watcherFactory: factory,
            ct: cts.Token);

        // Give the loop a tick to install the watcher + print initial banner.
        await Task.Delay(50);
        Assert.NotNull(captured);
        Assert.Contains("[watch] waiting for changes", output.ToString());

        // Simulate a file change via the synthetic trigger.
        captured!.TriggerForTests();
        await Task.Delay(100);
        Assert.Equal(1, rerunCount);
        Assert.Contains("[watch] file(s) changed — rerunning", output.ToString());

        // Cancel to shut down cleanly.
        cts.Cancel();
        await loopTask;
    }
}
