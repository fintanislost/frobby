namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Success-response DTO for <c>scenario.begin</c>. <see cref="SessionId"/> is a GUID minted
/// on begin; <see cref="Tick"/> is <c>Game1.ticks</c> at the start of the scenario.
/// </summary>
public sealed class ScenarioBeginResult
{
    public string SessionId { get; set; } = string.Empty;
    public int Tick { get; set; }
}
