using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>draw.arm</c>. Arms the draw-event recorder for the next N ticks. When
/// <c>output_path</c> is provided the buffer is also flushed to a JSONL file on completion;
/// otherwise the events are retained in memory for retrieval via <c>draw.snapshot</c>.
/// Runs on the game thread.
/// </summary>
public static class DrawArmHandler
{
    public const string Method = "draw.arm";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        // Optional<T> mirrors Required<T>'s try/catch around JsonException so malformed fields
        // (e.g. "ticks":"thirty") surface as InvalidParams rather than a raw 500.
        var req = RpcParams.Optional<DrawArmRequest>(paramsElement);
        if (req.Ticks < 1)
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.ticks must be >= 1");

        Recorder.Arm(req.Ticks, req.OutputPath);

        // MutatorOk directly: "armed: true" would be redundant with "ok: true".
        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
