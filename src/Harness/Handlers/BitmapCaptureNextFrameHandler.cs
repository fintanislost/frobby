using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Harness.Capture;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Handlers;

public static class BitmapCaptureNextFrameHandler
{
    public const string Method = "bitmap.capture_next_frame";

    public static RenderSynchronizedCaptureService CaptureService { get; set; } = new();
    public static Func<JsonElement?, BitmapCaptureResult> CaptureNow { get; set; } =
        BitmapCaptureWriter.CaptureCurrent;

    public static async Task<JsonElement?> HandleAsync(JsonElement? paramsElement, CancellationToken ct)
    {
        var captureParams = paramsElement is { } p ? p.Clone() : (JsonElement?)null;
        var req = captureParams is { ValueKind: JsonValueKind.Object } obj
            ? JsonSerializer.Deserialize<BitmapCaptureRequest>(obj.GetRawText(), ProtocolJson.Options) ?? new BitmapCaptureRequest()
            : new BitmapCaptureRequest();

        if (req.TimeoutMs < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.timeout_ms must be >= 1");

        var result = await CaptureService.RequestAsync(
            () => CaptureNow(captureParams),
            TimeSpan.FromMilliseconds(req.TimeoutMs),
            ct).ConfigureAwait(false);

        return ProtocolJson.ToElement(result);
    }
}
