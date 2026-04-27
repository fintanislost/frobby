namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request DTO for the <c>scenario.begin</c> RPC method. <see cref="Name"/> is required;
/// <see cref="Seed"/> drives RNG pinning. <see cref="Fixture"/> is accepted for forward
/// compatibility with D1.4 (fixture loading) — ignored by the T12 handler.
/// </summary>
public sealed class ScenarioBeginRequest
{
    public string Name { get; set; } = string.Empty;
    public int Seed { get; set; }
    public string? Fixture { get; set; }   // accepted for future use; D1.4 wires it up
}
