using System;
using System.Collections.Generic;

namespace SdvTestFramework.Harness.Handlers;

internal static class CombatLabIdentityRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<object, CombatLabMonsterIdentity> Identities = new(ReferenceEqualityComparer.Instance);
    private static int nextId;

    internal sealed record CombatLabMonsterIdentity(string MonsterId, string? Label, bool SpawnedByFrobby);

    internal static CombatLabMonsterIdentity Assign(object monster, string? label, bool spawnedByFrobby = true)
    {
        ArgumentNullException.ThrowIfNull(monster);

        lock (Gate)
        {
            if (Identities.TryGetValue(monster, out var existing))
            {
                if (label is null || string.Equals(existing.Label, label, StringComparison.Ordinal))
                    return existing;

                var renamed = existing with { Label = label };
                Identities[monster] = renamed;
                return renamed;
            }

            var identity = new CombatLabMonsterIdentity(
                $"frobby-monster-{++nextId}",
                label,
                spawnedByFrobby);
            Identities.Add(monster, identity);
            return identity;
        }
    }

    internal static bool TryGet(object monster, out CombatLabMonsterIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(monster);

        lock (Gate)
            return Identities.TryGetValue(monster, out identity!);
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
