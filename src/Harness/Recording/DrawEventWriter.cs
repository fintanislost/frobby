using System.Globalization;
using System.IO;
using System.Text;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Deterministic JSON Lines serializer for <see cref="DrawEvent"/>. Hand-written to enforce:
/// fixed key order, invariant-culture number formatting, and "R" round-trip float formatting.
/// </summary>
/// <remarks>
/// System.Text.Json's default float formatting and property ordering have shifted between
/// .NET versions; for determinism-sensitive capture we serialize manually.
/// </remarks>
public static class DrawEventWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void WriteHeader(TextWriter w, int ticks, int events, int dropped, string reason)
    {
        w.Write("{\"type\":\"meta\",\"ticks\":");
        w.Write(ticks.ToString(Inv));
        w.Write(",\"events\":");
        w.Write(events.ToString(Inv));
        w.Write(",\"dropped\":");
        w.Write(dropped.ToString(Inv));
        w.Write(",\"reason\":");
        WriteString(w, reason);
        w.Write("}\n");
    }

    public static void WriteEvent(TextWriter w, in DrawEvent e)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"type\":\"draw\",\"tick\":").Append(e.Tick.ToString(Inv));
        sb.Append(",\"call\":").Append(e.CallIndex.ToString(Inv));
        sb.Append(",\"tex_ref\":").Append(e.TextureRefId.ToString(Inv));
        sb.Append(",\"tex_w\":").Append(e.TextureWidth.ToString(Inv));
        sb.Append(",\"tex_h\":").Append(e.TextureHeight.ToString(Inv));

        sb.Append(",\"src\":");
        if (e.SourceRect is { } sr)
            sb.Append('[').Append(sr.X.ToString(Inv)).Append(',').Append(sr.Y.ToString(Inv))
              .Append(',').Append(sr.Width.ToString(Inv)).Append(',').Append(sr.Height.ToString(Inv)).Append(']');
        else
            sb.Append("null");

        sb.Append(",\"dst\":[")
          .Append(e.DestRect.X.ToString(Inv)).Append(',').Append(e.DestRect.Y.ToString(Inv))
          .Append(',').Append(e.DestRect.Width.ToString(Inv)).Append(',').Append(e.DestRect.Height.ToString(Inv)).Append(']');

        sb.Append(",\"col\":[")
          .Append(e.Color.R.ToString(Inv)).Append(',').Append(e.Color.G.ToString(Inv))
          .Append(',').Append(e.Color.B.ToString(Inv)).Append(',').Append(e.Color.A.ToString(Inv)).Append(']');

        sb.Append(",\"rot\":").Append(e.Rotation.ToString("R", Inv));
        sb.Append(",\"orig\":[").Append(e.Origin.X.ToString("R", Inv)).Append(',').Append(e.Origin.Y.ToString("R", Inv)).Append(']');
        sb.Append(",\"fx\":").Append(((int)e.Effects).ToString(Inv));
        sb.Append(",\"z\":").Append(e.LayerDepth.ToString("R", Inv));
        sb.Append("}\n");
        w.Write(sb.ToString());
    }

    private static void WriteString(TextWriter w, string s)
    {
        w.Write('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': w.Write("\\\""); break;
                case '\\': w.Write("\\\\"); break;
                case '\n': w.Write("\\n"); break;
                case '\r': w.Write("\\r"); break;
                case '\t': w.Write("\\t"); break;
                default:
                    if (c < 0x20) w.Write("\\u" + ((int)c).ToString("X4", Inv));
                    else w.Write(c);
                    break;
            }
        }
        w.Write('"');
    }
}
