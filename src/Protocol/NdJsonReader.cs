using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SdvTestFramework.Protocol;

/// <summary>
/// Reads newline-delimited JSON off an arbitrary <see cref="Stream"/>. One line per message,
/// UTF-8 encoding, buffering partial reads. Blank lines are skipped.
/// </summary>
public sealed class NdJsonReader
{
    private readonly Stream _stream;
    private readonly byte[] _readBuf = new byte[4096];
    private readonly StringBuilder _line = new();

    public NdJsonReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>Reads the next message. Returns null at EOF.</summary>
    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        while (true)
        {
            // Try to extract a line from buffered content first — cheap path if a whole
            // message was returned in one read.
            for (; _lineReadyCursor < _line.Length; _lineReadyCursor++)
            {
                if (_line[_lineReadyCursor] != '\n') continue;

                var end = _lineReadyCursor;
                var trimmedStart = 0;
                // trim trailing \r
                if (end > 0 && _line[end - 1] == '\r') end--;

                var line = _line.ToString(trimmedStart, end - trimmedStart);
                _line.Remove(0, _lineReadyCursor + 1);
                _lineReadyCursor = 0;

                if (line.Length == 0) continue;
                return line;
            }

            // Nothing complete buffered; grab more bytes.
            int read = await _stream.ReadAsync(_readBuf.AsMemory(0, _readBuf.Length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                // EOF — any trailing partial line without a newline is dropped per spec.
                // (Strict NDJSON: every record ends with \n.)
                if (_line.Length > 0)
                {
                    _line.Clear();
                    _lineReadyCursor = 0;
                }
                return null;
            }
            _line.Append(Encoding.UTF8.GetString(_readBuf, 0, read));
        }
    }

    private int _lineReadyCursor;
}
