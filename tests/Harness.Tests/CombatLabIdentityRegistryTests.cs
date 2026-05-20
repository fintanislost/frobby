using System.Runtime.Serialization;
using SdvTestFramework.Harness.Handlers;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class CombatLabIdentityRegistryTests
{
    [Fact]
    public void Assign_ReturnsStableIdentityForSameMonster()
    {
        CombatLabIdentityRegistry.Clear();
        var monster = FormatterServices.GetUninitializedObject(typeof(StardewValley.Monsters.GreenSlime));

        var first = CombatLabIdentityRegistry.Assign(monster, "target");
        var second = CombatLabIdentityRegistry.Assign(monster, null);
        var renamed = CombatLabIdentityRegistry.Assign(monster, "renamed");

        Assert.Equal("frobby-monster-1", first.MonsterId);
        Assert.Equal("target", first.Label);
        Assert.True(first.SpawnedByFrobby);
        Assert.Equal(first, second);
        Assert.Equal(first.MonsterId, renamed.MonsterId);
        Assert.Equal("renamed", renamed.Label);
        Assert.True(renamed.SpawnedByFrobby);
    }

    [Fact]
    public void Clear_RemovesPreviouslyAssignedIdentity()
    {
        CombatLabIdentityRegistry.Clear();
        var monster = FormatterServices.GetUninitializedObject(typeof(StardewValley.Monsters.GreenSlime));

        CombatLabIdentityRegistry.Assign(monster, "target");
        CombatLabIdentityRegistry.Clear();

        Assert.False(CombatLabIdentityRegistry.TryGet(monster, out _));
        var reassigned = CombatLabIdentityRegistry.Assign(monster, null);
        Assert.Equal("frobby-monster-1", reassigned.MonsterId);
    }
}
