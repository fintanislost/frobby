namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>festival.finish_grange_judging</c>.</summary>
public sealed class FinishGrangeJudgingResult : MutatorOk
{
    public string Id { get; set; } = string.Empty;
    public int? GrangeScore { get; set; }
    public bool GrangeJudged { get; set; }
}
