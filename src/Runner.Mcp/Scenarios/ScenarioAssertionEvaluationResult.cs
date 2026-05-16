namespace SdvTestFramework.Runner.Mcp.Scenarios;

public readonly record struct ScenarioAssertionEvaluationResult(bool Passed, string? Detail)
{
    public static ScenarioAssertionEvaluationResult Pass()
        => new(Passed: true, Detail: null);

    public static ScenarioAssertionEvaluationResult Fail(string? detail)
        => new(Passed: false, Detail: detail);
}
