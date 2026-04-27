using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit.Sdk;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// Wraps a test method in <c>scenario.begin</c> / <c>scenario.end</c>. Apply alongside
/// <c>[Fact]</c> on any test in a <c>[Collection("SDV")]</c>-decorated class.
/// </summary>
/// <remarks>
/// Because xUnit's <see cref="BeforeAfterTestAttribute"/> is purely a lifecycle hook (not
/// a test-discoverer), users still need <c>[Fact]</c> on the method itself. A combined
/// <c>[ScenarioFact]</c> attribute is deferred to M4.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScenarioAttribute : BeforeAfterTestAttribute
{
    public string? Name { get; }
    public int Seed { get; }
    public string? Fixture { get; }

    public ScenarioAttribute(string? name = null, int seed = 42, string? fixture = null)
    {
        Name = name;
        Seed = seed;
        Fixture = fixture;
    }

    public override void Before(MethodInfo methodUnderTest)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var resolvedName = Name ?? methodUnderTest.Name;
        // Set the scenario name before the begin RPC so any DSL facet invoked inside
        // the test body (e.g. Screenshot.Capture) can route output into the right subdir.
        s.CurrentScenarioName = resolvedName;
        var req = new ScenarioBeginRequest
        {
            Name = resolvedName,
            Seed = Seed,
            Fixture = Fixture,
        };
        var p = JsonSerializer.SerializeToElement(req, ProtocolJson.Options);
        // Block the Before hook on the RPC — the framework needs the scenario established
        // before the test body runs. GetAwaiter().GetResult() is OK here; xUnit's hook
        // machinery is synchronous.
        s.InvokeAsync("scenario.begin", p, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override void After(MethodInfo methodUnderTest)
    {
        var s = SdvTestSession.Current;
        if (s is null) return;   // session torn down already; nothing to do.
        try
        {
            s.InvokeAsync("scenario.end", null, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Swallow teardown errors — xUnit is already about to report the test's
            // outcome; don't mask it with a cleanup exception.
        }
        // Clear the scenario name after teardown so subsequent DSL calls outside a
        // [Scenario] fail loudly instead of silently writing into a stale subdir.
        s.CurrentScenarioName = null;
    }
}
