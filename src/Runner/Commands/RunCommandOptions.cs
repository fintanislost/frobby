using System.Collections.Generic;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Reports;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// Threaded bundle of parsed CLI flags for the <c>run</c> command path. Replaces the
/// earlier static-field hack so callers (most notably <c>BaselinesCommand.update</c>)
/// can construct an options instance directly and reuse <see cref="RunCommand.RunFromOptions"/>
/// without going through argv parsing.
/// </summary>
/// <remarks>
/// <see cref="PreCreatedRunDir"/> is populated by <see cref="RunCommand.RunAsync"/> after
/// the eager run-directory creation; downstream callers that build their own
/// <see cref="RunCommandOptions"/> can pre-create and pass a <see cref="RunDirectory"/>
/// the same way (or leave it null, in which case no HTML report is generated).
/// <see cref="NoCacheCleanup"/> is parsed and threaded for completeness; the actual
/// cache-cleanup invocation lands in a follow-up task.
/// </remarks>
public sealed record RunCommandOptions(
    IReadOnlyList<string> Paths,
    string? Filter,
    string? ModsPath,
    string ReporterName,
    string? OutputPath,
    bool Watch,
    bool UpdateBaselines,
    string? ReportDirPath,
    bool NoReport,
    DiffFormat DiffFormat,
    string Tier,
    bool NoCacheCleanup,
    RunDirectory? PreCreatedRunDir);
