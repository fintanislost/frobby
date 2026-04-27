using System;

namespace SdvTestFramework.Harness.Scenarios;

/// <summary>
/// Process-wide state for the active scenario (if any). Singleton accessor pattern because
/// the harness only ever runs one scenario at a time — parallel scenarios would require
/// parallel SDV processes (outside M1 scope).
/// </summary>
public sealed class ScenarioState
{
    public static ScenarioState Current { get; } = new();

    public bool IsActive { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StartTick { get; set; }
    public DateTime StartUtc { get; set; }
    public int AssertionsRun { get; set; }
    public int AssertionsPassed { get; set; }

    /// <summary>Seed supplied at <c>scenario.begin</c>. Consumed by <c>DeterminismController</c>
    /// at <c>freeze.begin</c> to pin per-location RNG deterministically.</summary>
    public int Seed { get; set; }

    /// <summary>Clears all scenario state. Call at scenario.end or on a fresh scenario.begin.</summary>
    public void Reset()
    {
        IsActive = false;
        SessionId = string.Empty;
        Name = string.Empty;
        StartTick = 0;
        StartUtc = DateTime.UtcNow;
        AssertionsRun = 0;
        AssertionsPassed = 0;
        Seed = 0;
    }
}
