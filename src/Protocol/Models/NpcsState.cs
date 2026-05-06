using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape for <c>state.npcs</c>.</summary>
public sealed class NpcsState
{
    public List<NpcState> Npcs { get; set; } = new();
}
