namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>combat.attack</c>. Field names deserialize from snake_case via
/// <see cref="Json.ProtocolJson.Options"/>.
/// </summary>
public sealed class CombatAttackRequest
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? Direction { get; set; }
    public int Repeat { get; set; } = 1;
    public int DelayTicks { get; set; }
    public string? QualifiedItemId { get; set; }
}
