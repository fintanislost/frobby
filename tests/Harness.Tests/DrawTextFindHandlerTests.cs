using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("Recorder")]
public class DrawTextFindHandlerTests
{
    public DrawTextFindHandlerTests()
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
    public void Handle_DisarmAfterSnapshot_ReturnsMatchesAndDisarmsRecorder()
    {
        ForceArm();
        Recorder.RecordText(new TextDrawEvent
        {
            Tick = 1,
            CallIndex = 1,
            Text = "POSITIONING WATCH",
            Position = new Vector2(64, 48),
            Size = new Vector2(180, 24),
            Color = Color.White,
            LayerDepth = 0.91f,
        });

        var req = JsonDocument.Parse(
            """{"text_contains":"POSITIONING","disarm_after_snapshot":true}""").RootElement;

        var result = DrawTextFindHandler.Handle(req);

        Assert.Equal(1, result.GetProperty("count").GetInt32());
        Assert.False(Recorder.IsArmed);
    }
}
