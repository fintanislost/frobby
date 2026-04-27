using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>bitmap.*</c> RPC surface (FREEZE-phase framebuffer capture).</summary>
public static class Bitmap
{
    public static async Task<BitmapCaptureResult> Capture(BitmapRegion? region = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        JsonElement? p = null;
        if (region is { } r)
        {
            p = JsonSerializer.SerializeToElement(
                new { region = new { x = r.X, y = r.Y, w = r.W, h = r.H } },
                ProtocolJson.Options);
        }
        var resp = await s.InvokeAsync("bitmap.capture", p, ct);
        return JsonSerializer.Deserialize<BitmapCaptureResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("bitmap.capture returned null");
    }
}
