using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SdvTestFramework.Runner.Watch;

/// <summary>
/// Wraps <see cref="FileSystemWatcher"/> with a debounce so burst events from editor
/// saves coalesce into a single callback. One internal watcher per input path — files
/// watch their parent directory filtered to the specific filename; directories watch
/// recursively filtered to <c>*.test.json</c>.
/// </summary>
/// <remarks>
/// Debounce uses <see cref="System.Threading.Timer"/> (lighter than <c>System.Timers.Timer</c>;
/// has the exact "reset on event" semantics needed). Callback runs on a thread-pool thread;
/// callers must be thread-safe.
/// </remarks>
public sealed class ScenarioWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Action _onTriggered;
    private readonly TimeSpan _debounce;
    private readonly Timer _debounceTimer;
    private int _disposed;

    public ScenarioWatcher(
        IReadOnlyList<string> paths,
        Action onTriggered,
        TimeSpan? debounce = null)
    {
        _onTriggered = onTriggered ?? throw new ArgumentNullException(nameof(onTriggered));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);

        _debounceTimer = new Timer(_ => Fire(), state: null, Timeout.Infinite, Timeout.Infinite);

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
                var file = Path.GetFileName(path);
                InstallWatcher(dir, file, includeSub: false);
            }
            else if (Directory.Exists(path))
            {
                InstallWatcher(path, "*.test.json", includeSub: true);
            }
        }
    }

    /// <summary>Bypass the debounce and fire the callback synchronously. Tests only.</summary>
    public void TriggerForTests() => _onTriggered();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _debounceTimer.Dispose();
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); } catch { /* best-effort */ }
        }
        _watchers.Clear();
    }

    private void InstallWatcher(string dir, string filter, bool includeSub)
    {
        var w = new FileSystemWatcher(dir, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = includeSub,
            EnableRaisingEvents = true,
        };
        w.Created += OnAny;
        w.Changed += OnAny;
        w.Deleted += OnAny;
        w.Renamed += OnAny;
        _watchers.Add(w);
    }

    private void OnAny(object sender, FileSystemEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    private void Fire()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _onTriggered(); }
        catch { /* swallow — callback failures shouldn't kill the timer thread */ }
    }
}
