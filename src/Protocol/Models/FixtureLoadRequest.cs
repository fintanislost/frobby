namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>fixture.load</c>. <see cref="Name"/> is the save folder name passed
/// to <c>SaveGame.getLoadEnumerator</c>; deserializes from snake_case via
/// <see cref="Json.ProtocolJson.Options"/>.
/// </summary>
public sealed class FixtureLoadRequest
{
    /// <summary>Save folder name (not a full path) — e.g. <c>spring_day_1_clean</c>.</summary>
    public string Name { get; set; } = string.Empty;
}
