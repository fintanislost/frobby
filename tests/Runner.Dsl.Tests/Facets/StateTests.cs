using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class StateTests
{
    private sealed class StubInvoker : ISdvTestInvoker
    {
        public string? LastMethod { get; private set; }
        public string? LastParams { get; private set; }
        public string NextJson { get; set; } = "{}";
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            LastMethod = m;
            LastParams = p?.GetRawText();
            return Task.FromResult(JsonDocument.Parse(NextJson).RootElement.Clone());
        }
    }

    [Fact]
    public async Task Player_InvokesStatePlayerAndDeserializes()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker
        {
            NextJson = "{\"name\":\"Alice\",\"money\":5000,\"stamina\":200,\"max_stamina\":270,\"health\":100,\"location\":\"Farm\",\"tile\":{\"x\":64,\"y\":15}}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var p = await State.Player();
            Assert.Equal("state.player", inv.LastMethod);
            Assert.Equal("Alice", p.Name);
            Assert.Equal(5000, p.Money);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public async Task Npc_InvokesStateNpcWithName()
    {
        SdvTestSession.ResetForTests();
        var inv = new StubInvoker { NextJson = "{\"name\":\"Pierre\"}" };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var n = await State.Npc("Pierre");
            Assert.Equal("state.npc", inv.LastMethod);
            Assert.Contains("\"name\":\"Pierre\"", inv.LastParams);
            Assert.Equal("Pierre", n.Name);
        }
        finally { SdvTestSession.ResetForTests(); }
    }
}
