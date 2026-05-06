using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.location</c> RPC method. Runs on the game thread.</summary>
public static class StateLocationHandler
{
    public const string Method = "state.location";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        // Optional `name` param — defaults to current location.
        GameLocation? loc = Game1.currentLocation;
        if (paramsElement is { } p && p.TryGetProperty("name", out var nameEl))
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrEmpty(name))
                loc = Game1.getLocationFromName(name);
        }

        if (loc is null)
            return ProtocolJson.ToElement(new LocationState { Name = string.Empty });

        return ProtocolJson.ToElement(LocationStateProjector.ToState(loc));
    }
}
