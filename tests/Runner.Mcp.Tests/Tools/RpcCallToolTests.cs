using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class RpcCallToolTests
{
    private sealed class FakeLifecycle : SdvLifecycle
    {
        public string? LastMethod { get; private set; }
        public string? LastParams { get; private set; }
        public JsonElement NextResult { get; set; } = JsonDocument.Parse("{\"ok\":true}").RootElement;
        public JsonRpcError? NextError { get; set; }

        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            LastMethod = method;
            LastParams = p?.GetRawText();
            if (NextError is { } e) throw SdvRpcException.Create(method, e);
            return Task.FromResult(NextResult);
        }
    }

    [Fact]
    public async Task Dispatch_ForwardsToSession()
    {
        var life = new FakeLifecycle { NextResult = JsonDocument.Parse("{\"tick\":42}").RootElement };
        var tool = new RpcCallTool();
        var args = JsonDocument.Parse("{\"method\":\"state.player\",\"params\":{}}").RootElement;

        var result = await tool.InvokeAsync(args, life, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("\"tick\":42", result.Text);
        Assert.Equal("state.player", life.LastMethod);
    }

    [Fact]
    public async Task Error_MapsToMcpError()
    {
        var life = new FakeLifecycle
        {
            NextError = new JsonRpcError(JsonRpcErrorCode.GameStateInvalid, "no scenario"),
        };
        var tool = new RpcCallTool();
        var args = JsonDocument.Parse("{\"method\":\"freeze.begin\"}").RootElement;

        var result = await tool.InvokeAsync(args, life, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("freeze.begin", result.Text);
        Assert.Contains("no scenario", result.Text);
    }
}
