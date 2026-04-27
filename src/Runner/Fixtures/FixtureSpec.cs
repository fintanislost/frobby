using System;
using System.Text.Json;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>DTO mirroring <c>schemas/fixture.schema.json</c>. Populated by <see cref="FixtureLoader"/>.</summary>
public sealed class FixtureSpec
{
    /// <summary>Fixture identifier — must match the containing directory name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Name of an existing fixture whose save is loaded as the starting state.
    /// <c>null</c> for root fixtures captured outside the scripted builder path
    /// (e.g. spike saves migrated into <c>tests/fixtures/</c>).
    /// </summary>
    public string? Base { get; set; }

    /// <summary>One-line human description, copied into metadata + README.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Ordered RPC step list, dispatched by <see cref="FixtureBuilder"/>.</summary>
    public FixtureStep[] Steps { get; set; } = Array.Empty<FixtureStep>();
}

/// <summary>A single step in a <see cref="FixtureSpec.Steps"/> list.</summary>
public sealed class FixtureStep
{
    /// <summary>RPC method name, e.g. <c>"player.set_money"</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Raw params element, passed through to the RPC. May be null.</summary>
    public JsonElement? Args { get; set; }
}
