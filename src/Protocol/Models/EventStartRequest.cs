namespace SdvTestFramework.Protocol.Models;

public sealed class EventStartRequest
{
    public string Id { get; set; } = string.Empty;
    public string? Location { get; set; }
}

public sealed class EventStartResult
{
    public bool Ok { get; set; } = true;
    public int Tick { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public sealed class EventSkipResult
{
    public bool Ok { get; set; } = true;
    public int Tick { get; set; }
    public string Id { get; set; } = string.Empty;
}
