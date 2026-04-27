namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Universal success-response shape for state-mutator RPC methods. Handlers whose response
/// needs no method-specific fields return <see cref="MutatorOk"/> directly. Handlers with
/// extra fields (e.g. <c>player.set_money</c>'s <c>previous</c>) declare a derived DTO —
/// only create a subclass when it actually carries new state, not as a naming placeholder.
/// </summary>
public class MutatorOk
{
    public bool Ok { get; set; } = true;

    /// <summary>
    /// <c>Game1.ticks</c> at the moment the mutation was applied. Scenarios correlate
    /// post-mutation asserts to this tick (mutations are often queued and complete a few
    /// ticks later).
    /// </summary>
    public int Tick { get; set; }
}
