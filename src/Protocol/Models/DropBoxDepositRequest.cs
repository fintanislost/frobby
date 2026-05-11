namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>drop_box.deposit</c>.</summary>
public sealed class DropBoxDepositRequest
{
    public string OrderKey { get; set; } = string.Empty;
    public string? DropBox { get; set; }
    public string? ItemId { get; set; }
    public string? QualifiedId { get; set; }
    public int Count { get; set; } = 1;
}

/// <summary>Result shape for <c>drop_box.deposit</c>.</summary>
public sealed class DropBoxDepositResult
{
    public bool Ok { get; set; }
    public string OrderKey { get; set; } = string.Empty;
    public string DropBox { get; set; } = string.Empty;
    public int DepositedCount { get; set; }
    public int ObjectiveIndex { get; set; }
    public int? BeforeCount { get; set; }
    public int? AfterCount { get; set; }
    public SpecialOrderItemSummary? Item { get; set; }
}
