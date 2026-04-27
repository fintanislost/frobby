using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.SpikeHarness.Recording;
using StardewModdingAPI;

namespace SdvTestFramework.SpikeHarness.Patches;

/// <summary>
/// Harmony prefixes for every <see cref="SpriteBatch.Draw"/> overload.
/// </summary>
/// <remarks>
/// Enumerate overloads at runtime (per .claude/rules/draw-call-recorder.md) and dispatch to
/// an explicit per-overload prefix by parameter-type signature. If SDV/FNA introduces a new
/// overload we don't know about we warn loud and leave it unpatched — capture will have a
/// coverage gap that the spike's diff will surface.
/// </remarks>
// Patch: SpriteBatch.Draw(…) — all 7 known overloads
// Type: Prefix (non-modifying, records side effect)
// Why: Capture draw events for assertion queries (spec §4.2); M0 spike, deliverable D0.1
// Rollback: Remove call to Apply() from ModEntry; recorder falls back to inert mode
// Tested in: docs/spikes/2026-04-determinism/scratch/run.sh (integration only; no xUnit for spike)
// Depends on: Harmony 2.x (bundled with SMAPI), SMAPI >= 4.1.10
internal static class SpriteBatchDrawPatches
{
    private static readonly Dictionary<string, MethodInfo> _prefixes;

    static SpriteBatchDrawPatches()
    {
        _prefixes = new Dictionary<string, MethodInfo>();
        foreach (var m in typeof(SpriteBatchDrawPatches).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (m.Name.StartsWith("Prefix_", StringComparison.Ordinal))
                _prefixes[SignatureKey(m.GetParameters().Select(p => p.ParameterType))] = m;
        }
    }

    public static void Apply(Harmony harmony, IMonitor monitor)
    {
        var drawMethods = AccessTools.GetDeclaredMethods(typeof(SpriteBatch))
            .Where(m => m.Name == "Draw"
                     && m.GetParameters().Length > 0
                     && m.GetParameters()[0].ParameterType == typeof(Texture2D))
            .ToList();

        if (drawMethods.Count == 0)
            throw new InvalidOperationException(
                "SpriteBatch.Draw(Texture2D, ...) enumeration returned zero methods — SDV/FNA has changed and the spike must be revised.");

        int patched = 0, unknown = 0;
        foreach (var m in drawMethods)
        {
            var key = SignatureKey(m.GetParameters().Select(p => p.ParameterType));
            if (!_prefixes.TryGetValue(key, out var prefix))
            {
                monitor.Log($"Unknown Draw overload — not patched: {Format(m)}", LogLevel.Warn);
                unknown++;
                continue;
            }
            harmony.Patch(m, prefix: new HarmonyMethod(prefix));
            monitor.Log($"Patched: {Format(m)}", LogLevel.Trace);
            patched++;
        }

        monitor.Log(
            $"SpriteBatch.Draw prefix coverage: patched {patched}, unknown {unknown}, total-overloads {drawMethods.Count}.",
            unknown == 0 ? LogLevel.Info : LogLevel.Warn);
    }

    private static string SignatureKey(IEnumerable<Type> types) =>
        string.Join("|", types.Select(t => t.FullName ?? t.Name));

    private static string Format(MethodInfo m) =>
        $"SpriteBatch.Draw({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})";

    // ---------------- per-overload prefixes ----------------
    //
    // Each prefix records, nothing else. They must be `void` returning no bool: the patched
    // method always runs. This is the safest Harmony style — `harmony-patching.md §patch
    // type preferences` forbids `Prefix + return false` here.

    // 1: Draw(Texture2D, Vector2, Color)
    private static void Prefix_1(Texture2D texture, Vector2 position, Color color)
    {
        if (!Recorder.IsArmed) return;
        var (tick, call) = Recorder.NextId();
        Recorder.Record(new DrawEvent
        {
            Tick = tick,
            CallIndex = call,
            TextureRefId = RuntimeHelpers.GetHashCode(texture),
            TextureWidth = texture.Width,
            TextureHeight = texture.Height,
            SourceRect = null,
            DestRect = new Rectangle((int)position.X, (int)position.Y, texture.Width, texture.Height),
            Color = color,
            Rotation = 0f,
            Origin = Vector2.Zero,
            Effects = SpriteEffects.None,
            LayerDepth = 0f,
        });
    }

    // 2: Draw(Texture2D, Vector2, Rectangle?, Color)
    private static void Prefix_2(Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color)
    {
        if (!Recorder.IsArmed) return;
        var (tick, call) = Recorder.NextId();
        int w = sourceRectangle?.Width ?? texture.Width;
        int h = sourceRectangle?.Height ?? texture.Height;
        Recorder.Record(new DrawEvent
        {
            Tick = tick, CallIndex = call,
            TextureRefId = RuntimeHelpers.GetHashCode(texture),
            TextureWidth = texture.Width, TextureHeight = texture.Height,
            SourceRect = sourceRectangle,
            DestRect = new Rectangle((int)position.X, (int)position.Y, w, h),
            Color = color,
            Rotation = 0f, Origin = Vector2.Zero,
            Effects = SpriteEffects.None, LayerDepth = 0f,
        });
    }

    // 3: Draw(Texture2D, Vector2, Rectangle?, Color, float, Vector2, float, SpriteEffects, float)
    private static void Prefix_3(Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
        Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
    {
        if (!Recorder.IsArmed) return;
        var (tick, call) = Recorder.NextId();
        int sw = sourceRectangle?.Width ?? texture.Width;
        int sh = sourceRectangle?.Height ?? texture.Height;
        Recorder.Record(new DrawEvent
        {
            Tick = tick, CallIndex = call,
            TextureRefId = RuntimeHelpers.GetHashCode(texture),
            TextureWidth = texture.Width, TextureHeight = texture.Height,
            SourceRect = sourceRectangle,
            DestRect = new Rectangle((int)position.X, (int)position.Y, (int)(sw * scale), (int)(sh * scale)),
            Color = color,
            Rotation = rotation, Origin = origin,
            Effects = effects, LayerDepth = layerDepth,
        });
    }

    // 4: Draw(Texture2D, Vector2, Rectangle?, Color, float, Vector2, Vector2, SpriteEffects, float)
    private static void Prefix_4(Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
        Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
    {
        if (!Recorder.IsArmed) return;
        var (tick, call) = Recorder.NextId();
        int sw = sourceRectangle?.Width ?? texture.Width;
        int sh = sourceRectangle?.Height ?? texture.Height;
        Recorder.Record(new DrawEvent
        {
            Tick = tick, CallIndex = call,
            TextureRefId = RuntimeHelpers.GetHashCode(texture),
            TextureWidth = texture.Width, TextureHeight = texture.Height,
            SourceRect = sourceRectangle,
            DestRect = new Rectangle((int)position.X, (int)position.Y, (int)(sw * scale.X), (int)(sh * scale.Y)),
            Color = color,
            Rotation = rotation, Origin = origin,
            Effects = effects, LayerDepth = layerDepth,
        });
    }

    // 5: Draw(Texture2D, Rectangle, Color)
    private static void Prefix_5(Texture2D texture, Rectangle destinationRectangle, Color color)
    {
        if (!Recorder.IsArmed) return;
        var (tick, call) = Recorder.NextId();
        Recorder.Record(new DrawEvent
        {
            Tick = tick, CallIndex = call,
            TextureRefId = RuntimeHelpers.GetHashCode(texture),
            TextureWidth = texture.Width, TextureHeight = texture.Height,
            SourceRect = null,
            DestRect = destinationRectangle,
            Color = color,
            Rotation = 0f, Origin = Vector2.Zero,
            Effects = SpriteEffects.None, LayerDepth = 0f,
        });
    }

    // 6: Draw(Texture2D, Rectangle, Rectangle?, Color)
    private static void Prefix_6(Texture2D texture, Rectangle destinationRectangle,
        Rectangle? sourceRectangle, Color color)
    {
        if (!Recorder.IsArmed) return;
        var (tick, call) = Recorder.NextId();
        Recorder.Record(new DrawEvent
        {
            Tick = tick, CallIndex = call,
            TextureRefId = RuntimeHelpers.GetHashCode(texture),
            TextureWidth = texture.Width, TextureHeight = texture.Height,
            SourceRect = sourceRectangle,
            DestRect = destinationRectangle,
            Color = color,
            Rotation = 0f, Origin = Vector2.Zero,
            Effects = SpriteEffects.None, LayerDepth = 0f,
        });
    }

    // 7: Draw(Texture2D, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float)
    private static void Prefix_7(Texture2D texture, Rectangle destinationRectangle,
        Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin,
        SpriteEffects effects, float layerDepth)
    {
        if (!Recorder.IsArmed) return;
        var (tick, call) = Recorder.NextId();
        Recorder.Record(new DrawEvent
        {
            Tick = tick, CallIndex = call,
            TextureRefId = RuntimeHelpers.GetHashCode(texture),
            TextureWidth = texture.Width, TextureHeight = texture.Height,
            SourceRect = sourceRectangle,
            DestRect = destinationRectangle,
            Color = color,
            Rotation = rotation, Origin = origin,
            Effects = effects, LayerDepth = layerDepth,
        });
    }
}
