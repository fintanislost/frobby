using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>freeze.*</c> determinism-controller RPCs.</summary>
public static class Freeze
{
    /// <summary>Enter FREEZE phase — pins RNG, halts NPCs, stops the game-time clock.</summary>
    public static async Task Begin(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        await s.InvokeAsync("freeze.begin", null, ct);
    }

    /// <summary>Exit FREEZE phase — restores snapshotted state.</summary>
    public static async Task End(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        await s.InvokeAsync("freeze.end", null, ct);
    }

    /// <summary>Query current FREEZE state.</summary>
    public static async Task<FreezeStatusResult> Status(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("freeze.status", null, ct);
        return JsonSerializer.Deserialize<FreezeStatusResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("freeze.status returned no result");
    }
}
