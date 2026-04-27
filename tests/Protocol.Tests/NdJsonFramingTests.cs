using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

/// <summary>
/// Newline-delimited-JSON framing: one message per line, UTF-8, separated by <c>'\n'</c>.
/// Matches <c>docs/rpc-schema.md §Transport</c>.
/// </summary>
public class NdJsonFramingTests
{
    [Fact]
    public async Task Reader_YieldsOneMessagePerLine()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"a\":1}\n" +
            "{\"b\":2}\n" +
            "{\"c\":3}\n"));

        var reader = new NdJsonReader(stream);

        var m1 = await reader.ReadAsync();
        var m2 = await reader.ReadAsync();
        var m3 = await reader.ReadAsync();
        var eof = await reader.ReadAsync();

        Assert.Equal("{\"a\":1}", m1);
        Assert.Equal("{\"b\":2}", m2);
        Assert.Equal("{\"c\":3}", m3);
        Assert.Null(eof);
    }

    [Fact]
    public async Task Reader_HandlesPartialReads()
    {
        // Simulate TCP-style chunking by using a stream that returns one byte at a time.
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":2}\n");
        using var slow = new ByteAtATimeStream(bytes);
        var reader = new NdJsonReader(slow);

        Assert.Equal("{\"a\":1}", await reader.ReadAsync());
        Assert.Equal("{\"b\":2}", await reader.ReadAsync());
        Assert.Null(await reader.ReadAsync());
    }

    [Fact]
    public async Task Reader_IgnoresBlankLines()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "\n{\"a\":1}\n\n{\"b\":2}\n"));
        var reader = new NdJsonReader(stream);

        Assert.Equal("{\"a\":1}", await reader.ReadAsync());
        Assert.Equal("{\"b\":2}", await reader.ReadAsync());
        Assert.Null(await reader.ReadAsync());
    }

    [Fact]
    public async Task Writer_AppendsExactlyOneNewlinePerMessage()
    {
        using var ms = new MemoryStream();
        var writer = new NdJsonWriter(ms);

        await writer.WriteAsync("{\"a\":1}");
        await writer.WriteAsync("{\"b\":2}");

        Assert.Equal(
            "{\"a\":1}\n{\"b\":2}\n",
            Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task Writer_RejectsMessagesContainingNewlines()
    {
        using var ms = new MemoryStream();
        var writer = new NdJsonWriter(ms);
        await Assert.ThrowsAsync<System.ArgumentException>(
            async () => await writer.WriteAsync("has\nnewline"));
    }

    /// <summary>Stream that returns one byte per ReadAsync call — exercises buffering.</summary>
    private sealed class ByteAtATimeStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ByteAtATimeStream(byte[] data) { _data = data; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new System.NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            buffer[offset] = _data[_pos++];
            return 1;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotSupportedException();
        public override void SetLength(long value) => throw new System.NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();
    }
}
