using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape for <c>festival.set_grange_display</c>.</summary>
public sealed class SetGrangeDisplayRequest
{
    /// <summary>Whether to clear all display slots before placing requested items.</summary>
    public bool Clear { get; set; } = true;

    /// <summary>Items to place in the 0-based nine-slot grange display.</summary>
    public List<SetGrangeDisplayItemRequest> Items { get; set; } = new();
}

public sealed class SetGrangeDisplayItemRequest
{
    /// <summary>0-based grange display slot, from 0 to 8.</summary>
    public int Slot { get; set; }

    /// <summary>Qualified SDV object item id, e.g. <c>"(O)254"</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Optional stack override. Null keeps the created object's stack.</summary>
    public int? Stack { get; set; }

    /// <summary>Optional quality override. Null keeps the created object's quality.</summary>
    public int? Quality { get; set; }
}

public sealed class SetGrangeDisplayResult : MutatorOk
{
    public int FilledSlots { get; set; }
    public List<SetGrangeDisplayItemResult> Items { get; set; } = new();
}

public sealed class SetGrangeDisplayItemResult
{
    public int Slot { get; set; }
    public string Id { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int Quality { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
}
