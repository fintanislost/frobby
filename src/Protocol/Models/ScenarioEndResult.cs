namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Success-response DTO for <c>scenario.end</c>. <see cref="DurationMs"/> is measured wall-clock
/// from begin; <see cref="AssertionsRun"/> and <see cref="AssertionsPassed"/> are running tallies
/// maintained on <see cref="SdvTestFramework.Harness.Scenarios.ScenarioState"/> during the session.
/// </summary>
public sealed class ScenarioEndResult
{
    public int DurationMs { get; set; }
    public int AssertionsRun { get; set; }
    public int AssertionsPassed { get; set; }
}
