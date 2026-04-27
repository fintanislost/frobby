using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdvTestFramework.Harness.Assets;

/// <summary>
/// Read-only view of a <c>hash → asset_path</c> manifest produced by
/// <c>sdv-test build-manifest</c>. Absent manifest → empty + Tier 2 no-ops.
/// </summary>
public sealed class TextureHashManifest
{
    private readonly Dictionary<string, string> _map;

    public int Count => _map.Count;

    private TextureHashManifest(Dictionary<string, string> map) => _map = map;

    /// <summary>Load from disk. Missing / corrupt file → empty manifest (no throw).</summary>
    public static TextureHashManifest Load(string path)
    {
        if (!File.Exists(path))
            return new TextureHashManifest(new Dictionary<string, string>());

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<ManifestFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return new TextureHashManifest(parsed?.Manifest ?? new Dictionary<string, string>());
        }
        catch
        {
            return new TextureHashManifest(new Dictionary<string, string>());
        }
    }

    /// <summary>Look up a 16-hex-char hash prefix; returns null if absent.</summary>
    public string? TryResolve(string hashHex) =>
        _map.TryGetValue(hashHex, out var path) ? path : null;

    private sealed class ManifestFile
    {
        [JsonPropertyName("sdv_version")]
        public string? SdvVersion { get; set; }

        [JsonPropertyName("texture_count")]
        public int TextureCount { get; set; }

        [JsonPropertyName("manifest")]
        public Dictionary<string, string>? Manifest { get; set; }
    }
}
