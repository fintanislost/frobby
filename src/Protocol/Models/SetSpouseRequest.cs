namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>player.set_spouse</c>.</summary>
public sealed class SetSpouseRequest
{
    public string Npc { get; set; } = string.Empty;
    public int? Points { get; set; }
    public bool? Roommate { get; set; }
    public int? WeddingYear { get; set; }
    public string? WeddingSeason { get; set; }
    public int? WeddingDay { get; set; }
}

/// <summary>Result shape for <c>player.set_spouse</c>.</summary>
public sealed class SetSpouseResult : MutatorOk
{
    public string Spouse { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Status { get; set; } = "married";
}
