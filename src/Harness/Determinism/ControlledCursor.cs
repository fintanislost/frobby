namespace SdvTestFramework.Harness.Determinism;

/// <summary>Scenario-scoped cursor override for intentional hover testing.</summary>
internal static class ControlledCursor
{
    private static int? _x;
    private static int? _y;

    public static bool HasOverride => _x.HasValue && _y.HasValue;

    public static void Set(int x, int y)
    {
        _x = x;
        _y = y;
    }

    public static void Clear()
    {
        _x = null;
        _y = null;
    }

    public static bool TryGet(out int x, out int y)
    {
        if (_x.HasValue && _y.HasValue)
        {
            x = _x.Value;
            y = _y.Value;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }
}
