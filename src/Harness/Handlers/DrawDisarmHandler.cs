using System.Text.Json;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>draw.disarm</c>. Stops recording and flushes any pending buffer to disk
/// (if an output path was provided at arm time); otherwise leaves the in-memory snapshot
/// available via <c>draw.snapshot</c>. Takes no params. Runs on the game thread.
/// </summary>
public static class DrawDisarmHandler
{
    public const string Method = "draw.disarm";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        Recorder.Disarm();
        return ProtocolJson.ToElement(new MutatorOk { Tick = Game1.ticks });
    }
}
