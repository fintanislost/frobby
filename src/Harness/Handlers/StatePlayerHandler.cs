using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.player</c> RPC method. Runs on the game thread.</summary>
public static class StatePlayerHandler
{
    public const string Method = "state.player";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var p = Game1.player;
        var state = new PlayerState
        {
            Name = p.Name ?? string.Empty,
            Money = p.Money,
            Stamina = (int)p.Stamina,
            MaxStamina = p.MaxStamina,
            Health = p.health,
            Location = Game1.currentLocation?.Name ?? string.Empty,
            Tile = new TilePoint { X = p.TilePoint.X, Y = p.TilePoint.Y },
        };
        return ProtocolJson.ToElement(state);
    }
}
