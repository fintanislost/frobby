using System.Text.Json;

namespace SdvTestFramework.Runner.Mcp.Scenarios;

public readonly record struct ScenarioAssertionRpcResult(JsonElement? Result, string? Error)
{
    public bool Succeeded => Error is null;

    public static ScenarioAssertionRpcResult Success(JsonElement result)
        => new(result.Clone(), Error: null);

    public static ScenarioAssertionRpcResult Failure(string error)
        => new(Result: null, Error: error);
}
