using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("Recorder")]
public class TextDrawAssertContainsHandlerTests
{
    public TextDrawAssertContainsHandlerTests()
    {
        Recorder.Initialize(null, capacity: 16);
        Recorder.Disarm();
    }

    private static void ForceArm()
    {
        var field = typeof(Recorder).GetField("_armed", BindingFlags.NonPublic | BindingFlags.Static)!;
        field.SetValue(null, true);
    }

    private static void RecordText(string text)
    {
        ForceArm();
        Recorder.RecordText(new TextDrawEvent
        {
            Tick = 1,
            CallIndex = 1,
            Text = text,
            Position = new Vector2(64, 48),
            Color = new Color(255, 176, 0, 255),
            LayerDepth = 0.91f,
        });
        Recorder.Disarm();
    }

    [Fact]
    public void Contains_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertTextContainsHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Contains_NullFilter_TreatedAsEmpty()
    {
        var req = JsonDocument.Parse("""{"filter":null}""").RootElement;

        var result = DrawAssertTextContainsHandler.Handle(req);

        Assert.False(result.GetProperty("passed").GetBoolean());
        Assert.Equal(0, result.GetProperty("matched_count").GetInt32());
    }

    [Fact]
    public void Contains_MatchingTextPassesWithMessage()
    {
        RecordText("CASH & WIRES");
        var req = JsonDocument.Parse(
            """{"filter":{"text_contains":"CASH & WIRES","case_sensitive":true},"min_count":1,"message":"Cash panel should be visible"}""").RootElement;

        var result = DrawAssertTextContainsHandler.Handle(req);

        Assert.True(result.GetProperty("passed").GetBoolean());
        Assert.Equal(1, result.GetProperty("matched_count").GetInt32());
        Assert.Equal(1, result.GetProperty("min_count").GetInt32());
        Assert.Equal("Cash panel should be visible", result.GetProperty("message").GetString());
    }

    [Fact]
    public void Contains_MinCountZero_ThrowsInvalidParams()
    {
        var req = JsonDocument.Parse("""{"filter":{},"min_count":0}""").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => DrawAssertTextContainsHandler.Handle(req));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("min_count", ex.Message);
    }

    [Fact]
    public void NotContains_NoMatchPasses()
    {
        RecordText("STARBERG TERMINAL");
        var req = JsonDocument.Parse("""{"filter":{"text_contains":"missing"},"message":"No missing text"}""").RootElement;

        var result = DrawAssertTextNotContainsHandler.Handle(req);

        Assert.True(result.GetProperty("passed").GetBoolean());
        Assert.Equal(0, result.GetProperty("matched_count").GetInt32());
        Assert.Equal("No missing text", result.GetProperty("message").GetString());
    }

    [Fact]
    public void NotContains_MatchFails()
    {
        RecordText("STARBERG TERMINAL");
        var req = JsonDocument.Parse("""{"filter":{"text_contains":"STARBERG"}}""").RootElement;

        var result = DrawAssertTextNotContainsHandler.Handle(req);

        Assert.False(result.GetProperty("passed").GetBoolean());
        Assert.Equal(1, result.GetProperty("matched_count").GetInt32());
    }
}
