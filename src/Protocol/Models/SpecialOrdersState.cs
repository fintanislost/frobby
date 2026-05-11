using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of Stardew team special-order state. Response shape of <c>state.special_orders</c>.</summary>
public sealed class SpecialOrdersState
{
    public List<SpecialOrderSummary> Active { get; set; } = new();
    public List<SpecialOrderSummary> Available { get; set; } = new();
    public List<string> Completed { get; set; } = new();
    public List<string> AcceptedTypes { get; set; } = new();
    public List<SpecialOrderItemSummary> ReturnedDonations { get; set; } = new();
}

public sealed class SpecialOrderSummary
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Requester { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string SpecialRule { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public int? DueDate { get; set; }
    public string State { get; set; } = string.Empty;
    public bool? ReadyForRemoval { get; set; }
    public bool? IsTimed { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
    public List<SpecialOrderKeyValueSummary> SelectedRandomElements { get; set; } = new();
    public List<SpecialOrderKeyValueSummary> PreselectedItems { get; set; } = new();
    public List<SpecialOrderObjectiveSummary> Objectives { get; set; } = new();
    public List<SpecialOrderRewardSummary> Rewards { get; set; } = new();
    public List<SpecialOrderItemSummary> DonatedItems { get; set; } = new();
}

public sealed class SpecialOrderKeyValueSummary
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class SpecialOrderObjectiveSummary
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public string RuntimeType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CurrentCount { get; set; }
    public int? MaxCount { get; set; }
    public bool? Complete { get; set; }
    public string DropBox { get; set; } = string.Empty;
    public string DropBoxLocation { get; set; } = string.Empty;
    public TilePoint? DropBoxTile { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public List<string> AcceptedContextTags { get; set; } = new();
    public bool? Confirmed { get; set; }
    public int? MinimumCapacity { get; set; }
}

public sealed class SpecialOrderRewardSummary
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public string RuntimeType { get; set; } = string.Empty;
    public int? Amount { get; set; }
    public List<string> Mail { get; set; } = new();
}

public sealed class SpecialOrderItemSummary
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int? Quality { get; set; }
    public int? Category { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
