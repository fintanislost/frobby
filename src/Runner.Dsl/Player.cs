using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>player.*</c> mutator RPC surface.</summary>
public static class Player
{
    /// <summary>Warp the player to <paramref name="location"/> at tile (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static async Task Warp(string location, int x, int y, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new WarpRequest { Location = location, X = x, Y = y }, ProtocolJson.Options);
        await s.InvokeAsync("player.warp", p, ct);
    }

    /// <summary>Set the player's money to <paramref name="amount"/>.</summary>
    public static async Task SetMoney(int amount, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new SetMoneyRequest { Amount = amount }, ProtocolJson.Options);
        await s.InvokeAsync("player.set_money", p, ct);
    }

    /// <summary>Give the player <paramref name="count"/> of item <paramref name="id"/> (e.g. <c>"(O)74"</c> for prismatic shard).</summary>
    public static async Task GiveItem(string id, int count = 1, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new GiveItemRequest { Id = id, Count = count }, ProtocolJson.Options);
        await s.InvokeAsync("player.give_item", p, ct);
    }
}
