using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>freeze.begin</c>.</summary>
public sealed class FreezeBeginResult : MutatorOk
{
    /// <summary>Number of <c>GameLocation</c>s whose <c>random</c> field was pinned.</summary>
    public int LocationsPinned { get; set; }

    /// <summary>Number of NPCs halted.</summary>
    public int NpcsHalted { get; set; }
}
