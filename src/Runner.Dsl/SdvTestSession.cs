using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// Lightweight abstraction over "send this RPC, await the result" — lets the DSL's
/// ambient facets run against either a real <see cref="JsonRpcSession"/> or a test shim
/// without knowing the difference.
/// </summary>
public interface ISdvTestInvoker
{
    Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct);
}

/// <summary>
/// Ambient accessor for the per-assembly SDV session. Populated by <see cref="SdvFixture"/>
/// at xUnit collection-fixture startup; read by every DSL facet (<c>Player</c>, <c>Draw</c>, etc.).
/// </summary>
/// <remarks>
/// Static because xUnit tests in the same <c>[Collection]</c> don't run in parallel, so a
/// simple static accessor is thread-safe within the collection's execution window. Users in
/// multiple parallel collections would need one session per collection — not yet supported;
/// see the design spec's out-of-scope list.
/// </remarks>
public sealed class SdvTestSession
{
    private static SdvTestSession? _current;

    /// <summary>Ambient session; null before <see cref="SdvFixture"/> initializes it.</summary>
    public static SdvTestSession? Current => _current;

    private readonly ISdvTestInvoker _invoker;

    /// <summary>Per-test-assembly run report directory; null if not configured.</summary>
    public RunDirectory? ReportDir { get; set; }

    /// <summary>Name of the currently-executing scenario (set by [Scenario] Before, cleared in After).
    /// Used by facets like <c>Screenshot</c> to route output into the per-scenario subdir.</summary>
    public string? CurrentScenarioName { get; set; }

    private SdvTestSession(ISdvTestInvoker invoker) => _invoker = invoker;

    /// <summary>Production initialization wrapping a real <see cref="JsonRpcSession"/>.</summary>
    public static SdvTestSession Initialize(JsonRpcSession session)
    {
        if (_current != null)
            throw new InvalidOperationException("SdvTestSession.Current is already initialized");
        _current = new SdvTestSession(new SessionInvoker(session));
        return _current;
    }

    /// <summary>Test-only initialization with a custom invoker (shim).</summary>
    internal static SdvTestSession InitializeForTests(ISdvTestInvoker invoker)
    {
        _current = new SdvTestSession(invoker);
        return _current;
    }

    /// <summary>Tear down; used by production fixture dispose + tests.</summary>
    public static void ResetForTests() => _current = null;

    /// <summary>Invoke an RPC method; throws typed <see cref="SdvRpcException"/> on error.</summary>
    public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        => _invoker.InvokeAsync(method, @params, ct);

    // Internal adapter that wraps JsonRpcSession + translates errors to typed exceptions.
    private sealed class SessionInvoker : ISdvTestInvoker
    {
        private readonly JsonRpcSession _session;
        public SessionInvoker(JsonRpcSession session) => _session = session;

        public async Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            var resp = await _session.InvokeAsync(method, @params, ct);
            if (resp.Error is { } e)
                throw SdvRpcException.Create(method, e);
            return resp.Result ?? JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}
