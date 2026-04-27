using System;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Serializable metadata per <c>.claude/rules/fixtures.md</c>.</summary>
public sealed class FixtureMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SdvVersion { get; set; } = string.Empty;
    public string SmapiVersion { get; set; } = string.Empty;
    public string[] ModsInstalled { get; set; } = Array.Empty<string>();
    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "fixture-builder";
    public string? Base { get; set; }
    public string RegenerateWith { get; set; } = string.Empty;
    public FarmerInfo Farmer { get; set; } = new();

    public sealed class FarmerInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
    }

    /// <summary>
    /// Build metadata from a fixture spec + runtime-captured environment info.
    /// The caller is responsible for capturing the inputs (via state RPCs) before calling.
    /// </summary>
    public static FixtureMetadata Generate(
        FixtureSpec spec,
        string sdvVersion,
        string smapiVersion,
        string[] mods,
        string farmerName,
        string farmerGender,
        DateTime createdAtUtc)
    {
        return new FixtureMetadata
        {
            Name = spec.Name,
            Description = spec.Description,
            SdvVersion = sdvVersion,
            SmapiVersion = smapiVersion,
            ModsInstalled = mods,
            CreatedAt = createdAtUtc.ToString("O"),
            CreatedBy = "fixture-builder",
            Base = spec.Base,
            RegenerateWith = $"tests/fixtures/{spec.Name}/{spec.Name}.fixture.json",
            Farmer = new FarmerInfo { Name = farmerName, Gender = farmerGender },
        };
    }
}
