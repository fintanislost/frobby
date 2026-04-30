using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Patches;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("ScenarioState")]
public class ControlledCursorTests
{
    public ControlledCursorTests()
    {
        ControlledCursor.Clear();
        DeterminismController.ResetForTests();
    }

    [Fact]
    public void Set_OverridesCursorCoordinates()
    {
        ControlledCursor.Set(144, 134);

        Assert.True(ControlledCursor.TryGet(out var x, out var y));
        Assert.Equal((144, 134), (x, y));
    }

    [Fact]
    public void Clear_RemovesCursorOverride()
    {
        ControlledCursor.Set(144, 134);
        ControlledCursor.Clear();

        Assert.False(ControlledCursor.TryGet(out _, out _));
    }

    [Fact]
    public void CursorPatch_UsesOriginalCoordinatesWhenUnfrozenAndUncontrolled()
    {
        Assert.Equal(300, CursorPatches.ResolveX(300));
        Assert.Equal(400, CursorPatches.ResolveY(400));
    }

    [Fact]
    public void CursorPatch_UsesZeroWhenFrozenAndUncontrolled()
    {
        DeterminismController.EnterFreeze(seed: 1, monitor: null);

        Assert.Equal(0, CursorPatches.ResolveX(300));
        Assert.Equal(0, CursorPatches.ResolveY(400));
    }

    [Fact]
    public void CursorPatch_UsesControlledCoordinatesWhileFrozen()
    {
        ControlledCursor.Set(144, 134);
        DeterminismController.EnterFreeze(seed: 1, monitor: null);

        Assert.Equal(144, CursorPatches.ResolveX(300));
        Assert.Equal(134, CursorPatches.ResolveY(400));
    }
}
