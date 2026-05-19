using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>world.*</c> RPC surface.</summary>
public static class World
{
    /// <summary>Set the current weather (<c>"sunny"</c>, <c>"rain"</c>, etc.).</summary>
    public static async Task SetWeather(string type, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new WeatherRequest { Type = type }, ProtocolJson.Options);
        await s.InvokeAsync("world.set_weather", p, ct);
    }

    /// <summary>
    /// Trigger an NPC interaction by name. The NPC must be present in the player's current
    /// location. Mirrors what SDV does when the player presses action while facing the NPC
    /// at conversation distance.
    /// </summary>
    public static async Task InteractNpc(string name, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new WorldInteractNpcRequest { Name = name }, ProtocolJson.Options);
        await s.InvokeAsync("world.interact_npc", p, ct);
    }

    /// <summary>Interact with furniture or an object at a tile in the current location.</summary>
    public static async Task<InteractTileResult> InteractTile(
        int x,
        int y,
        bool justCheckingForActivity = false,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(
            new InteractTileRequest { X = x, Y = y, JustCheckingForActivity = justCheckingForActivity },
            ProtocolJson.Options);
        var resp = await s.InvokeAsync("world.interact_tile", p, ct);
        return JsonSerializer.Deserialize<InteractTileResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("world.interact_tile", Protocol.JsonRpcErrorCode.InternalError,
                "empty world.interact_tile response");
    }

    /// <summary>Run an Action or TouchAction map property at a tile in the current location.</summary>
    public static async Task<InteractTileResult> InteractTileAction(
        int? x = null,
        int? y = null,
        string? location = null,
        string? property = null,
        IEnumerable<string>? layers = null,
        bool justCheckingForActivity = false,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new InteractTileActionRequest
        {
            Location = location,
            X = x,
            Y = y,
            Property = property,
            Layers = layers?.ToList(),
            JustCheckingForActivity = justCheckingForActivity,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("world.interact_tile_action", p, ct);
        return JsonSerializer.Deserialize<InteractTileResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("world.interact_tile_action", Protocol.JsonRpcErrorCode.InternalError,
                "empty world.interact_tile_action response");
    }

    /// <summary>Use an equipped or inventory tool at a tile in the current or named location.</summary>
    public static async Task<UseToolResult> UseTool(
        string tool,
        int x,
        int y,
        string? location = null,
        string? facing = null,
        int power = 0,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new UseToolRequest
        {
            Tool = tool,
            Location = location,
            X = x,
            Y = y,
            Facing = facing,
            Power = power,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("world.use_tool", p, ct);
        return JsonSerializer.Deserialize<UseToolResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("world.use_tool", Protocol.JsonRpcErrorCode.InternalError,
                "empty world.use_tool response");
    }
}
