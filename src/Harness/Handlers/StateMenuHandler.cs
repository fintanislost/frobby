using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using StardewValley.Menus;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for the <c>state.menu</c> RPC method. Runs on the game thread.</summary>
public static class StateMenuHandler
{
    public const string Method = "state.menu";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        var menu = Game1.activeClickableMenu;
        if (menu is null)
            return ProtocolJson.ToElement(new MenuState { Present = false });

        var state = new MenuState
        {
            Type = menu.GetType().Name,
            Present = true,
        };

        // Menu-type-specific extras. Kept small for M1; extend per need.
        if (menu is ShopMenu shop)
        {
            state.Extra["currency"] = shop.currency.ToString();
            state.Extra["item_count"] = shop.forSale.Count.ToString();
        }
        else if (menu is DialogueBox dialog)
        {
            state.Extra["character"] = dialog.characterDialogue?.speaker?.Name ?? string.Empty;
        }

        return ProtocolJson.ToElement(state);
    }
}
