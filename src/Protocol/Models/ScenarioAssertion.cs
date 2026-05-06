using System.Text.Json;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Single assertion entry in a scenario's <c>assertions</c> array. Shape is a union across
/// assertion kinds (state, draw.contains, draw.not_contains, bitmap.diff, ...) discriminated by
/// <see cref="Type"/>; fields not applicable to a given <see cref="Type"/> are left null.
/// </summary>
public sealed class ScenarioAssertion
{
    /// <summary>Assertion kind, e.g. <c>state</c>, <c>draw.contains</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>For <c>state</c> assertions: an expression over <c>state.*</c> queries.</summary>
    public string? Expr { get; set; }

    /// <summary>For <c>content.asset</c> assertions: runtime asset name to query.</summary>
    public string? Asset { get; set; }

    /// <summary>For <c>content.asset</c> assertions: optional type hint such as map, texture, data, or string.</summary>
    public string? AssetType { get; set; }

    /// <summary>For <c>content.asset</c> data assertions: include a bounded key list.</summary>
    public bool? IncludeKeys { get; set; }

    /// <summary>For <c>content.asset</c> data assertions: maximum key count to include.</summary>
    public int? KeysLimit { get; set; }

    /// <summary>For <c>content.asset</c> data assertions: selected data entries to summarize.</summary>
    public string[]? EntryKeys { get; set; }

    /// <summary>For <c>content.asset</c> texture assertions: include content hash when possible.</summary>
    public bool? HashTexture { get; set; }

    /// <summary>For draw-call assertions: a <c>DrawFilter</c>- or <c>TextDrawFilter</c>-shaped JSON object.</summary>
    public JsonElement? Filter { get; set; }

    /// <summary>For draw-contains assertions: minimum match count (schema allows 0; runtime may tighten).</summary>
    public int MinCount { get; set; } = 1;

    /// <summary>For draw text contains assertions: optional maximum match count.</summary>
    public int? MaxCount { get; set; }

    /// <summary>Optional human-readable failure message override.</summary>
    public string? Message { get; set; }

    /// <summary>For <c>bitmap</c> assertions: path to the baseline PNG (relative resolves against the scenario file's directory).</summary>
    public string? Baseline { get; set; }

    /// <summary>For <c>bitmap</c> assertions: per-assertion tolerance override. Polymorphic per method (SSIM: float in (0, 1]; pixel-exact: int ≥ 0; dHash: int 0-64). Null → method default via <c>TierTolerance.Resolve</c>.</summary>
    public double? Tolerance { get; set; }

    /// <summary>For <c>bitmap</c> assertions: optional capture region <c>{x, y, w, h}</c>. <see cref="JsonElement"/> passed through to RPC.</summary>
    public JsonElement? Region { get; set; }

    /// <summary>For <c>bitmap</c> assertions: per-assertion override of the run-wide diff format. Null → use run-wide default.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("diff_format")]
    public DiffFormat? DiffFormat { get; set; }

    /// <summary>For <c>bitmap</c> assertions: diff method. Wire format: <c>"ssim" | "pixel-exact" | "dhash"</c>. Null → ssim.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>For <c>bitmap</c> assertions: per-assertion tier override. Wire format: <c>"generic" | "ci-ubuntu" | "self-hosted-nvidia"</c>. Null → use the run-wide tier.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tier")]
    public string? Tier { get; set; }
}
