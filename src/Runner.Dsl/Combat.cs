using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>combat.*</c> RPC surface.</summary>
public static class Combat
{
    /// <summary>Attack the tile at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static async Task AttackTile(
        int x,
        int y,
        string? qualifiedItemId = null,
        int repeat = 1,
        int delayTicks = 0,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatAttackRequest
        {
            X = x,
            Y = y,
            QualifiedItemId = qualifiedItemId,
            Repeat = repeat,
            DelayTicks = delayTicks,
        }, ProtocolJson.Options);
        await s.InvokeAsync("combat.attack", p, ct);
    }

    /// <summary>Attack in a cardinal <paramref name="direction"/>.</summary>
    public static async Task AttackDirection(
        string direction,
        string? qualifiedItemId = null,
        int repeat = 1,
        int delayTicks = 0,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatAttackRequest
        {
            Direction = direction,
            QualifiedItemId = qualifiedItemId,
            Repeat = repeat,
            DelayTicks = delayTicks,
        }, ProtocolJson.Options);
        await s.InvokeAsync("combat.attack", p, ct);
    }

    /// <summary>Attack a monster selected from current location state by identity or metadata.</summary>
    public static async Task AttackTarget(
        string? monsterId = null,
        string? label = null,
        string? location = null,
        string? type = null,
        string? qualifiedItemId = null,
        int repeat = 1,
        int delayTicks = 0,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatAttackRequest
        {
            QualifiedItemId = qualifiedItemId,
            Repeat = repeat,
            DelayTicks = delayTicks,
            Target = new CombatTargetCriteria
            {
                MonsterId = monsterId,
                Label = label,
                Location = location,
                Type = type,
            },
        }, ProtocolJson.Options);
        await s.InvokeAsync("combat.attack", p, ct);
    }
}
