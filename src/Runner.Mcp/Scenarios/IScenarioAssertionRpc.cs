using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

public interface IScenarioAssertionRpc
{
    Task<ScenarioAssertionRpcResult> InvokeAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken);
}
