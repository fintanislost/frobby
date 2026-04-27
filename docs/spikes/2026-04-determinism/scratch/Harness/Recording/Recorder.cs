using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SdvTestFramework.SpikeHarness.Recording;

// `StardewModdingAPI.Context` is exposed under the same namespace import.

/// <summary>
/// Arm/disarm state machine + pre-allocated ring buffer for draw events.
/// All recording happens on the game thread (SpriteBatch.Draw callers).
/// Flushing (writes to /tmp) happens on UpdateTicked — also game thread.
/// Therefore the ring buffer does not need locking.
/// </summary>
public static class Recorder
{
    // DISARMED is the fast path: the Harmony prefix just reads _armed and returns.
    // Must be volatile — the console-command handler writes from a potentially different
    // code path during a command-loop tick. (In practice SMAPI runs commands on the
    // update path, but belt-and-braces.)
    private static volatile bool _armed;
    private static bool _armPending; // world not ready yet; auto-arm when it is

    private static DrawEvent[] _buffer = Array.Empty<DrawEvent>();
    private static int _bufferHead; // next write index
    private static int _dropped;    // events lost to overflow (reported in flush footer)
    private static int _callIndex;  // monotonic across the whole run
    private static int _ticksRemaining;
    private static int _totalTicksObserved; // debug counter
    private static bool _savedEventUp;
    private static bool _savedDisplayHUD;

    private static IMonitor? _monitor;
    private static string? _pendingOutputPath;
    private static int _capturedTicks;

    /// <summary>Public read-only flag for the Harmony prefix. JIT should inline to a volatile load.</summary>
    public static bool IsArmed => _armed;

    public static void Initialize(IMonitor monitor, int capacity = 100_000)
    {
        _monitor = monitor;
        _buffer = new DrawEvent[capacity];
    }

    /// <summary>
    /// Begin recording the next <paramref name="ticks"/> complete ticks to <paramref name="outputPath"/>.
    /// If the world isn't ready (still on title screen / loading), arming is deferred until
    /// <see cref="Context.IsWorldReady"/> becomes true — so scripts can issue harness_load
    /// and harness_arm back-to-back without racing the save-load coroutine.
    /// </summary>
    public static void Arm(int ticks, string outputPath)
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

        if (Context.IsWorldReady)
        {
            _armed = true;
            _monitor?.Log($"ARMED: capturing {ticks} ticks to {outputPath}", LogLevel.Info);
        }
        else
        {
            _armPending = true;
            _monitor?.Log(
                $"ARM DEFERRED: world not ready; will start capturing {ticks} ticks to {outputPath} as soon as save loads.",
                LogLevel.Info);
        }
    }

    public static void Disarm()
    {
        if (!_armed) return;
        _armed = false;
        Flush("manual disarm");
    }

    /// <summary>
    /// Hot path: appends one event. Called from the Harmony prefix on SpriteBatch.Draw.
    /// Must stay allocation-free.
    /// </summary>
    public static void Record(in DrawEvent ev)
    {
        if (!_armed) return; // redundant with prefix check, but guards against races
        if (_bufferHead >= _buffer.Length)
        {
            _dropped++;
            return;
        }
        _buffer[_bufferHead++] = ev;
    }

    /// <summary>Allocate a CallIndex and current tick for the current draw event.</summary>
    public static (int tick, int callIndex) NextId()
    {
        // Game1.ticks: public static int, wraps at int.MaxValue (~414 days of 60Hz, fine).
        return (Game1.ticks, ++_callIndex);
    }

    /// <summary>Hook for IModHelper.Events.GameLoop.UpdateTicked.</summary>
    public static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        _totalTicksObserved++;

        // Heartbeat: first few ticks + every 120 ticks (2 sec). Proves the event fires.
        if (_totalTicksObserved <= 5 || _totalTicksObserved % 120 == 0)
        {
            _monitor?.Log(
                $"tick #{_totalTicksObserved}: gameMode={Game1.gameMode} "
                + $"armed={_armed} pending={_armPending} worldReady={Context.IsWorldReady}",
                LogLevel.Info);
        }

        // Permissive readiness check. Context.IsWorldReady can lag behind gameMode=3 because
        // it also requires Game1.hasLoadedGame; for the spike, "in the playing state" is
        // sufficient and lets us capture all the fade-in draws too.
        bool readyForArm = Game1.gameMode == Game1.playingGameMode;

        if (_armPending && readyForArm)
        {
            _armPending = false;
            _armed = true;

            // Ambient-effect suppression per .claude/rules/determinism.md §Particles, critters, grass.
            // eventUp disables scrolling background clouds, weather particles, critters, grass sway,
            // HUD elements tied to event state — all sources of per-tick nondeterminism.
            _savedEventUp = Game1.eventUp;
            _savedDisplayHUD = Game1.displayHUD;
            Game1.eventUp = true;
            Game1.displayHUD = false;

            _monitor?.Log(
                $"ARMED (deferred): capturing {_ticksRemaining} ticks to {_pendingOutputPath} "
                + $"(eventUp:{_savedEventUp}->true, displayHUD:{_savedDisplayHUD}->false)",
                LogLevel.Info);
        }

        if (!_armed) return;

        _capturedTicks++;
        _ticksRemaining--;
        if (_ticksRemaining <= 0)
        {
            _armed = false;
            // Restore game state flipped during arm.
            Game1.eventUp = _savedEventUp;
            Game1.displayHUD = _savedDisplayHUD;
            Flush("tick budget exhausted");
        }
    }

    /// <summary>Deterministic JSONL flush. See DrawEventWriter for field ordering rules.</summary>
    public static void Flush(string reason)
    {
        if (_pendingOutputPath is null)
        {
            _monitor?.Log($"Flush requested ({reason}) but no output path pending.", LogLevel.Warn);
            return;
        }

        var path = _pendingOutputPath;
        _pendingOutputPath = null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var w = new StreamWriter(fs);
            DrawEventWriter.WriteHeader(w, _capturedTicks, _bufferHead, _dropped, reason);
            for (int i = 0; i < _bufferHead; i++)
                DrawEventWriter.WriteEvent(w, in _buffer[i]);
            w.Flush();

            _monitor?.Log($"Flushed {_bufferHead} events ({_dropped} dropped) to {path} — {reason}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _monitor?.Log($"Flush failed: {ex}", LogLevel.Error);
        }
    }
}
