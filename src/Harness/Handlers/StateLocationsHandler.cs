using System.Linq;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.locations</c> RPC method. Runs on the game thread.</summary>
public static class StateLocationsHandler
{
    public const string Method = "state.locations";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var locations = Game1.locations
            .Where(loc => loc is not null)
            .Select(LocationStateProjector.ToSummary)
            .OrderBy(loc => loc.Name, System.StringComparer.Ordinal)
            .ThenBy(loc => loc.UniqueName, System.StringComparer.Ordinal)
            .ToList();

        return ProtocolJson.ToElement(new LocationsState { Locations = locations });
    }
}
