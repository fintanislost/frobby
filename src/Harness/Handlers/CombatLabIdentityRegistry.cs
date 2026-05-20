using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SdvTestFramework.Harness.Handlers;

internal static class CombatLabIdentityRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, CombatLabMonsterIdentity> Identities = new();
    private static int nextId;

    internal sealed record CombatLabMonsterIdentity(string MonsterId, string? Label, bool SpawnedByFrobby);

    internal static CombatLabMonsterIdentity Assign(object monster, string? label)
    {
        ArgumentNullException.ThrowIfNull(monster);

        var key = RuntimeHelpers.GetHashCode(monster);
        lock (Gate)
        {
            if (Identities.TryGetValue(key, out var existing))
            {
                if (label is null || string.Equals(existing.Label, label, StringComparison.Ordinal))
                    return existing;

                var renamed = existing with { Label = label };
                Identities[key] = renamed;
                return renamed;
            }

            var identity = new CombatLabMonsterIdentity(
                $"frobby-monster-{++nextId}",
                label,
                SpawnedByFrobby: true);
            Identities.Add(key, identity);
            return identity;
        }
    }

    internal static bool TryGet(object monster, out CombatLabMonsterIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(monster);

        var key = RuntimeHelpers.GetHashCode(monster);
        lock (Gate)
            return Identities.TryGetValue(key, out identity!);
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Identities.Clear();
            nextId = 0;
        }
    }
}
