using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Mcp.Scenarios;

namespace SdvTestFramework.Runner.Scenarios;

internal sealed class JsonRpcSessionScenarioAssertionRpc : IScenarioAssertionRpc
{
    private readonly JsonRpcSession _session;

    public JsonRpcSessionScenarioAssertionRpc(JsonRpcSession session)
    {
        _session = session;
    }

    public async Task<ScenarioAssertionRpcResult> InvokeAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        var response = await _session.InvokeAsync(method, parameters, cancellationToken);
        if (response.Error is { } error)
            return ScenarioAssertionRpcResult.Failure(error.Message);

        return ScenarioAssertionRpcResult.Success(response.Result ?? JsonSerializer.SerializeToElement(new { }));
    }
}
