using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("Recorder")]
public class DrawAssertNotContainsHandlerTests
{
    public DrawAssertNotContainsHandlerTests()
    {
        Recorder.Initialize(null, capacity: 16);
        Recorder.Disarm();
    }

    /// <summary>
    /// Force the Recorder into armed state directly (bypasses the Game1.gameMode guard
    /// that would defer arming in a test context with no live SDV instance).
    /// </summary>
    private static void ForceArm()
    {
        var field = typeof(Recorder).GetField("_armed",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        field.SetValue(null, true);
    }

    [Fact]
    public void Handle_EmptyBuffer_PassesOk()
    {
        var req = JsonDocument.Parse("""{"filter":{"texture_asset":"anything"}}""").RootElement;
        var resp = DrawAssertNotContainsHandler.Handle(req);
        Assert.True(resp.GetProperty("passed").GetBoolean());
        Assert.Equal(0, resp.GetProperty("matched_count").GetInt32());
    }

    [Fact]
    public void Handle_MatchFound_FailsWithCount()
    {
        // Seed a draw event that matches, then assert_not_contains should fail with passed=false.
        // ForceArm bypasses the Game1.gameMode guard that would defer arming in a test context.
        // Constructor already called Initialize which resets the buffer head to 0.
        ForceArm();
        Recorder.Record(new DrawEvent
        {
            Tick = 1, CallIndex = 1,
            DestRect = new Rectangle(0, 0, 16, 16),
            Color = Color.White,
        });
        Recorder.Disarm();

        var req = JsonDocument.Parse("""{"filter":{}}""").RootElement;  // empty filter matches all
        var resp = DrawAssertNotContainsHandler.Handle(req);
        Assert.False(resp.GetProperty("passed").GetBoolean());
        Assert.Equal(1, resp.GetProperty("matched_count").GetInt32());
    }

    [Fact]
    public void Handle_InvalidFilter_ThrowsInvalidParams()
    {
        // in_rect with negative width fails DrawFilterValidator — same code path as assert_contains.
        var req = JsonDocument.Parse("""{"filter":{"in_rect":[0,0,-1,10]}}""").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertNotContainsHandler.Handle(req));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }
}
