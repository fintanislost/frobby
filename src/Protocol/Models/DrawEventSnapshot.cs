using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Response for <c>draw.snapshot</c>. Array of captured draw events + meta.</summary>
public sealed class DrawEventSnapshot
{
    public List<DrawEventDto> Events { get; set; } = new();
    public SnapshotMeta Meta { get; set; } = new();
}

/// <summary>
/// Wire shape for a captured draw event. Field names intentionally match the JSONL format
/// from the M0 spike's <c>DrawEventWriter</c> — short identifiers (<c>tex_w</c>, <c>src</c>,
/// etc.) to keep captured files compact.
/// </summary>
public sealed class DrawEventDto
{
    public int Tick { get; set; }
    public int Call { get; set; }
    public int TexRef { get; set; }
    public int TexW { get; set; }
    public int TexH { get; set; }

    /// <summary>
    /// Resolved asset path for this draw's texture, when known (e.g. <c>Characters/Abigail</c>).
    /// Null when Tier 1 resolution didn't find a mapping — either because the texture was
    /// engine-loaded before the harness's content-load patch caught it, or because it was
    /// dynamically generated (render targets etc.). Tier 2 hash fallback is deferred to M2.
    /// </summary>
    /// <remarks>
    /// Explicitly emitted as <c>null</c> rather than omitted (via <see cref="JsonIgnoreAttribute"/>)
    /// so scenario authors can distinguish "field unavailable in this protocol version" from
    /// "this texture has no asset path".
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? TextureAsset { get; set; }

    /// <summary>
    /// <c>null</c> when the overload didn't supply a source rect. Override the global
    /// <c>WhenWritingNull</c> policy so the wire shape always carries an explicit <c>"src":null</c>
    /// — consumers (and downstream <c>draw.find</c> in T11) distinguish "no rect given" from
    /// "field absent" and need the explicit marker.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int[]? Src { get; set; }
    public int[] Dst { get; set; } = System.Array.Empty<int>();
    public int[] Col { get; set; } = System.Array.Empty<int>();
    public float Rot { get; set; }
    public float[] Orig { get; set; } = System.Array.Empty<float>();
    public int Fx { get; set; }
    public float Z { get; set; }

    /// <summary>16-hex-char prefix of SHA-256(texture pixels). Present on all events once Tier 2 lands. Nullable until backfilled.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Texture dimensions as <c>[width, height]</c>. Present on all events. Nullable until backfilled.</summary>
    public int[]? TextureSize { get; set; }
}

/// <summary>Envelope meta for <see cref="DrawEventSnapshot"/>. Matches the JSONL "meta" line shape.</summary>
public sealed class SnapshotMeta
{
    public int Ticks { get; set; }
    public int Events { get; set; }
    public int Dropped { get; set; }

    /// <summary>
    /// Count of events where Tier 1 texture-asset resolution succeeded — i.e.
    /// <see cref="DrawEventDto.TextureAsset"/> is non-null. Divide by <see cref="Events"/>
    /// for the Tier 1 resolution rate (D1.5 acceptance criterion). Useful for diagnosing
    /// "why doesn't my texture_asset filter match?" — if the rate is low, the texture is
    /// likely engine-loaded before the harness's ContentLoad patch caught it. Tier 2 hash
    /// fallback (deferred to M2) will raise this rate for vanilla content.
    /// </summary>
    public int ResolvedCount { get; set; }
}
