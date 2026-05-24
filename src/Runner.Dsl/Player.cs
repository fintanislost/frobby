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

    /// <summary>Add received mail flag <paramref name="id"/> to the master farmer.</summary>
    public static async Task AddMail(string id, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new AddMailRequest { Id = id }, ProtocolJson.Options);
        await s.InvokeAsync("player.add_mail", p, ct);
    }

    /// <summary>Add secret note id <paramref name="id"/> to the master farmer's seen-note set.</summary>
    public static async Task AddSecretNoteSeen(int id, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new AddSecretNoteSeenRequest { Id = id }, ProtocolJson.Options);
        await s.InvokeAsync("player.add_secret_note_seen", p, ct);
    }

    /// <summary>Add numeric event id <paramref name="id"/> to the master farmer's seen-event set.</summary>
    public static async Task AddEventSeen(string id, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new AddEventSeenRequest { Id = id }, ProtocolJson.Options);
        await s.InvokeAsync("player.add_event_seen", p, ct);
    }

    /// <summary>Set friendship state for a vanilla or custom NPC.</summary>
    public static async Task SetFriendship(
        string npc,
        int points,
        bool? talkedToToday = null,
        int? giftsToday = null,
        int? giftsThisWeek = null,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new SetFriendshipRequest
        {
            Npc = npc,
            Points = points,
            TalkedToToday = talkedToToday,
            GiftsToday = giftsToday,
            GiftsThisWeek = giftsThisWeek,
        }, ProtocolJson.Options);
        await s.InvokeAsync("player.set_friendship", p, ct);
    }

    /// <summary>Give the player <paramref name="count"/> of item <paramref name="id"/> (e.g. <c>"(O)74"</c> for prismatic shard).</summary>
    public static async Task GiveItem(string id, int count = 1, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new GiveItemRequest { Id = id, Count = count }, ProtocolJson.Options);
        await s.InvokeAsync("player.give_item", p, ct);
    }

    /// <summary>Select an existing farmer inventory item by id or slot.</summary>
    public static async Task<PlayerSelectItemResult> SelectItem(
        string? id = null,
        int? slot = null,
        bool preferHotbar = true,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new PlayerSelectItemRequest
        {
            Id = id,
            Slot = slot,
            PreferHotbar = preferHotbar,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("player.select_item", p, ct);
        return JsonSerializer.Deserialize<PlayerSelectItemResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("player.select_item", Protocol.JsonRpcErrorCode.InternalError,
                "empty player.select_item response");
    }
}
