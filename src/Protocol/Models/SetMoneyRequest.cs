namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>player.set_money</c>. Field names deserialize from snake_case via
/// <see cref="Json.ProtocolJson.Options"/>.
/// </summary>
public sealed class SetMoneyRequest
{
    /// <summary>Absolute money value to set on the local farmer. Must be <c>&gt;= 0</c>.</summary>
    public int Amount { get; set; }
}
