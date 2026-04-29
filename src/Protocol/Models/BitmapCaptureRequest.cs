namespace SdvTestFramework.Protocol.Models;

/// <summary>Request shape shared by bitmap capture RPCs.</summary>
public sealed class BitmapCaptureRequest
{
    public bool AllowUnfrozen { get; set; }
    public int TimeoutMs { get; set; } = 2000;
    public BitmapCaptureRegion? Region { get; set; }
}

public sealed class BitmapCaptureRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
