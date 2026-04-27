using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>Smoke-test: PosixSignalRegistration is available on the target runtime + registers cleanly.</summary>
public class PosixSignalRegistrationTests
{
    [Fact]
    public void RegisterSigterm_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        using var reg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => cts.Cancel());
        Assert.NotNull(reg);
    }

    [Fact]
    public void RegisterSigint_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        using var reg = PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => cts.Cancel());
        Assert.NotNull(reg);
    }
}
