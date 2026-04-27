using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StardewValley;

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
    {
        var s = ScenarioState.Current;
        if (!s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "bitmap.capture requires an active scenario (call scenario.begin first)");

        if (!DeterminismController.Frozen)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "bitmap.capture requires FREEZE phase (call freeze.begin first)");

        var gd = Game1.graphics?.GraphicsDevice
            ?? throw new JsonRpcException(JsonRpcErrorCode.InternalError,
                "bitmap.capture: GraphicsDevice unavailable");

        int bbW = gd.PresentationParameters.BackBufferWidth;
        int bbH = gd.PresentationParameters.BackBufferHeight;

        // Optional region crop. Validate bounds.
        int? rx = null, ry = null, rw = null, rh = null;
        if (paramsElement is { ValueKind: JsonValueKind.Object } p
            && p.TryGetProperty("region", out var r)
            && r.ValueKind == JsonValueKind.Object)
        {
            rx = r.GetProperty("x").GetInt32();
            ry = r.GetProperty("y").GetInt32();
            rw = r.GetProperty("w").GetInt32();
            rh = r.GetProperty("h").GetInt32();
            if (rx < 0 || ry < 0 || rw <= 0 || rh <= 0
                || rx + rw > bbW || ry + rh > bbH)
                throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                    $"region {{x={rx},y={ry},w={rw},h={rh}}} exceeds backbuffer {bbW}×{bbH}");
        }

        // Grab backbuffer. Color is packed RGBA; one entry per pixel.
        var buf = new Microsoft.Xna.Framework.Color[bbW * bbH];
        try { gd.GetBackBufferData(buf); }
        catch (Exception ex)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InternalError,
                $"bitmap.capture: GetBackBufferData failed: {ex.Message}");
        }

        // Convert to Rgba32 bytes. Byte-by-byte copy is safer than a reinterpret cast
        // across backend variations (FNA vs XNA vs DesktopGL).
        var bytes = new byte[buf.Length * 4];
        for (int i = 0; i < buf.Length; i++)
        {
            bytes[i * 4 + 0] = buf[i].R;
            bytes[i * 4 + 1] = buf[i].G;
            bytes[i * 4 + 2] = buf[i].B;
            bytes[i * 4 + 3] = buf[i].A;
        }

        string outPath;
        int outW, outH;
        try
        {
            using var img = Image.LoadPixelData<Rgba32>(bytes, bbW, bbH);
            if (rx is not null)
            {
                // Extract to non-nullable locals so the lambda capture is verifiably non-null.
                int cropX = rx.Value, cropY = ry!.Value, cropW = rw!.Value, cropH = rh!.Value;
                img.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(cropX, cropY, cropW, cropH)));
            }

            outW = img.Width;
            outH = img.Height;

            var scenario = s.Name.Length > 0 ? s.Name : "unknown";
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "captures", scenario);
            Directory.CreateDirectory(dir);

            // Auto-number: count existing bitmap_*.png files, use count as next index.
            int n = Directory.EnumerateFiles(dir, "bitmap_*.png").Count();
            outPath = Path.Combine(dir, $"bitmap_{n}.png");

            img.SaveAsPng(outPath);
        }
        catch (JsonRpcException) { throw; }
        catch (Exception ex)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InternalError,
                $"bitmap.capture: encode/write failed: {ex.Message}");
        }

        return ProtocolJson.ToElement(new BitmapCaptureResult
        {
            Path = outPath,
            Width = outW,
            Height = outH,
        });
    }
}
