using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Watch;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class ScenarioWatcherTests
{
    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"watcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Triggers_AfterDebounce()
    {
        var dir = MakeTempDir();
        try
        {
            int triggerCount = 0;
            using var watcher = new ScenarioWatcher(
                new[] { dir },
                () => Interlocked.Increment(ref triggerCount),
                debounce: TimeSpan.FromMilliseconds(50));

            File.WriteAllText(Path.Combine(dir, "x.test.json"), "{}");

            await Task.Delay(300);
            Assert.Equal(1, triggerCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CoalescesBurst_OneCallback()
    {
        var dir = MakeTempDir();
        try
        {
            int triggerCount = 0;
            using var watcher = new ScenarioWatcher(
                new[] { dir },
                () => Interlocked.Increment(ref triggerCount),
                debounce: TimeSpan.FromMilliseconds(80));

            for (int i = 0; i < 5; i++)
            {
                File.WriteAllText(Path.Combine(dir, $"burst_{i}.test.json"), "{}");
                await Task.Delay(10);
            }

            await Task.Delay(300);
            Assert.Equal(1, triggerCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Dispose_StopsWatcher()
    {
        var dir = MakeTempDir();
        try
        {
            int triggerCount = 0;
            var watcher = new ScenarioWatcher(
                new[] { dir },
                () => Interlocked.Increment(ref triggerCount),
                debounce: TimeSpan.FromMilliseconds(50));

            watcher.Dispose();

            File.WriteAllText(Path.Combine(dir, "after_dispose.test.json"), "{}");
            await Task.Delay(200);
            Assert.Equal(0, triggerCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TriggerForTests_FiresCallbackImmediately()
    {
        int triggerCount = 0;
        using var watcher = new ScenarioWatcher(
            Array.Empty<string>(),
            () => Interlocked.Increment(ref triggerCount),
            debounce: TimeSpan.FromMilliseconds(50));

        watcher.TriggerForTests();
        Assert.Equal(1, triggerCount);
    }
}
