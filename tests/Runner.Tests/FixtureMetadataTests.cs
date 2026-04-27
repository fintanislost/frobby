using System;
using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Runner.Fixtures;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureMetadataTests
{
    [Fact]
    public void Generate_ProducesAllRuleFields()
    {
        var spec = new FixtureSpec
        {
            Name = "spring_day_5",
            Base = "m0spike_436515781",
            Description = "Spring day 5 with 500g",
        };
        var meta = FixtureMetadata.Generate(
            spec,
            sdvVersion: "1.6.15",
            smapiVersion: "4.5.2",
            mods: new[] { "A.B", "C.D" },
            farmerName: "Tester",
            farmerGender: "female",
            createdAtUtc: new DateTime(2026, 4, 23, 15, 30, 0, DateTimeKind.Utc));
        Assert.Equal("spring_day_5", meta.Name);
        Assert.Equal("m0spike_436515781", meta.Base);
        Assert.Equal("1.6.15", meta.SdvVersion);
        Assert.Equal("4.5.2", meta.SmapiVersion);
        Assert.Equal(new[] { "A.B", "C.D" }, meta.ModsInstalled);
        Assert.Equal("2026-04-23T15:30:00.0000000Z", meta.CreatedAt);
        Assert.Equal("fixture-builder", meta.CreatedBy);
        Assert.Equal("Tester", meta.Farmer.Name);
        Assert.Equal("female", meta.Farmer.Gender);
        Assert.Equal("tests/fixtures/spring_day_5/spring_day_5.fixture.json", meta.RegenerateWith);
    }

    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var spec = new FixtureSpec { Name = "x", Base = "y", Description = "z" };
        var meta = FixtureMetadata.Generate(spec, "1.6.15", "4.5.2", new[] { "a" }, "n", "male",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = JsonSerializer.Serialize(meta, ProtocolJson.Options);
        Assert.Contains("\"sdv_version\":\"1.6.15\"", json);
        Assert.Contains("\"smapi_version\":\"4.5.2\"", json);
        Assert.Contains("\"mods_installed\":[\"a\"]", json);
        Assert.Contains("\"created_at\":", json);
        Assert.Contains("\"created_by\":\"fixture-builder\"", json);
        Assert.Contains("\"regenerate_with\":\"tests/fixtures/x/x.fixture.json\"", json);
    }
}
