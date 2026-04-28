using System.Text.Json;

namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Single step in a scenario's <c>steps</c> array. Runtime steps use <see cref="Action"/>;
/// loader-only include steps use <see cref="Include"/> and are expanded before execution.
/// </summary>
public sealed class ScenarioStep
{
    /// <summary>RPC method name, e.g. <c>player.warp</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Relative path to a JSON array of scenario steps. Expanded by <c>ScenarioLoader</c>.</summary>
    public string? Include { get; set; }

    /// <summary>Free-form JSON object passed as the RPC params. May be null when the RPC takes no args.</summary>
    public JsonElement? Args { get; set; }
}
