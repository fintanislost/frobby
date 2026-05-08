using System.Collections.Generic;
using System.Linq;
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

    public static async Task<LocationsState> Locations(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.locations", null, ct);
        return Deserialize<LocationsState>(resp, "state.locations");
    }

    public static async Task<NpcsState> Npcs(
        bool includeOffscreen = true,
        int limit = 200,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new NpcsStateRequest
        {
            IncludeOffscreen = includeOffscreen,
            Limit = limit,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.npcs", p, ct);
        return Deserialize<NpcsState>(resp, "state.npcs");
    }

    public static async Task<MapTileState> MapTile(
        string? location = null,
        int? x = null,
        int? y = null,
        IEnumerable<string>? layers = null,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new MapTileRequest
        {
            Location = location,
            X = x,
            Y = y,
            Layers = layers?.ToList(),
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.map_tile", p, ct);
        return Deserialize<MapTileState>(resp, "state.map_tile");
    }

    public static async Task<TileActionsState> TileActions(
        string? location = null,
        int? x = null,
        int? y = null,
        int radius = 0,
        IEnumerable<string>? layers = null,
        IEnumerable<string>? properties = null,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new TileActionsRequest
        {
            Location = location,
            X = x,
            Y = y,
            Radius = radius,
            Layers = layers?.ToList(),
            Properties = properties?.ToList(),
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.tile_actions", p, ct);
        return Deserialize<TileActionsState>(resp, "state.tile_actions");
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

    public static async Task<ShopState> Shop(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.shop", null, ct);
        return Deserialize<ShopState>(resp, "state.shop");
    }

    public static async Task<VisualEffectsState> VisualEffects(string? location = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        JsonElement? p = location is null
            ? null
            : JsonSerializer.SerializeToElement(new VisualEffectsRequest { Location = location }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("state.visual_effects", p, ct);
        return Deserialize<VisualEffectsState>(resp, "state.visual_effects");
    }

    public static async Task<EventState> Event(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("state.event", null, ct);
        return Deserialize<EventState>(resp, "state.event");
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
