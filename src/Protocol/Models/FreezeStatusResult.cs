namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>freeze.status</c> — lightweight query, no <c>Ok</c> needed.</summary>
public sealed class FreezeStatusResult
{
    public bool Frozen { get; set; }
    public bool IsWarping { get; set; }
    public bool IsFading { get; set; }
    public int Tick { get; set; }
}
