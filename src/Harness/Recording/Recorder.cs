using System;
using System.IO;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Arm/disarm state machine + pre-allocated ring buffer for draw events.
/// All recording happens on the game thread (the thread that calls <c>SpriteBatch.Draw</c>).
/// Flushing (writes to disk) happens on <c>UpdateTicked</c>, also game thread, so the ring
/// buffer needs no locks.
/// </summary>
public static class Recorder
{
    // Fast path: Harmony prefix reads _armed and returns. Volatile because the console /
    // RPC handler writes from a different code path during a command-loop tick.
    private static volatile bool _armed;
    private static bool _armPending;

    private static DrawEvent[] _buffer = Array.Empty<DrawEvent>();
    private static int _bufferHead;
    private static int _dropped;
    private static int _callIndex;
    private static int _ticksRemaining;

    private static IMonitor? _monitor;
    private static string? _pendingOutputPath;
    private static int _capturedTicks;

    public static bool IsArmed => _armed;

    /// <summary>
    /// Initialize the recorder with an optional <paramref name="monitor"/> and a pre-allocated
    /// ring buffer of <paramref name="capacity"/> events. A null monitor is legal so unit tests
    /// can exercise Recorder APIs without a full SMAPI host — log calls then no-op.
    /// </summary>
    public static void Initialize(IMonitor? monitor, int capacity = 100_000)
    {
        _monitor = monitor;
        _buffer = new DrawEvent[capacity];
        _bufferHead = 0;
        _dropped = 0;
        _callIndex = 0;
        _capturedTicks = 0;
        _armed = false;
        _armPending = false;
    }

    /// <summary>
    /// Begin recording the next <paramref name="ticks"/> ticks. When <paramref name="outputPath"/>
    /// is non-null the buffer is flushed to a JSONL file on completion; when null capture is
    /// in-memory only and retrievable via <see cref="SnapshotEvents"/> (exposed to the
    /// <c>draw.snapshot</c> RPC). If the world isn't ready (title screen / loading), arming is
    /// deferred until <c>Game1.gameMode == playingGameMode</c>, so scripted clients can issue
    /// load-then-arm back to back without racing the save-load coroutine.
    /// </summary>
    public static void Arm(int ticks, string? outputPath = null)
    {
        if (_armed || _armPending)
            throw new InvalidOperationException("Already armed.");
        if (ticks < 1)
            throw new ArgumentOutOfRangeException(nameof(ticks));

        _bufferHead = 0;
        _dropped = 0;
        _callIndex = 0;
        _capturedTicks = 0;
        _ticksRemaining = ticks;
        _pendingOutputPath = outputPath;

        if (Game1.gameMode == Game1.playingGameMode)
        {
            ActivateArm(deferred: false);
        }
        else
        {
            _armPending = true;
            _monitor?.Log(
                $"ARM DEFERRED: will capture {ticks} ticks to {outputPath ?? "<in-memory>"} as soon as save loads.",
                LogLevel.Info);
        }
    }

    /// <summary>Copy the current ring buffer's contents for inspection, without flushing.</summary>
    /// <remarks>
    /// Called by <c>draw.snapshot</c> (T10) to read the captured events while the recorder is
    /// still armed or after disarm. Returns a fresh array sized to the current head so callers
    /// don't observe buffer slots past the written region. Always succeeds — no <c>Try</c>
    /// prefix because there's no failure path.
    /// </remarks>
    public static void SnapshotEvents(out DrawEvent[] events, out SnapshotMetadata meta)
    {
        var copy = new DrawEvent[_bufferHead];
        System.Array.Copy(_buffer, copy, _bufferHead);
        events = copy;
        meta = new SnapshotMetadata(_capturedTicks, _dropped);
    }

    public static void Disarm()
    {
        if (!_armed && !_armPending) return;
        _armPending = false;
        _armed = false;
        Flush("manual disarm");
    }

    public static void Record(in DrawEvent ev)
    {
        if (!_armed) return;
        if (_bufferHead >= _buffer.Length)
        {
            _dropped++;
            return;
        }
        _buffer[_bufferHead++] = ev;
    }

    public static (int tick, int callIndex) NextId() => (Game1.ticks, ++_callIndex);

    public static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (_armPending && Game1.gameMode == Game1.playingGameMode)
        {
            _armPending = false;
            ActivateArm(deferred: true);
        }

        if (!_armed) return;

        _capturedTicks++;
        _ticksRemaining--;
        if (_ticksRemaining <= 0)
        {
            _armed = false;
            Flush("tick budget exhausted");
        }
    }

    private static void ActivateArm(bool deferred)
    {
        // Arm is purely "start capture." Ambient-effect suppression and cursor freeze
        // now live in DeterminismController (D1.6 migration); scenarios that want them
        // should call freeze.begin before/alongside arm.
        _armed = true;
        _monitor?.Log(
            $"ARMED{(deferred ? " (deferred)" : "")}: capturing {_ticksRemaining} ticks to {_pendingOutputPath ?? "<in-memory>"}",
            LogLevel.Info);
    }

    private static void Flush(string reason)
    {
        if (_pendingOutputPath is null)
        {
            // In-memory-only arm: no file to write. Snapshot remains available via
            // SnapshotEvents until the next Arm() resets the buffer head.
            _monitor?.Log(
                $"Flush skipped ({reason}): in-memory-only capture — {_bufferHead} events ({_dropped} dropped) retained for snapshot.",
                LogLevel.Info);
            return;
        }

        var path = _pendingOutputPath;
        _pendingOutputPath = null;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var w = new StreamWriter(fs);
            DrawEventWriter.WriteHeader(w, _capturedTicks, _bufferHead, _dropped, reason);
            for (int i = 0; i < _bufferHead; i++)
                DrawEventWriter.WriteEvent(w, in _buffer[i]);
            w.Flush();

            _monitor?.Log(
                $"Flushed {_bufferHead} events ({_dropped} dropped) to {path} — {reason}",
                LogLevel.Info);
        }
        catch (Exception ex)
        {
            _monitor?.Log($"Flush failed: {ex}", LogLevel.Error);
        }
    }
}

/// <summary>
/// Shape returned alongside <see cref="Recorder.SnapshotEvents"/>. Kept at namespace scope
/// so <c>draw.snapshot</c>'s response DTO (T10) can project it directly.
/// </summary>
public readonly struct SnapshotMetadata
{
    public SnapshotMetadata(int ticks, int dropped) { Ticks = ticks; Dropped = dropped; }

    /// <summary>Number of update ticks observed while armed.</summary>
    public int Ticks { get; }

    /// <summary>Events dropped because the ring buffer was full at write time.</summary>
    public int Dropped { get; }
}
