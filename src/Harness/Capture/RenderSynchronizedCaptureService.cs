using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Capture;

/// <summary>
/// Queues bitmap capture callbacks and completes them from the next rendered frame.
/// </summary>
public sealed class RenderSynchronizedCaptureService
{
    private readonly object _gate = new();
    private readonly List<PendingCapture> _pending = new();

    public Task<BitmapCaptureResult> RequestAsync(
        Func<BitmapCaptureResult> capture,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var pending = new PendingCapture(capture, timeout, ct, this.Remove);
        lock (_gate)
        {
            if (!pending.IsCompleted)
                _pending.Add(pending);
        }

        return pending.Task;
    }

    public void OnRendered()
    {
        PendingCapture[] captures;
        lock (_gate)
        {
            captures = _pending.ToArray();
            _pending.Clear();
        }

        foreach (var pending in captures)
            pending.Complete();
    }

    private void Remove(PendingCapture pending)
    {
        lock (_gate)
            _pending.Remove(pending);
    }

    private sealed class PendingCapture
    {
        private readonly Func<BitmapCaptureResult> _capture;
        private readonly Action<PendingCapture> _remove;
        private readonly TaskCompletionSource<BitmapCaptureResult> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _cancelRegistration;
        private readonly Timer _timer;
        private int _completed;

        public PendingCapture(
            Func<BitmapCaptureResult> capture,
            TimeSpan timeout,
            CancellationToken ct,
            Action<PendingCapture> remove)
        {
            _capture = capture;
            _remove = remove;
            if (ct.CanBeCanceled)
                _cancelRegistration = ct.Register(this.Cancel);
            _timer = new Timer(
                _ => this.OnTimeout(timeout),
                null,
                timeout,
                Timeout.InfiniteTimeSpan);
        }

        public Task<BitmapCaptureResult> Task => _tcs.Task;
        public bool IsCompleted => _completed != 0;

        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;

            try { _tcs.TrySetResult(_capture()); }
            catch (Exception ex) { _tcs.TrySetException(ex); }
            finally { this.Dispose(); }
        }

        private void OnTimeout(TimeSpan timeout)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;

            _tcs.TrySetException(new TimeoutException(
                $"bitmap.capture_next_frame timed out after {(int)timeout.TotalMilliseconds}ms waiting for Display.Rendered"));
            this.Dispose();
            _remove(this);
        }

        private void Cancel()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;

            _tcs.TrySetCanceled();
            this.Dispose();
            _remove(this);
        }

        private void Dispose()
        {
            _timer.Dispose();
            _cancelRegistration.Dispose();
        }
    }
}
