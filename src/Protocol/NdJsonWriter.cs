using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Protocol;

/// <summary>Writes newline-delimited JSON to an arbitrary <see cref="Stream"/>.</summary>
public sealed class NdJsonWriter
{
    private readonly Stream _stream;

    public NdJsonWriter(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Writes one message, appending exactly one <c>'\n'</c>. The message body must not
    /// contain a raw newline — if it does we throw rather than corrupt the framing.
    /// </summary>
    public async Task WriteAsync(string message, CancellationToken ct = default)
    {
        if (message.Contains('\n'))
            throw new ArgumentException("NDJSON message bodies must not contain raw newlines", nameof(message));

        var bytes = Encoding.UTF8.GetBytes(message + "\n");
        await _stream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
