using System;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>state.mods</c>.</summary>
public sealed class ModsState
{
    /// <summary>Loaded mod UniqueIDs, in SMAPI load order.</summary>
    public string[] Mods { get; set; } = Array.Empty<string>();
}
