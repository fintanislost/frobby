using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Protocol.Tests;

/// <summary>
/// In-memory duplex stream pair — each side reads what the other wrote. Used to exercise
/// <see cref="JsonRpcSession"/> end-to-end without a socket dependency.
/// </summary>
internal static class DuplexStreams
{
    public static (Stream a, Stream b) CreatePair()
    {
        var atob = new Pipe();
        var btoa = new Pipe();
        return (
            new CompositeStream(readFrom: btoa.Reader.AsStream(), writeTo: atob.Writer.AsStream()),
            new CompositeStream(readFrom: atob.Reader.AsStream(), writeTo: btoa.Writer.AsStream())
        );
    }

    private sealed class CompositeStream : Stream
    {
        private readonly Stream _r, _w;
        public CompositeStream(Stream readFrom, Stream writeTo) { _r = readFrom; _w = writeTo; }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new System.NotSupportedException();
        public override long Position
        {
            get => throw new System.NotSupportedException();
            set => throw new System.NotSupportedException();
        }
        public override void Flush() => _w.Flush();
        public override Task FlushAsync(CancellationToken ct) => _w.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => _r.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(System.Memory<byte> buffer, CancellationToken ct = default) => _r.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) => _w.Write(buffer, offset, count);
        public override ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _w.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotSupportedException();
        public override void SetLength(long value) => throw new System.NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Close write side first so the peer sees EOF on read; closing read side
                // last prevents ObjectDisposedException on any in-flight reads we own.
                try { _w.Dispose(); } catch { /* best-effort */ }
                try { _r.Dispose(); } catch { /* best-effort */ }
            }
            base.Dispose(disposing);
        }
    }
}
