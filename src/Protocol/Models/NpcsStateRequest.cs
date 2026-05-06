namespace SdvTestFramework.Protocol.Models;

/// <summary>Optional request shape for <c>state.npcs</c>.</summary>
public sealed class NpcsStateRequest
{
    public bool IncludeOffscreen { get; set; } = true;
    public int Limit { get; set; } = 200;
}
