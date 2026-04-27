using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Client-side wait primitives — no RPC, but the game keeps ticking during the delay.</summary>
public static class Wait
{
    /// <summary>Sleep for <paramref name="ms"/> milliseconds. Use between RPCs to let async game-thread work (warps, loading) complete.</summary>
    public static Task Ms(int ms, CancellationToken ct = default) => Task.Delay(ms, ct);
}
