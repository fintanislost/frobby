using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

public sealed class LifecycleScenarioAssertionRpc : IScenarioAssertionRpc
{
    private readonly SdvLifecycle _lifecycle;

    public LifecycleScenarioAssertionRpc(SdvLifecycle lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public async Task<ScenarioAssertionRpcResult> InvokeAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _lifecycle.InvokeAsync(method, parameters, cancellationToken);
            return ScenarioAssertionRpcResult.Success(result);
        }
        catch (SdvRpcException ex)
        {
            return ScenarioAssertionRpcResult.Failure(ex.Message);
        }
    }
}
