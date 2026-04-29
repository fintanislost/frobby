using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Scenarios;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StardewValley;

namespace SdvTestFramework.Harness.Capture;

/// <summary>
/// Shared framebuffer capture implementation for immediate and render-synchronized bitmap RPCs.
/// </summary>
public static class BitmapCaptureWriter
{
    public static BitmapCaptureResult CaptureCurrent(JsonElement? paramsElement)
    {
        var s = ScenarioState.Current;
        if (!s.IsActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "bitmap.capture requires an active scenario (call scenario.begin first)");

        var req = paramsElement is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<BitmapCaptureRequest>(obj.GetRawText(), ProtocolJson.Options) ?? new BitmapCaptureRequest()
            : new BitmapCaptureRequest();

        if (!req.AllowUnfrozen && !DeterminismController.Frozen)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "bitmap.capture requires FREEZE phase (call freeze.begin first)");

        var gd = Game1.graphics?.GraphicsDevice
            ?? throw new JsonRpcException(JsonRpcErrorCode.InternalError,
                "bitmap.capture: GraphicsDevice unavailable");

        int bbW = gd.PresentationParameters.BackBufferWidth;
        int bbH = gd.PresentationParameters.BackBufferHeight;

        var region = req.Region;
        if (region is not null
            && (region.X < 0 || region.Y < 0 || region.W <= 0 || region.H <= 0
                || region.X + region.W > bbW || region.Y + region.H > bbH))
        {
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams,
                $"region {{x={region.X},y={region.Y},w={region.W},h={region.H}}} exceeds backbuffer {bbW}x{bbH}");
        }

        var buf = new Microsoft.Xna.Framework.Color[bbW * bbH];
        try { gd.GetBackBufferData(buf); }
        catch (Exception ex)
        {
            throw new JsonRpcException(JsonRpcErrorCode.InternalError,
                $"bitmap.capture: GetBackBufferData failed: {ex.Message}");
        }

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
            if (region is not null)
                img.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(region.X, region.Y, region.W, region.H)));

            outW = img.Width;
            outH = img.Height;

            var scenario = s.Name.Length > 0 ? s.Name : "unknown";
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "captures", scenario);
            Directory.CreateDirectory(dir);

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

        return new BitmapCaptureResult
        {
            Path = outPath,
            Width = outW,
            Height = outH,
        };
    }
}
