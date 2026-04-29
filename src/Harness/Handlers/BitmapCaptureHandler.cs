using System.Text.Json;
using SdvTestFramework.Harness.Capture;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>bitmap.capture</c>. Preconditions: scenario active + FREEZE phase
/// (delegates to the same checks as <see cref="FreezeBeginHandler"/> for consistency).
/// Reads the backbuffer via <see cref="GraphicsDevice.GetBackBufferData{T}(T[])"/>,
/// optionally crops to a region, encodes to PNG via ImageSharp, and writes to
/// <c>~/.cache/sdv-test-framework/captures/&lt;scenario&gt;/bitmap_&lt;N&gt;.png</c>.
/// Returns absolute path + dimensions.
/// </summary>
/// <remarks>
/// Auto-numbered output: index = count of existing <c>bitmap_*.png</c> files in the
/// scenario's capture dir. Keeps the handler stateless across scenarios.
/// MonoGame <see cref="Microsoft.Xna.Framework.Color"/> is layout-compatible with ImageSharp's
/// <c>Rgba32</c> on the supported FNA/XNA backends (both are packed RGBA bytes in memory).
/// This handler copies byte-by-byte rather than reinterpret-cast to stay portable across
/// backend quirks.
/// </remarks>
public static class BitmapCaptureHandler
{
    public const string Method = "bitmap.capture";

    public static JsonElement Handle(JsonElement? paramsElement)
        => ProtocolJson.ToElement(BitmapCaptureWriter.CaptureCurrent(paramsElement));
}
