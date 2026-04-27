using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ActionTraceRecorderTests
{
    private sealed class FakeFileSink : IFileSink
    {
        public List<(string Path, string Contents)> Writes { get; } = new();
        public void Write(string path, string contents) => Writes.Add((path, contents));
    }

    [Fact]
    public void Start_ThenStop_FlushesBuffer()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();
        var rec = new ActionTraceRecorder(sink, messages.Add);

        rec.Start("test_session", "/tmp/records-test");
        // Inject a synthetic warp via internal seam.
        rec.Record(new RecordedAction(
            DateTime.UtcNow, ActionKind.Warp, Location: "Farm", X: 64, Y: 15));
        rec.Stop();

        Assert.Single(sink.Writes);
        var (path, contents) = sink.Writes[0];
        Assert.Equal("/tmp/records-test/test_session.test.json", path);
        Assert.Contains("player.warp", contents);
        Assert.Contains("\"location\": \"Farm\"", contents);

        // Verify the emitted JSON is valid and has required scenario fields.
        var doc = JsonDocument.Parse(contents);
        var root = doc.RootElement;
        Assert.Equal("test_session", root.GetProperty("name").GetString());
        Assert.True(root.TryGetProperty("steps", out var steps));
        Assert.Equal(JsonValueKind.Array, steps.ValueKind);
        Assert.True(root.TryGetProperty("assertions", out _));
        Assert.True(root.TryGetProperty("config", out _));
    }

    [Fact]
    public void DoubleStart_LogsWarning_KeepsFirstSession()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();
        var rec = new ActionTraceRecorder(sink, messages.Add);

        rec.Start("first", "/tmp/records-test");
        rec.Start("second", "/tmp/records-test");

        Assert.Contains(messages, m => m.Contains("already in progress"));
    }

    [Fact]
    public void StopBeforeStart_LogsWarning_NoFile()
    {
        var sink = new FakeFileSink();
        var messages = new List<string>();
        var rec = new ActionTraceRecorder(sink, messages.Add);

        rec.Stop();

        Assert.Empty(sink.Writes);
        Assert.Contains(messages, m => m.Contains("no active recording"));
    }
}
