using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Recording;
using StardewModdingAPI;

namespace SdvTestFramework.Harness.Patches;

/// <summary>Harmony prefixes for <see cref="SpriteBatch.DrawString"/> overloads.</summary>
internal static class SpriteBatchDrawStringPatches
{
    private static readonly Dictionary<string, MethodInfo> _prefixes;

    static SpriteBatchDrawStringPatches()
    {
        _prefixes = new Dictionary<string, MethodInfo>();
        foreach (var m in typeof(SpriteBatchDrawStringPatches).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (m.Name.StartsWith("Prefix_", StringComparison.Ordinal))
                _prefixes[SignatureKey(m.GetParameters().Select(p => p.ParameterType))] = m;
        }
    }

    public static void Apply(Harmony harmony, IMonitor monitor)
    {
        var drawStringMethods = AccessTools.GetDeclaredMethods(typeof(SpriteBatch))
            .Where(m => m.Name == "DrawString"
                     && m.GetParameters().Length >= 4
                     && m.GetParameters()[0].ParameterType == typeof(SpriteFont)
                     && (m.GetParameters()[1].ParameterType == typeof(string)
                         || m.GetParameters()[1].ParameterType == typeof(StringBuilder)))
            .ToList();

        if (drawStringMethods.Count == 0)
            throw new InvalidOperationException(
                "SpriteBatch.DrawString(SpriteFont, string/StringBuilder, ...) enumeration returned zero methods — SDV/FNA has changed and the harness must be revised.");

        int patched = 0, unknown = 0;
        foreach (var m in drawStringMethods)
        {
            var key = SignatureKey(m.GetParameters().Select(p => p.ParameterType));
            if (!_prefixes.TryGetValue(key, out var prefix))
            {
                monitor.Log($"Unknown DrawString overload — not patched: {Format(m)}", LogLevel.Warn);
                unknown++;
                continue;
            }

            harmony.Patch(m, prefix: new HarmonyMethod(prefix));
            monitor.Log($"Patched: {Format(m)}", LogLevel.Trace);
            patched++;
        }

        monitor.Log(
            $"SpriteBatch.DrawString prefix coverage: patched {patched}, unknown {unknown}, total-overloads {drawStringMethods.Count}.",
            unknown == 0 ? LogLevel.Info : LogLevel.Warn);
    }

    internal static bool CanPatchForTests(MethodInfo method)
    {
        if (method.Name != "DrawString")
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length < 4 ||
            parameters[0].ParameterType != typeof(SpriteFont) ||
            (parameters[1].ParameterType != typeof(string) &&
             parameters[1].ParameterType != typeof(StringBuilder)))
        {
            return false;
        }

        var key = SignatureKey(parameters.Select(p => p.ParameterType));
        return _prefixes.ContainsKey(key);
    }

    private static string SignatureKey(IEnumerable<Type> types) =>
        string.Join("|", types.Select(t => t.FullName ?? t.Name));

    private static string Format(MethodInfo m) =>
        $"SpriteBatch.DrawString({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})";

    private static void Record(SpriteFont? font, string? text, Vector2 position, Color color, Vector2 scale, float layerDepth)
    {
        if (!Recorder.IsArmed) return;
        var value = text ?? string.Empty;
        var measured = font is null ? Vector2.Zero : font.MeasureString(value) * scale;
        var (tick, call) = Recorder.NextId();
        Recorder.RecordText(new TextDrawEvent
        {
            Tick = tick,
            CallIndex = call,
            Text = value,
            Position = position,
            Size = measured,
            Color = color,
            LayerDepth = layerDepth,
        });
    }

    private static void Prefix_StringBasic(SpriteFont spriteFont, string text, Vector2 position, Color color) =>
        Record(spriteFont, text, position, color, Vector2.One, layerDepth: 0f);

    private static void Prefix_StringScaleFloat(SpriteFont spriteFont, string text, Vector2 position, Color color,
        float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth) =>
        Record(spriteFont, text, position, color, new Vector2(scale, scale), layerDepth);

    private static void Prefix_StringScaleVector(SpriteFont spriteFont, string text, Vector2 position, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth) =>
        Record(spriteFont, text, position, color, scale, layerDepth);

    private static void Prefix_StringBuilderBasic(SpriteFont spriteFont, StringBuilder text, Vector2 position, Color color) =>
        Record(spriteFont, text?.ToString(), position, color, Vector2.One, layerDepth: 0f);

    private static void Prefix_StringBuilderScaleFloat(SpriteFont spriteFont, StringBuilder text, Vector2 position, Color color,
        float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth) =>
        Record(spriteFont, text?.ToString(), position, color, new Vector2(scale, scale), layerDepth);

    private static void Prefix_StringBuilderScaleVector(SpriteFont spriteFont, StringBuilder text, Vector2 position, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth) =>
        Record(spriteFont, text?.ToString(), position, color, scale, layerDepth);
}
