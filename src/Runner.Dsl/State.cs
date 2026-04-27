using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>state.*</c> read-only query surface.</summary>
public static class State
{
    public static async Task<PlayerState> Player(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.player", null, ct);
        return Deserialize<PlayerState>(resp, "state.player");
    }

    public static async Task<TimeState> Time(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.time", null, ct);
        return Deserialize<TimeState>(resp, "state.time");
    }

    public static async Task<LocationState> Location(string? name = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        JsonElement? p = name is null
            ? null
            : JsonSerializer.SerializeToElement(new { name }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.location", p, ct);
        return Deserialize<LocationState>(resp, "state.location");
    }

    public static async Task<NpcState> Npc(string name, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { name }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.npc", p, ct);
        return Deserialize<NpcState>(resp, "state.npc");
    }

    public static async Task<MenuState> Menu(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.menu", null, ct);
        return Deserialize<MenuState>(resp, "state.menu");
    }

    public static async Task<ModsState> Mods(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.mods", null, ct);
        return Deserialize<ModsState>(resp, "state.mods");
    }

    private static T Deserialize<T>(JsonElement el, string method)
        => JsonSerializer.Deserialize<T>(el, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException($"{method} returned null result");
}
