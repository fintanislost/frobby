using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of <c>Game1.activeClickableMenu</c>. Response shape of <c>state.menu</c>.</summary>
public sealed class MenuState
{
    /// <summary>CLR type name of the active menu (e.g. <c>ShopMenu</c>), or empty string when no menu is open.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>True if a menu is currently active.</summary>
    public bool Present { get; set; }

    /// <summary>
    /// Menu-type-specific extra fields (e.g. shop currency, dialogue speaker). Empty when no menu is active.
    /// </summary>
    /// <remarks>
    /// M1 simplicity trade-off: all values are strings, including values that are logically
    /// integers (<c>item_count</c>) or enums (<c>currency</c>). Consumers must parse. Keys are
    /// serialized through <c>DictionaryKeyPolicy</c> (snake_case) — use snake_case at the source
    /// to avoid a silent rename at serialization time. Upgrade path: swap to
    /// <c>Dictionary&lt;string, JsonElement&gt;</c> when a scenario needs typed access.
    /// </remarks>
    public Dictionary<string, string> Extra { get; set; } = new();
}
