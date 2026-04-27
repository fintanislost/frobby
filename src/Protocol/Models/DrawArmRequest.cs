namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>draw.arm</c>. All fields are optional — absent params arm for the
/// default <see cref="Ticks"/> budget with in-memory capture only. Field names deserialize
/// from snake_case via <see cref="Json.ProtocolJson.Options"/>.
/// </summary>
public sealed class DrawArmRequest
{
    /// <summary>Number of update ticks to capture. Default 30 (~0.5s at 60fps).</summary>
    public int Ticks { get; set; } = 30;

    /// <summary>
    /// Optional output path. When set, the ring buffer is flushed to a JSONL file on disarm.
    /// When null, capture is in-memory only and retrievable via <c>draw.snapshot</c>.
    /// </summary>
    public string? OutputPath { get; set; }
}
