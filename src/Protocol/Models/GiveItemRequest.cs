namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>player.give_item</c>. Field names deserialize from snake_case via
/// <see cref="Json.ProtocolJson.Options"/>.
/// </summary>
public sealed class GiveItemRequest
{
    /// <summary>
    /// SDV qualified item id (e.g. <c>"(O)388"</c> for wood). Passed to
    /// <c>ItemRegistry.Create</c> as-is.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Stack size to create; defaults to 1 when absent.</summary>
    public int Count { get; set; } = 1;
}
