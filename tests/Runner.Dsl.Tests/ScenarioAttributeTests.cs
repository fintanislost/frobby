using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class ScenarioAttributeTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((m, p?.GetRawText() ?? ""));
            return Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone());
        }
    }

    // Dummy method surface for reflection — the attribute's Before/After take a MethodInfo.
    private static void DummyTestMethod() { }

    [Fact]
    public void Before_InvokesScenarioBeginWithNameSeedFixture()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var attr = new ScenarioAttribute(name: "my_scenario", seed: 42, fixture: "m0spike");
            var mi = typeof(ScenarioAttributeTests).GetMethod(nameof(DummyTestMethod), BindingFlags.NonPublic | BindingFlags.Static)!;
            attr.Before(mi);

            Assert.Single(inv.Calls);
            Assert.Equal("scenario.begin", inv.Calls[0].Method);
            Assert.Contains("\"name\":\"my_scenario\"", inv.Calls[0].ParamsJson);
            Assert.Contains("\"seed\":42", inv.Calls[0].ParamsJson);
            Assert.Contains("\"fixture\":\"m0spike\"", inv.Calls[0].ParamsJson);
        }
        finally { SdvTestSession.ResetForTests(); }
    }

    [Fact]
    public void After_InvokesScenarioEnd()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var attr = new ScenarioAttribute();
            var mi = typeof(ScenarioAttributeTests).GetMethod(nameof(DummyTestMethod), BindingFlags.NonPublic | BindingFlags.Static)!;
            attr.After(mi);

            Assert.Equal("scenario.end", inv.Calls[0].Method);
        }
        finally { SdvTestSession.ResetForTests(); }
    }
}
