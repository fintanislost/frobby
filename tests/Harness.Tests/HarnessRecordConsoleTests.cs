using System.Collections.Generic;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class HarnessRecordConsoleTests
{
    private sealed class FakeFileSink : IFileSink
    {
        public List<(string Path, string Contents)> Writes { get; } = new();
        public void Write(string path, string contents) => Writes.Add((path, contents));
    }

    [Fact]
    public void ValidName_EmitsWellFormedJson()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();
        void Log(string msg) => messages.Add(msg);

        HarnessRecordConsole.BuildAndWrite(
            name: "my_state",
            snapshot: new HarnessSnapshot(
                Seed: 42,
                InSave: true,
                Season: "spring",
                DayOfMonth: 5,
                Year: 1,
                LocationName: "FarmHouse",
                Money: 500),
            outputDir: "/tmp/records-test",
            sink: sink,
            log: Log);

        Assert.Single(sink.Writes);
        var (path, contents) = sink.Writes[0];
        Assert.Equal("/tmp/records-test/my_state.test.json", path);
        Assert.Contains("\"name\": \"my_state\"", contents);  // WriteIndented produces "name": "value"
        Assert.Contains("state.time.season == 'spring'", contents);
        Assert.Contains("state.player.money == 500", contents);
    }

    [Fact]
    public void InvalidName_LogsErrorAndWritesNothing()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();

        HarnessRecordConsole.BuildAndWrite(
            name: "../bad",
            snapshot: new HarnessSnapshot(42, true, "spring", 1, 1, "Farm", 0),
            outputDir: "/tmp/records-test",
            sink: sink,
            log: messages.Add);

        Assert.Empty(sink.Writes);
        Assert.Contains(messages, m => m.Contains("name must match", System.StringComparison.OrdinalIgnoreCase));
    }
}
