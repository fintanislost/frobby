using System.Text.Json;

namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Single step in a scenario's <c>steps</c> array. <see cref="Args"/> is free-form JSON forwarded
/// to the RPC method named by <see cref="Action"/> — e.g., <c>player.warp</c>.
/// </summary>
public sealed class ScenarioStep
{
    /// <summary>RPC method name, e.g. <c>player.warp</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Free-form JSON object passed as the RPC params. May be null when the RPC takes no args.</summary>
    public JsonElement? Args { get; set; }
}
