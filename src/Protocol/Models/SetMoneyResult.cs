namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Response shape for <c>player.set_money</c>. Carries the farmer's money value
/// immediately before the mutation so scenarios can verify "previous money was X"
/// without an extra query.
/// </summary>
public sealed class SetMoneyResult : MutatorOk
{
    /// <summary><c>Game1.player.Money</c> captured immediately before the assignment.</summary>
    public int Previous { get; set; }
}
