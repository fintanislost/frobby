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
}
