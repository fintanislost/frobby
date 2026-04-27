namespace SdvTestFramework.Protocol.Reports;

/// <summary>
/// File paths produced by a single bitmap-assertion failure. Paths are absolute on
/// generation but relative to the run directory when serialized in <c>summary.json</c>.
/// </summary>
public sealed record DiffSet(
    string Baseline,
    string Capture,
    string Diff,
    string? Triptych);
