using System.Text.Json;
using System.Reflection;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>Handler for <c>freeze.status</c>. Pure query — no preconditions.</summary>
public static class FreezeStatusHandler
{
    public const string Method = "freeze.status";

    public static JsonElement Handle(JsonElement? paramsElement)
    {
        return ProtocolJson.ToElement(new FreezeStatusResult
        {
            Frozen = DeterminismController.Frozen,
            IsWarping = Game1.isWarping,
            IsFading = GameFadeState.IsFading,
            Tick = Game1.ticks,
        });
    }
}

internal static class GameFadeState
{
    private const BindingFlags StaticMemberFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    public static bool IsFading
        => ReadBool("fadeToBlack")
           || ReadBool("globalFade")
           || ReadFloat("fadeToBlackAlpha") > 0.001f;

    private static bool ReadBool(string name)
    {
        try
        {
            var member = FindPropertyOrField(name);
            var value = member switch
            {
                PropertyInfo property when property.PropertyType == typeof(bool)
                    => property.GetValue(null),
                FieldInfo field when field.FieldType == typeof(bool)
                    => field.GetValue(null),
                _ => null,
            };

            return value is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    private static float ReadFloat(string name)
    {
        try
        {
            var member = FindPropertyOrField(name);
            var value = member switch
            {
                PropertyInfo property when property.PropertyType == typeof(float)
                    => property.GetValue(null),
                FieldInfo field when field.FieldType == typeof(float)
                    => field.GetValue(null),
                _ => null,
            };

            return value is float f ? f : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private static MemberInfo? FindPropertyOrField(string name)
        => typeof(Game1).GetProperty(name, StaticMemberFlags)
           ?? (MemberInfo?)typeof(Game1).GetField(name, StaticMemberFlags);
}
