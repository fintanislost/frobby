using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the test-only Frobby Combat Lab.</summary>
public static class CombatLab
{
    public static async Task<CombatLabResetResult> Reset(
        int playerX = 8,
        int playerY = 8,
        int width = 20,
        int height = 14,
        bool warpPlayer = true,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatLabResetRequest
        {
            PlayerX = playerX,
            PlayerY = playerY,
            Width = width,
            Height = height,
            WarpPlayer = warpPlayer,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("combat_lab.reset", p, ct);
        return JsonSerializer.Deserialize<CombatLabResetResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException(
                "combat_lab.reset",
                JsonRpcErrorCode.InternalError,
                "empty combat_lab.reset response");
    }

    public static async Task<CombatLabSpawnMonsterResult> SpawnMonster(
        string kind,
        string? label,
        int x,
        int y,
        int? health = null,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new CombatLabSpawnMonsterRequest
        {
            Kind = kind,
            Label = label,
            X = x,
            Y = y,
            Health = health,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("combat_lab.spawn_monster", p, ct);
        return JsonSerializer.Deserialize<CombatLabSpawnMonsterResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException(
                "combat_lab.spawn_monster",
                JsonRpcErrorCode.InternalError,
                "empty combat_lab.spawn_monster response");
    }
}
