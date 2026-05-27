using System;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>state.event</c>. Runs on the game thread.</summary>
public static class StateEventHandler
{
    public const string Method = "state.event";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        // festival.start may return before the game has settled on the active festival
        // event object. Scenarios poll state.event for actors, so apply queued festival
        // actor additions immediately before projection as well as from UpdateTicked.
        SdvFestivalStartWorld.ApplyPendingAdditionalActors();

        var currentLocation = Game1.currentLocation;
        var viewport = Game1.viewport;
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            CurrentEvent = Game1.CurrentEvent,
            LocationEvent = currentLocation?.currentEvent,
            EventUp = Game1.eventUp,
            LocationName = currentLocation?.NameOrUniqueName ?? currentLocation?.Name ?? string.Empty,
            Viewport = new Microsoft.Xna.Framework.Rectangle(
                viewport.X,
                viewport.Y,
                viewport.Width,
                viewport.Height),
            ActiveMenu = Game1.activeClickableMenu,
            AdditionalActors = Game1.player is null ? Array.Empty<object?>() : new object?[] { Game1.player },
        });
        return ProtocolJson.ToElement(state);
    }
}
