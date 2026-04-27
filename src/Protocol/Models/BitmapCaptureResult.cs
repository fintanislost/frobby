namespace SdvTestFramework.Protocol.Models;

/// <summary>Response shape of <c>bitmap.capture</c>.</summary>
public sealed class BitmapCaptureResult
{
    /// <summary>Absolute path of the written PNG.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Width of the captured (and optionally cropped) image in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height of the captured (and optionally cropped) image in pixels.</summary>
    public int Height { get; set; }
}
