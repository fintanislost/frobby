using System;

namespace SdvTestFramework.Harness.Recording;

/// <summary>What kind of action occurred during a recording session.</summary>
internal enum ActionKind { Warp, NpcInteract, TimeAdvance }

/// <summary>
/// Buffered action event captured by <see cref="ActionTraceRecorder"/>. Translated to
/// scenario steps by <see cref="ActionTraceTranslator"/>.
/// </summary>
internal sealed record RecordedAction(
    DateTime At,
    ActionKind Kind,
    string? Location = null,
    int? X = null,
    int? Y = null,
    string? NpcName = null,
    int? MinutesElapsed = null);
