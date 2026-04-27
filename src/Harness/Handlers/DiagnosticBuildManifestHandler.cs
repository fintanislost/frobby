using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Protocol;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// <c>diagnostic.build_texture_manifest</c> — enumerates SDV's loaded content, hashes
/// every Texture2D, returns the full <c>{hash → asset_path}</c> map. Intended to be
/// driven by <c>sdv-test build-manifest</c> once per SDV version install.
/// </summary>
/// <remarks>
/// Blocks the game thread for 30-60 seconds while iterating ~4000 textures.
/// SDV will appear frozen during the build. One-time cost per SDV version.
/// </remarks>
public static class DiagnosticBuildManifestHandler
{
    public const string Method = "diagnostic.build_texture_manifest";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        // Reflect into ContentManager.loadedAssets — same field name and binding flags as
        // ContentLoadPatches.cs: GetField("loadedAssets", NonPublic | Instance).
        var loadedField = typeof(ContentManager).GetField(
            "loadedAssets", BindingFlags.Instance | BindingFlags.NonPublic);
        if (loadedField is null)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "loadedAssets field not found");

        // MonoGame stores loadedAssets as Dictionary<string, object>.
        // Cast matches what ContentLoadPatches.OnAssetReady does.
        if (loadedField.GetValue(Game1.content) is not Dictionary<string, object> loadedAssets)
            throw new JsonRpcException(JsonRpcErrorCode.InternalError, "loadedAssets not a dictionary");

        var map = new Dictionary<string, string>();
        int count = 0;
        foreach (var (key, value) in loadedAssets)
        {
            if (value is not Texture2D tex) continue;
            if (string.IsNullOrEmpty(key)) continue;

            try
            {
                var hash = TextureHasher.ComputeHashHexPrefix(tex);
                // If two textures share the 16-hex prefix (vanishingly rare), last-write wins.
                map[hash] = key;
                count++;
            }
            catch { /* skip GPU-backed / disposed textures */ }
        }

        var result = new JsonObject
        {
            ["sdv_version"] = Game1.version,
            ["texture_count"] = count,
            ["manifest"] = JsonNode.Parse(JsonSerializer.Serialize(map))!,
        };
        return JsonDocument.Parse(result.ToJsonString()).RootElement.Clone();
    }
}
