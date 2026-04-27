namespace SdvTestFramework.Protocol.Models;

/// <summary>
/// Request shape of <c>time.advance</c>. Field names deserialize from snake_case via
/// <see cref="Json.ProtocolJson.Options"/>.
/// </summary>
public sealed class TimeAdvanceRequest
{
    /// <summary>
    /// Minutes of in-game time to advance. Must be a multiple of 10 between 10 and 120
    /// inclusive — SDV's clock advances in 10-minute chunks; scenarios chain calls for
    /// longer advances.
    /// </summary>
    public int Minutes { get; set; }
}
