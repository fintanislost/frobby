using System.Reflection;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("Recorder")]
public class TextDrawSnapshotHandlerTests
{
    public TextDrawSnapshotHandlerTests()
    {
        Recorder.Initialize(null, capacity: 16);
        Recorder.Disarm();
    }

    private static void ForceArm()
    {
        var field = typeof(Recorder).GetField("_armed", BindingFlags.NonPublic | BindingFlags.Static)!;
        field.SetValue(null, true);
    }

    [Fact]
    public void Handle_EmptyBuffer_ReturnsEmptyEvents()
    {
        var result = DrawTextSnapshotHandler.Handle(null);

        Assert.Contains("\"events\":[]", result.GetRawText());
        Assert.Contains("\"dropped\":0", result.GetRawText());
    }

    [Fact]
    public void Handle_ReturnsCapturedTextEvents()
    {
        ForceArm();
        Recorder.RecordText(new TextDrawEvent
        {
            Tick = 101,
            CallIndex = 7,
            Text = "STARBERG TERMINAL v0.1.0",
            Position = new Vector2(64, 48),
            Color = new Color(255, 176, 0, 255),
            LayerDepth = 0.91f,
        });
        Recorder.Disarm();

        var result = DrawTextSnapshotHandler.Handle(null);

        Assert.Equal(1, result.GetProperty("events").GetArrayLength());
        var ev = result.GetProperty("events")[0];
        Assert.Equal("STARBERG TERMINAL v0.1.0", ev.GetProperty("text").GetString());
        Assert.Equal(64, ev.GetProperty("x").GetInt32());
        Assert.Equal(48, ev.GetProperty("y").GetInt32());
        Assert.Equal(0.91f, ev.GetProperty("layer_depth").GetSingle());
    }

    [Fact]
    public void ToDto_MapsAllFields()
    {
        var dto = DrawTextSnapshotHandler.ToDto(new TextDrawEvent
        {
            Tick = 101,
            CallIndex = 7,
            Text = "CASH & WIRES",
            Position = new Vector2(64.7f, 48.2f),
            Color = new Color(255, 176, 0, 255),
            LayerDepth = 0.91f,
        });

        Assert.Equal(101, dto.Tick);
        Assert.Equal(7, dto.Call);
        Assert.Equal("CASH & WIRES", dto.Text);
        Assert.Equal(64, dto.X);
        Assert.Equal(48, dto.Y);
        Assert.Equal(new[] { 255, 176, 0, 255 }, dto.Color);
        Assert.Equal(0.91f, dto.LayerDepth);
    }
}
