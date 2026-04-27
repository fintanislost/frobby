using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Reports;

/// <summary>
/// Bitmap diff method. Wire-format string: <c>"ssim"</c>, <c>"pixel-exact"</c>, <c>"dhash"</c>.
/// Wire format uses kebab-case strings; the enum uses PascalCase. Conversion via
/// <c>BitmapMethodExtensions.ParseMethod</c> in the Runner project.
/// </summary>
// Non-generic JsonStringEnumConverter — Protocol targets net6.0 where the
// generic JsonStringEnumConverter<T> form (added in .NET 7) is not available.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BitmapMethod
{
    Ssim,
    PixelExact,
    DHash,
}
