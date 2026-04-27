using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

public class SdvTestSessionTests
{
    [Fact]
    public void Current_NotInitialized_IsNull()
    {
        // Explicit reset in case a prior test left state. SdvTestSession is a singleton
        // accessor; production only initializes it via SdvFixture.
        SdvTestSession.ResetForTests();
        Assert.Null(SdvTestSession.Current);
    }

    [Fact]
    public async Task InvokeAsync_ForwardsToSession()
    {
        var fake = new FakeSession();
        SdvTestSession.InitializeForTests(fake);
        try
        {
            var paramsJson = JsonSerializer.SerializeToElement(new { name = "x" });
            await SdvTestSession.Current!.InvokeAsync("test.method", paramsJson, CancellationToken.None);

            Assert.Equal("test.method", fake.LastMethod);
        }
        finally
        {
            SdvTestSession.ResetForTests();
        }
    }

    // Minimal session seam — tests hand-build this; production uses the real JsonRpcSession.
    private sealed class FakeSession : ISdvTestInvoker
    {
        public string? LastMethod { get; private set; }

        public Task<JsonElement> InvokeAsync(string method, JsonElement? @params, CancellationToken ct)
        {
            LastMethod = method;
            // Return an empty object; real handlers' responses are verified in facet tests.
            return Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone());
        }
    }
}
