namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>player.select_item</c>.</summary>
public sealed class PlayerSelectItemRequest
{
    /// <summary>Inventory item id to select. Qualified ids such as <c>(O)287</c> are preferred.</summary>
    public string? Id { get; set; }

    /// <summary>Optional zero-based inventory slot to select.</summary>
    public int? Slot { get; set; }

    /// <summary>When selecting by id, prefer visible hotbar slots 0..11.</summary>
    public bool PreferHotbar { get; set; } = true;
}

/// <summary>Response shape for <c>player.select_item</c>.</summary>
public sealed class PlayerSelectItemResult : MutatorOk
{
    public int Slot { get; set; }
    public PlayerItemSummary Item { get; set; } = new();
}
