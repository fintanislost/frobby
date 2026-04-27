using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class RpcPreconditionsTests
{
    [Fact(Skip = "Requires live SDV — predicate widening is exercised by the sample-suite smoke (T10). This placeholder documents the behavior surface.")]
    public void RequireWorldReady_AtTitleScreen_Throws() { }

    [Fact(Skip = "Requires live SDV — predicate widening is exercised by the sample-suite smoke (T10).")]
    public void RequireWorldReady_DuringPlayingGameMode_DoesNotThrow() { }
}
