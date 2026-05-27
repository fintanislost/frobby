namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>festival.continue</c>.</summary>
public sealed class FestivalContinueResult : MutatorOk
{
    public string Id { get; set; } = string.Empty;
    public bool IsFestival { get; set; }
}
