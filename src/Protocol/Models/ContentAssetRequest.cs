namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for the <c>content.asset</c> RPC.</summary>
public sealed class ContentAssetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? AssetType { get; set; }
    public bool IncludeKeys { get; set; }
    public int? KeysLimit { get; set; }
    public string[]? EntryKeys { get; set; }
    public bool HashTexture { get; set; }
}
