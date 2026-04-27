using System.Reflection;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Patches;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("Recorder")]
public class TextDrawStringPatchTests
{
    public TextDrawStringPatchTests()
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
    public void StringBasicPrefix_RecordsTextEventWhenArmed()
    {
        ForceArm();
        var prefix = typeof(SpriteBatchDrawStringPatches).GetMethod(
            "Prefix_StringBasic",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        prefix.Invoke(null, new object?[]
        {
            null,
            "CASH & WIRES",
            new Vector2(12, 34),
            new Color(255, 176, 0, 255),
        });
        Recorder.Disarm();

        Recorder.SnapshotTextEvents(out var events, out _);
        Assert.Single(events);
        Assert.Equal("CASH & WIRES", events[0].Text);
        Assert.Equal(new Vector2(12, 34), events[0].Position);
        Assert.Equal(0f, events[0].LayerDepth);
    }

    [Fact]
    public void AllCurrentStringAndStringBuilderOverloads_HaveMatchingPrefix()
    {
        var methods = typeof(SpriteBatch)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "DrawString"
                     && m.GetParameters().Length >= 4
                     && m.GetParameters()[0].ParameterType == typeof(SpriteFont)
                     && (m.GetParameters()[1].ParameterType == typeof(string)
                         || m.GetParameters()[1].ParameterType == typeof(StringBuilder)))
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.True(
            SpriteBatchDrawStringPatches.CanPatchForTests(m),
            $"Missing DrawString prefix for {m}"));
    }

    [Fact]
    public void StringBuilderBasicPrefix_RecordsTextEventWhenArmed()
    {
        ForceArm();
        var prefix = typeof(SpriteBatchDrawStringPatches).GetMethod(
            "Prefix_StringBuilderBasic",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        prefix.Invoke(null, new object?[]
        {
            null,
            new StringBuilder("STARBERG TERMINAL"),
            new Vector2(22, 44),
            Color.White,
        });
        Recorder.Disarm();

        Recorder.SnapshotTextEvents(out var events, out _);
        Assert.Single(events);
        Assert.Equal("STARBERG TERMINAL", events[0].Text);
        Assert.Equal(new Vector2(22, 44), events[0].Position);
        Assert.Equal(0f, events[0].LayerDepth);
    }

    [Fact]
    public void StringScaleVectorPrefix_RecordsLayerDepth()
    {
        ForceArm();
        var prefix = typeof(SpriteBatchDrawStringPatches).GetMethod(
            "Prefix_StringScaleVector",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        prefix.Invoke(null, new object?[]
        {
            null,
            "CASH",
            new Vector2(12, 34),
            Color.Yellow,
            0f,
            Vector2.Zero,
            new Vector2(2f, 3f),
            SpriteEffects.FlipHorizontally,
            0.91f,
        });
        Recorder.Disarm();

        Recorder.SnapshotTextEvents(out var events, out _);
        Assert.Single(events);
        Assert.Equal("CASH", events[0].Text);
        Assert.Equal(0.91f, events[0].LayerDepth);
    }

    [Fact]
    public void StringScaleFloatPrefix_RecordsMeasuredSizeWithUniformScale()
    {
        try
        {
            SpriteBatchDrawStringPatches.SetMeasureStringForTests((_, text) => new Vector2(text.Length * 3, 8));
            ForceArm();
            var prefix = typeof(SpriteBatchDrawStringPatches).GetMethod(
                "Prefix_StringScaleFloat",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            prefix.Invoke(null, new object?[]
            {
                null,
                "CASH",
                new Vector2(12, 34),
                Color.Yellow,
                0f,
                Vector2.Zero,
                2f,
                SpriteEffects.None,
                0.91f,
            });
            Recorder.Disarm();

            Recorder.SnapshotTextEvents(out var events, out _);
            Assert.Single(events);
            Assert.Equal(new Vector2(24, 16), events[0].Size);
        }
        finally
        {
            Recorder.Disarm();
            SpriteBatchDrawStringPatches.SetMeasureStringForTests(null);
        }
    }

    [Fact]
    public void StringScaleVectorPrefix_RecordsMeasuredSizeWithVectorScale()
    {
        try
        {
            SpriteBatchDrawStringPatches.SetMeasureStringForTests((_, _) => new Vector2(10, 8));
            ForceArm();
            var prefix = typeof(SpriteBatchDrawStringPatches).GetMethod(
                "Prefix_StringScaleVector",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            prefix.Invoke(null, new object?[]
            {
                null,
                "CASH",
                new Vector2(12, 34),
                Color.Yellow,
                0f,
                Vector2.Zero,
                new Vector2(-2f, 3f),
                SpriteEffects.None,
                0.91f,
            });
            Recorder.Disarm();

            Recorder.SnapshotTextEvents(out var events, out _);
            Assert.Single(events);
            Assert.Equal(new Vector2(20, 24), events[0].Size);
        }
        finally
        {
            Recorder.Disarm();
            SpriteBatchDrawStringPatches.SetMeasureStringForTests(null);
        }
    }
}
