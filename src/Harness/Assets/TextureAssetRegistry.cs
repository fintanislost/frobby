using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;

namespace SdvTestFramework.Harness.Assets;

/// <summary>
/// Weak-reference map from loaded <see cref="Texture2D"/> instances to the asset path they
/// were loaded from. Populated by <see cref="ContentLoadPatches"/> (D1.5 T3) and queried at
/// snapshot time by <see cref="Handlers.DrawSnapshotHandler"/> (D1.5 T5).
/// </summary>
/// <remarks>
/// Uses <see cref="ConditionalWeakTable{TKey, TValue}"/> so GC'd textures drop out
/// automatically — no manual invalidation needed for texture lifetime management. SMAPI's
/// <c>AssetsInvalidated</c> event could force-drop entries on cache reloads, but since the
/// texture instance usually goes away (new instance on reload) the weak-ref eviction handles
/// it naturally.
///
/// <para>Not thread-safe for concurrent writers. Registrations happen from the game thread
/// (the <c>ContentManager.Load</c> postfix runs wherever content is loaded, single-threaded
/// in SDV). Reads happen at snapshot time — also game thread, after the recorder disarms.
/// Single-writer / single-reader is the contract.</para>
/// </remarks>
public sealed class TextureAssetRegistry
{
    /// <summary>
    /// Process-wide instance populated by <c>ContentLoadPatches</c> at mod startup
    /// and read by <c>DrawSnapshotHandler</c> at snapshot time. Null until
    /// <c>ModEntry.Entry</c> initializes it; handlers should treat null as "no Tier 1
    /// resolution available" and fall through to Tier 3 (texture_asset: null).
    /// </summary>
    /// <remarks>Setter is <c>internal</c> so only harness code + its test project (via
    /// <c>InternalsVisibleTo</c>) can assign; no external mod can stomp the singleton.</remarks>
    public static TextureAssetRegistry? Shared { get; internal set; }

    private readonly ConditionalWeakTable<object, string> _map = new();

    /// <summary>Associate <paramref name="texture"/> with <paramref name="assetName"/>. No-op when <paramref name="texture"/> is null.</summary>
    public void Register(Texture2D? texture, string assetName) => RegisterCore(texture, assetName);

    /// <summary>Lookup the asset path previously registered for <paramref name="texture"/>. Returns null when unregistered or when <paramref name="texture"/> is null.</summary>
    public string? TryResolve(Texture2D? texture) => TryResolveCore(texture);

    // --- internal core (takes object so test shims can reuse it without a GraphicsDevice) ---

    internal void RegisterCore(object? key, string assetName)
    {
        if (key is null) return;
        // ConditionalWeakTable lacks AddOrUpdate; emulate via remove-then-add.
        _map.Remove(key);
        _map.Add(key, assetName);
    }

    internal string? TryResolveCore(object? key)
    {
        if (key is null) return null;
        return _map.TryGetValue(key, out var name) ? name : null;
    }

    /// <summary>
    /// Full resolution cascade: Tier 1 (weak-ref map) → Tier 2 (hash + manifest) → Tier 3 (anonymous).
    /// Populates the Tier 1 map on Tier 2 hit so subsequent queries skip rehashing.
    /// Hash is always computed (unless a GPU/dispose exception occurs), regardless of tier.
    /// Dimensions are always available on a non-null texture.
    /// </summary>
    /// <returns>
    /// Path is non-null on Tier 1/2 hit; null on Tier 3 (anonymous).
    /// Hash is non-null unless <see cref="TextureHasher.ComputeHashHexPrefix"/> throws
    /// (e.g. GPU render target with no CPU-readable backing). Width/Height are 0 on error.
    /// </returns>
    public (string? Path, string? Hash, int Width, int Height) TryResolveWithFallback(
        Texture2D texture,
        TextureHashManifest manifest)
    {
        // Tier 1 — existing weak-ref lookup. No hash needed if we already know the path.
        var tier1 = TryResolve(texture);
        if (tier1 is not null)
        {
            string? hash1 = null;
            try { hash1 = TextureHasher.ComputeHashHexPrefix(texture); } catch { }
            return (tier1, hash1, texture.Width, texture.Height);
        }

        // Tier 2 — hash + manifest lookup. May throw for GPU-backed render targets.
        string? hash;
        try { hash = TextureHasher.ComputeHashHexPrefix(texture); }
        catch { return (null, null, 0, 0); }

        var resolved = manifest.TryResolve(hash);
        if (resolved is not null)
        {
            RegisterCore(texture, resolved);   // back-fill Tier 1 for future queries
            return (resolved, hash, texture.Width, texture.Height);
        }

        // Tier 3 — anonymous. Return hash + size so filter matching on content_hash / texture_size works.
        return (null, hash, texture.Width, texture.Height);
    }

    // Test hooks — Harness.Tests can reach these via InternalsVisibleTo.
    internal void RegisterShim(object shim, string assetName) => RegisterCore(shim, assetName);
    internal string? TryResolveShim(object shim) => TryResolveCore(shim);
}
