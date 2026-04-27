using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SdvTestFramework.Harness.Assets;

/// <summary>SMAPI <c>AssetReady</c> subscription that populates the shared <see cref="TextureAssetRegistry"/>.</summary>
// Hook: IContentEvents.AssetReady
// Type: Event subscription (observe only; no game-code modification)
// Why: Feed TextureAssetRegistry so DrawSnapshotHandler can resolve Texture2D → asset path
//      at snapshot time (D1.5 Tier 1 per .claude/rules/draw-call-recorder.md).
// Rollback: Remove the Apply() call from ModEntry; registry stays empty and draw.find with
//           texture_asset filter finds nothing (Tier 3 anonymous behavior).
// Tested in: tests/Harness.Tests/ContentLoadPatchesTests.cs (skip-marked integration)
// Depends on: SMAPI >= 4.1.10 (AssetReady event), MonoGame ContentManager (loadedAssets field)
//
// Design note — an earlier D1.5 draft used Harmony on ContentManager.Load<Texture2D> via
// MakeGenericMethod. That approach corrupts non-Texture2D loads: .NET shares JIT'd code
// across reference-type generic instantiations, so patching one closed generic method
// actually patches the shared body. Harmony's IL rewriting for the Texture2D-typed
// postfix then breaks when the shared body executes for a different T (e.g. the
// Dictionary<string,...> load of Data\BigCraftables) — observed failure was OOM in
// Path.Combine inside ContentManager.OpenStream during SDV content load.
// SMAPI's AssetReady + reflection into ContentManager.loadedAssets gets the same
// information without touching IL.
public static class ContentLoadPatches
{
    private static readonly FieldInfo? _loadedAssetsField = typeof(ContentManager)
        .GetField("loadedAssets", BindingFlags.NonPublic | BindingFlags.Instance);

    private static TextureAssetRegistry? _registry;

    public static void Apply(IModHelper helper, IMonitor monitor, TextureAssetRegistry registry)
    {
        _registry = registry;
        if (_loadedAssetsField is null)
        {
            monitor.Log(
                "ContentManager.loadedAssets field not found — MonoGame has changed and D1.5 Tier 1 is inert. Draw events will resolve as texture_asset: null.",
                LogLevel.Warn);
            return;
        }
        helper.Events.Content.AssetReady += OnAssetReady;
        monitor.Log(
            "Subscribed: IContentEvents.AssetReady — populates TextureAssetRegistry.",
            LogLevel.Info);
    }

    private static void OnAssetReady(object? sender, AssetReadyEventArgs e)
    {
        if (_registry is null || _loadedAssetsField is null) return;

        // MonoGame's ContentManager normalizes cache keys to forward slashes. IAssetName.BaseName
        // already uses forward slashes, so they match directly. Non-texture assets are silently
        // ignored — Tier 1 is best-effort; Tier 3 (texture_asset: null) handles the rest.
        if (_loadedAssetsField.GetValue(Game1.content) is not Dictionary<string, object> loadedAssets)
            return;

        var key = e.NameWithoutLocale.BaseName;
        if (loadedAssets.TryGetValue(key, out var asset) && asset is Texture2D tex)
        {
            _registry.Register(tex, key);
        }
    }
}
