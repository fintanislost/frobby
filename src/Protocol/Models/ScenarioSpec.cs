using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// In-memory representation of a <c>*.test.json</c> scenario, validated against
/// <c>schemas/scenario.schema.json</c>. See spec §4.6.
/// </summary>
public sealed class ScenarioSpec
{
    /// <summary>Human-readable scenario name; must be non-empty per schema.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional fixture save to load before steps execute.</summary>
    public string? Fixture { get; set; }

    /// <summary>Optional repo-local mod/config profile required by this scenario.</summary>
    public string? Profile { get; set; }

    /// <summary>Optional save-file mutations applied only to staged fixture copies.</summary>
    public ScenarioSaveOverrides? SaveOverrides { get; set; }

    /// <summary>Mods to ensure are loaded for this scenario.</summary>
    public List<string> Mods { get; set; } = new();

    /// <summary>Determinism + display config. Defaults: seed=42, zoom=1.0, resolution=[1280, 720].</summary>
    public ScenarioConfig Config { get; set; } = new();

    /// <summary>Ordered ARRANGE/ACT steps driving the scenario.</summary>
    public List<ScenarioStep> Steps { get; set; } = new();

    /// <summary>ASSERT phase checks evaluated after steps complete.</summary>
    public List<ScenarioAssertion> Assertions { get; set; } = new();
}

/// <summary>
/// Scenario-level configuration: RNG seed, render zoom, resolution. Defaults match the spec's
/// common-case expectations.
/// </summary>
public sealed class ScenarioConfig
{
    /// <summary>Seed for <c>Game1.random</c> pinning (see <c>determinism.md</c>).</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Zoom level passed to SDV at scenario begin.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>[width, height] in pixels. Schema enforces exactly two ints.</summary>
    public int[] Resolution { get; set; } = { 1280, 720 };
}

/// <summary>Scenario-level staged save overrides. Source fixtures are never modified.</summary>
public sealed class ScenarioSaveOverrides
{
    /// <summary>Optional farm-type override for additional/modded farm layouts.</summary>
    public ScenarioFarmTypeSaveOverride? FarmType { get; set; }
}

/// <summary>Farm metadata override for a staged save copy.</summary>
public sealed class ScenarioFarmTypeSaveOverride
{
    /// <summary>Farm kind. Currently only "mod" is supported.</summary>
    public string? WhichFarm { get; set; }

    /// <summary>Opaque additional-farm id, such as "FrontierFarm".</summary>
    public string? ModFarmId { get; set; }
}
