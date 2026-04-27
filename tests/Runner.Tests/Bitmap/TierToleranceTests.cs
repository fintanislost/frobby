using System;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class TierToleranceTests
{
    [Fact]
    public void TierCiUbuntu_SsimMethod_Returns098()
    {
        // Per spec §4.3 table: tier=ci-ubuntu + method=ssim → 0.98.
        var t = TierTolerance.Resolve("ci-ubuntu", BitmapMethod.Ssim, perAssertionTolerance: null);
        Assert.Equal(0.98, t, precision: 4);
    }

    [Fact]
    public void PerAssertionTolerance_OverridesTier()
    {
        // Tier ci-ubuntu would give 0.98; per-assertion 0.99 wins.
        var t = TierTolerance.Resolve("ci-ubuntu", BitmapMethod.Ssim, perAssertionTolerance: 0.99);
        Assert.Equal(0.99, t, precision: 4);
    }
}
