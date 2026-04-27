using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Reports;

/// <summary>
/// Diff artifact set produced when a bitmap assertion fails.
/// <list type="bullet">
///   <item><see cref="Files"/> — write only the 3 separate PNGs (baseline, capture, diff).</item>
///   <item><see cref="Triptych"/> — also write a horizontal stitch composite.</item>
///   <item><see cref="All"/> — same as <see cref="Triptych"/> today; reserved for future composites.</item>
/// </list>
/// </summary>
// Non-generic JsonStringEnumConverter — Protocol targets net6.0 where the
// generic JsonStringEnumConverter<T> form (added in .NET 7) is not available.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiffFormat
{
    Files,
    Triptych,
    All,
}
