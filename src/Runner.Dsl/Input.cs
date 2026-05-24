using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>input.*</c> RPC surface.</summary>
public static class Input
{
    /// <summary>Send a MonoGame key press to the active menu.</summary>
    public static async Task Key(string key, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new InputKeyRequest { Key = key }, ProtocolJson.Options);
        await s.InvokeAsync("input.key", p, ct);
    }

    /// <summary>Send text to the active menu.</summary>
    public static async Task Text(string text, bool submit = false, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(
            new InputTextRequest { Text = text, Submit = submit },
            ProtocolJson.Options);
        await s.InvokeAsync("input.text", p, ct);
    }

    /// <summary>Click the active menu at screen coordinates.</summary>
    public static async Task Click(int x, int y, string button = "left", CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(
            new InputClickRequest { X = x, Y = y, Button = button },
            ProtocolJson.Options);
        await s.InvokeAsync("input.click", p, ct);
    }

    /// <summary>Click a gameplay tile through Stardew's native left-click path.</summary>
    public static async Task<InputClickTileResult> ClickTile(
        int x,
        int y,
        string? location = null,
        bool requireCurrentLocation = true,
        int screenOffsetX = 32,
        int screenOffsetY = 32,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new InputClickTileRequest
        {
            Location = location,
            X = x,
            Y = y,
            RequireCurrentLocation = requireCurrentLocation,
            ScreenOffsetX = screenOffsetX,
            ScreenOffsetY = screenOffsetY,
        }, ProtocolJson.Options);
        var resp = await s.InvokeAsync("input.click_tile", p, ct);
        return JsonSerializer.Deserialize<InputClickTileResult>(resp, ProtocolJson.Options)
            ?? throw new SdvRpcException("input.click_tile", Protocol.JsonRpcErrorCode.InternalError,
                "empty input.click_tile response");
    }

    /// <summary>Move the deterministic cursor and send hover to the active menu at screen coordinates.</summary>
    public static async Task Hover(int x, int y, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(
            new InputHoverRequest { X = x, Y = y },
            ProtocolJson.Options);
        await s.InvokeAsync("input.hover", p, ct);
    }

    /// <summary>Click the center of a captured text draw event in the active menu.</summary>
    public static async Task ClickText(
        string text,
        string button = "left",
        bool caseSensitive = true,
        int occurrence = 1,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(
            new InputClickTextRequest
            {
                Text = text,
                Button = button,
                CaseSensitive = caseSensitive,
                Occurrence = occurrence,
            },
            ProtocolJson.Options);
        await s.InvokeAsync("input.click_text", p, ct);
    }

    /// <summary>Hover the center of a captured text draw event in the active menu.</summary>
    public static async Task HoverText(
        string text,
        bool caseSensitive = true,
        int occurrence = 1,
        CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(
            new InputHoverTextRequest
            {
                Text = text,
                CaseSensitive = caseSensitive,
                Occurrence = occurrence,
            },
            ProtocolJson.Options);
        await s.InvokeAsync("input.hover_text", p, ct);
    }
}
