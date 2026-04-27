using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>draw.*</c> RPC surface.</summary>
public static class Draw
{
    public static async Task Arm(int? ticks = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        JsonElement? p = ticks is null
            ? null
            : JsonSerializer.SerializeToElement(new DrawArmRequest { Ticks = ticks.Value }, ProtocolJson.Options);
        await s.InvokeAsync("draw.arm", p, ct);
    }

    public static async Task Disarm(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        await s.InvokeAsync("draw.disarm", null, ct);
    }

    public static async Task<DrawEventSnapshot> Snapshot(CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resp = await s.InvokeAsync("draw.snapshot", null, ct);
        return JsonSerializer.Deserialize<DrawEventSnapshot>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.snapshot returned null");
    }

    public static async Task<DrawFindResult> Find(DrawFilter filter, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { filter }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("draw.find", p, ct);
        return JsonSerializer.Deserialize<DrawFindResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.find returned null");
    }

    public static async Task<AssertResult> AssertContains(DrawFilter filter, int minCount = 1, string? message = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { filter, min_count = minCount, message }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("draw.assert_contains", p, ct);
        return JsonSerializer.Deserialize<AssertResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.assert_contains returned null");
    }

    public static async Task<AssertResult> AssertNotContains(DrawFilter filter, string? message = null, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new { filter, message }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("draw.assert_not_contains", p, ct);
        return JsonSerializer.Deserialize<AssertResult>(resp, ProtocolJson.Options)
            ?? throw new System.InvalidOperationException("draw.assert_not_contains returned null");
    }
}
