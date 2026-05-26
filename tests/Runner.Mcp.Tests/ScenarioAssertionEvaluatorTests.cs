using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Mcp.Scenarios;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class ScenarioAssertionEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_StateEqualityFailureReportsActualValue()
    {
        var evaluator = new ScenarioAssertionEvaluator(new StubAssertionRpc(
            "{\"held_item\":{\"qualified_id\":\"(O)Parsnip Seeds\"}}"));

        var result = await evaluator.EvaluateAsync(
            new ScenarioAssertion
            {
                Type = "state",
                Expr = "state.player.held_item.qualified_id == '(O)Tulip'",
            },
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal(
            "state.player.held_item.qualified_id did not match '(O)Tulip' (actual: \"(O)Parsnip Seeds\")",
            result.Detail);
    }

    private sealed class StubAssertionRpc : IScenarioAssertionRpc
    {
        private readonly JsonElement _result;

        public StubAssertionRpc(string resultJson)
        {
            _result = JsonDocument.Parse(resultJson).RootElement.Clone();
        }

        public Task<ScenarioAssertionRpcResult> InvokeAsync(
            string method,
            JsonElement? parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(ScenarioAssertionRpcResult.Success(_result));
    }
}
