using Xunit;

namespace SdvTestFramework.Harness.Tests;

/// <summary>Integration tests for D1.6 — each requires a live SDV and is exercised via the
/// smoke test. Documented here so the behavior surface is visible at test-discovery time.</summary>
public class DeterminismIntegrationTests
{
    [Fact(Skip = "Requires live SDV at title screen — smoke test verifies this behavior.")]
    public void FreezeBegin_AtTitleScreen_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV mid-warp — smoke test verifies this behavior.")]
    public void FreezeBegin_MidWarp_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV in-save — smoke test verifies happy-path freeze → status → end.")]
    public void FreezeBegin_InSave_Succeeds_StatusReportsFrozen() { }

    [Fact(Skip = "Requires live SDV — smoke test confirms same-tick across snapshots while frozen.")]
    public void DrawSnapshots_TakenAcross2Seconds_WhileFrozen_ShareTickNumber() { }

    [Fact(Skip = "Requires live SDV — smoke confirms scenario.end auto-thaws a leaked freeze.")]
    public void ScenarioEnd_WhileFrozen_AutoThawsWithoutLeak() { }

    [Fact(Skip = "Requires live SDV — smoke confirms eventUp/displayHUD/locations[0].random restored.")]
    public void FullRoundTrip_RestoresAmbientAndRngState() { }

    [Fact(Skip = "Requires live SDV — smoke confirms Game1.background.position stable across freeze window (M0 parallax residual fix).")]
    public void ParallaxBackground_DoesNotDriftWhileFrozen() { }
}
