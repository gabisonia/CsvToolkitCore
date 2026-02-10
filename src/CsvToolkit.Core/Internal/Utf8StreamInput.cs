using System.Buffers;
using System.Text;

namespace CsvToolkit.Core.Internal;

internal sealed class Utf8StreamInput(Stream stream, int byteBufferSize, bool leaveOpen) : ICsvCharInput
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly byte[] _byteBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(32, byteBufferSize));
    private int _bytePosition;
    private int _byteLength;
    private bool _reachedEof;

    public int Read(Span<char> destination)
    {
        var totalChars = 0;

        while (destination.Length > 0)
        {
            EnsureBytes();

            var bytes = _byteBuffer.AsSpan(_bytePosition, _byteLength - _bytePosition);
            _decoder.Convert(bytes, destination, _reachedEof, out var bytesUsed, out var charsUsed, out _);

            _bytePosition += bytesUsed;
            destination = destination[charsUsed..];
            totalChars += charsUsed;

            if (charsUsed == 0)
            {
                if (_reachedEof)
                {
                    break;
                }

                if (_bytePosition >= _byteLength)
                {
                    continue;
                }
            }
        }

        return totalChars;
    }

    public async ValueTask<int> ReadAsync(Memory<char> destination, CancellationToken cancellationToken)
    {
        var totalChars = 0;

        while (destination.Length > 0)
        {
            await EnsureBytesAsync(cancellationToken).ConfigureAwait(false);

            var bytes = _byteBuffer.AsSpan(_bytePosition, _byteLength - _bytePosition);
            _decoder.Convert(bytes, destination.Span, _reachedEof, out var bytesUsed, out var charsUsed, out _);

            _bytePosition += bytesUsed;
            destination = destination[charsUsed..];
            totalChars += charsUsed;

            if (charsUsed == 0)
            {
                if (_reachedEof)
                {
                    break;
                }

                if (_bytePosition >= _byteLength)
                {
                    continue;
                }
            }
        }

        return totalChars;
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_byteBuffer);
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        ArrayPool<byte>.Shared.Return(_byteBuffer);
        if (!leaveOpen)
        {
            return stream.DisposeAsync();
        }

        return ValueTask.CompletedTask;
    }

    private void EnsureBytes()
    {
        if (_bytePosition < _byteLength || _reachedEof)
        {
            return;
        }

        _byteLength = stream.Read(_byteBuffer, 0, _byteBuffer.Length);
        _bytePosition = 0;
        if (_byteLength == 0)
        {
            _reachedEof = true;
        }
    }

    private async ValueTask EnsureBytesAsync(CancellationToken cancellationToken)
    {
        if (_bytePosition < _byteLength || _reachedEof)
        {
            return;
        }

        _byteLength = await stream.ReadAsync(_byteBuffer.AsMemory(0, _byteBuffer.Length), cancellationToken).ConfigureAwait(false);
        _bytePosition = 0;
        if (_byteLength == 0)
        {
            _reachedEof = true;
        }
    }
}
