using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class FixtureFreezeWaitTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string, string)> Calls { get; } = new();
        public JsonElement NextResponse { get; set; } = JsonDocument.Parse("{\"ok\":true}").RootElement;
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        { Calls.Add((m, p?.GetRawText() ?? "")); return Task.FromResult(NextResponse); }
    }

    [Fact]
    public async Task FixtureLoad_InvokesWithName()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Fixture.Load("m0spike_436515781"); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("fixture.load", inv.Calls[0].Item1);
        Assert.Contains("m0spike_436515781", inv.Calls[0].Item2);
    }

    [Fact]
    public async Task FreezeBegin_InvokesFreezeBegin()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Freeze.Begin(); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("freeze.begin", inv.Calls[0].Item1);
    }

    [Fact]
    public async Task FreezeEnd_InvokesFreezeEnd()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Freeze.End(); }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("freeze.end", inv.Calls[0].Item1);
    }

    [Fact]
    public async Task WaitMs_DelaysLocallyWithoutRpc()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var sw = Stopwatch.StartNew();
            await Wait.Ms(100);
            sw.Stop();
            Assert.Empty(inv.Calls);
            Assert.True(sw.ElapsedMilliseconds >= 90, $"expected ≥90ms, got {sw.ElapsedMilliseconds}ms");
        }
        finally { SdvTestSession.ResetForTests(); }
    }
}
