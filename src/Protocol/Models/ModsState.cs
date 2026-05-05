using System;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>state.mods</c>.</summary>
public sealed class ModsState
{
    /// <summary>Loaded mod UniqueIDs, in SMAPI load order. Compact form for assertions and fixture metadata.</summary>
    public string[] UniqueIds { get; set; } = Array.Empty<string>();

    /// <summary>Loaded mod metadata, in SMAPI load order.</summary>
    public LoadedModSummary[] Mods { get; set; } = Array.Empty<LoadedModSummary>();
}

public sealed class LoadedModSummary
{
    public string UniqueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsContentPack { get; set; }
    public string? ContentPackFor { get; set; }
}
