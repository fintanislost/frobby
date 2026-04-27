using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Result of a <see cref="FixtureBuilder.BuildAsync"/> run.</summary>
public sealed class FixtureBuildResult
{
    public bool Success { get; set; }
    public string SdvVersion { get; set; } = string.Empty;
    public string SmapiVersion { get; set; } = string.Empty;
    public string[] Mods { get; set; } = Array.Empty<string>();
    public string FarmerName { get; set; } = string.Empty;
    public string FarmerGender { get; set; } = string.Empty;
    public string SavePath { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Orchestrator: given a parsed <see cref="FixtureSpec"/> and a connected
/// <see cref="JsonRpcSession"/>, runs the build flow (load base → steps → capture env →
/// save → populate result).
/// </summary>
public static class FixtureBuilder
{
    public static async Task<FixtureBuildResult> BuildAsync(
        FixtureSpec spec, JsonRpcSession session, CancellationToken ct)
    {
        var result = new FixtureBuildResult();
        try
        {
            // 1. load base (skip if null — the root fixture has no base)
            if (!string.IsNullOrEmpty(spec.Base))
            {
                var loadReq = JsonSerializer.SerializeToElement(
                    new FixtureLoadRequest { Name = spec.Base }, ProtocolJson.Options);
                var loadResp = await session.InvokeAsync("fixture.load", loadReq, ct);
                if (loadResp.Error is { } le)
                    throw new InvalidOperationException($"fixture.load failed: {le.Message}");

                // Poll state.player until location is populated — same wait-for-ready
                // logic as ScenarioRunner.WaitForWorldReady.
                await WaitForWorldReadyAsync(session, ct);
            }

            // 2. steps
            foreach (var step in spec.Steps)
            {
                var resp = await session.InvokeAsync(step.Action, step.Args, ct);
                if (resp.Error is { } e)
                    throw new InvalidOperationException($"step '{step.Action}' failed: {e.Message}");
            }

            // 3. capture environment for metadata (BEFORE save, so state.player reflects
            //    the post-steps farmer state, not the post-save reset-to-next-day state).
            var playerResp = await session.InvokeAsync("state.player", params_: null, ct);
            if (playerResp.Result is { } pr)
            {
                result.FarmerName = pr.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                // state.player doesn't currently return gender; leave blank for now.
                // A future PlayerState extension would populate this.
            }
            var modsResp = await session.InvokeAsync("state.mods", params_: null, ct);
            if (modsResp.Result is { } mr && mr.TryGetProperty("mods", out var modsEl))
            {
                var count = modsEl.GetArrayLength();
                var mods = new string[count];
                for (int i = 0; i < count; i++) mods[i] = modsEl[i].GetString() ?? "";
                result.Mods = mods;
            }

            // 4. SDV + SMAPI versions — hardcoded to the currently-pinned versions.
            //    If the protocol adds a handshake getter later, swap these in.
            result.SdvVersion = "1.6.15";
            result.SmapiVersion = "4.5.2";

            // 5. save
            var saveReq = JsonSerializer.SerializeToElement(
                new FixtureSaveRequest { Name = spec.Name }, ProtocolJson.Options);
            var saveResp = await session.InvokeAsync("fixture.save", saveReq, ct);
            if (saveResp.Error is { } se)
                throw new InvalidOperationException($"fixture.save failed: {se.Message}");
            if (saveResp.Result is { } sr && sr.TryGetProperty("save_path", out var sp))
                result.SavePath = sp.GetString() ?? "";

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    private static async Task WaitForWorldReadyAsync(JsonRpcSession session, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var resp = await session.InvokeAsync("state.player", params_: null, ct);
            if (resp.Result is { } r
                && r.TryGetProperty("location", out var loc)
                && loc.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(loc.GetString()))
                return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("world never became ready after fixture.load");
    }
}
