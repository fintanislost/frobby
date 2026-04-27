using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>
/// Determinism guarantees of the serializer — locking down the behaviors that the M0 spike
/// relied on. These must not regress: two runs that produce the same in-memory event must
/// produce byte-identical JSONL.
/// </summary>
public class DrawEventWriterTests
{
    [Fact]
    public void WriteEvent_ProducesExpectedJsonLine()
    {
        var ev = new DrawEvent
        {
            Tick = 5,
            CallIndex = 1,
            TextureRefId = 42,
            TextureWidth = 64,
            TextureHeight = 128,
            SourceRect = new Rectangle(0, 0, 16, 16),
            DestRect = new Rectangle(10, 20, 32, 32),
            Color = new Color(255, 128, 64, 200),
            Rotation = 0f,
            Origin = Vector2.Zero,
            Effects = SpriteEffects.None,
            LayerDepth = 0.5f,
        };

        using var sw = new StringWriter();
        DrawEventWriter.WriteEvent(sw, in ev);

        var expected =
            "{\"type\":\"draw\",\"tick\":5,\"call\":1,\"tex_ref\":42,\"tex_w\":64,\"tex_h\":128," +
            "\"src\":[0,0,16,16],\"dst\":[10,20,32,32],\"col\":[255,128,64,200],\"rot\":0,\"orig\":[0,0],\"fx\":0,\"z\":0.5}\n";

        Assert.Equal(expected, sw.ToString());
    }

    [Fact]
    public void WriteEvent_NullSourceRect_SerializesAsNull()
    {
        var ev = new DrawEvent { DestRect = new Rectangle(0, 0, 1, 1), Color = Color.White };

        using var sw = new StringWriter();
        DrawEventWriter.WriteEvent(sw, in ev);

        Assert.Contains("\"src\":null", sw.ToString());
    }

    [Fact]
    public void WriteEvent_FloatsUseRoundTripInvariantFormat()
    {
        var ev = new DrawEvent
        {
            DestRect = new Rectangle(0, 0, 1, 1),
            Color = Color.White,
            Rotation = 0.1234567f,
            LayerDepth = 0.987654f,
        };

        using var sw = new StringWriter();
        DrawEventWriter.WriteEvent(sw, in ev);
        var line = sw.ToString();

        // "R" round-trip format: the exact value is recoverable from the text.
        Assert.Contains(ev.Rotation.ToString("R", CultureInfo.InvariantCulture), line);
        Assert.Contains(ev.LayerDepth.ToString("R", CultureInfo.InvariantCulture), line);
    }

    [Fact]
    public void WriteEvent_CultureIndependent()
    {
        // Crucial for cross-environment determinism: a dev on fr-FR must produce the same
        // bytes as CI on en-US. "0.5" must not become "0,5".
        var ev = new DrawEvent
        {
            DestRect = new Rectangle(0, 0, 1, 1),
            Color = Color.White,
            Rotation = 0.5f,
        };

        var prior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
            using var sw = new StringWriter();
            DrawEventWriter.WriteEvent(sw, in ev);
            Assert.Contains("\"rot\":0.5", sw.ToString());
            Assert.DoesNotContain("0,5", sw.ToString());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prior;
        }
    }

    [Fact]
    public void WriteHeader_MetaShape()
    {
        using var sw = new StringWriter();
        DrawEventWriter.WriteHeader(sw, ticks: 30, events: 1234, dropped: 5, reason: "ok");
        Assert.Equal(
            "{\"type\":\"meta\",\"ticks\":30,\"events\":1234,\"dropped\":5,\"reason\":\"ok\"}\n",
            sw.ToString());
    }

    [Fact]
    public void WriteHeader_EscapesSpecialReasons()
    {
        using var sw = new StringWriter();
        DrawEventWriter.WriteHeader(sw, 0, 0, 0, "line1\nline2\t\"quote\"");
        Assert.Contains(@"""reason"":""line1\nline2\t\""quote\""""", sw.ToString());
    }
}
