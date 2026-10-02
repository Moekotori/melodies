using System.Text;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal sealed class BoundedLineReader(StreamReader reader)
{
    private readonly char[] _buffer = new char[4096];
    private int _offset, _count;
    public async ValueTask<string> ReadAsync(CancellationToken cancellation)
    {
        var line = new StringBuilder(256);
        while (true)
        {
            if (_offset == _count)
            {
                _count = await reader.ReadAsync(_buffer.AsMemory(), cancellation).ConfigureAwait(false);
                _offset = 0;
                if (_count == 0) throw new MetadataReadException("native_scanner_exited");
            }
            var newline = Array.IndexOf(_buffer, '\n', _offset, _count - _offset);
            var length = (newline < 0 ? _count : newline) - _offset;
            if (line.Length + length > 256 * 1024) throw new MetadataReadException("native_reply_size_limit");
            line.Append(_buffer, _offset, length);
            _offset += length;
            if (newline < 0) continue;
            _offset++;
            return line.ToString().TrimEnd('\r');
        }
    }
}
