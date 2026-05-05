namespace SdvTestFramework.Protocol.Models;

/// <summary>Response for <c>draw.assert_contains</c>.</summary>
public sealed class AssertResult
{
    public bool Passed { get; set; }
    public int MatchedCount { get; set; }
    public int MinCount { get; set; }
    public int? MaxCount { get; set; }
    public string? Message { get; set; }
}
