using System.Buffers;
using System.Text;

namespace CsvToolkit.Core.Internal;

internal sealed class Utf8StreamOutput(Stream stream, int byteBufferSize, bool leaveOpen) : ICsvCharOutput
{
    private readonly Encoder _encoder = Encoding.UTF8.GetEncoder();
    private readonly byte[] _byteBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(32, byteBufferSize));
    private int _bufferedByteCount;

    public void Write(ReadOnlySpan<char> source)
    {
        while (!source.IsEmpty)
        {
            if (_bufferedByteCount == _byteBuffer.Length)
            {
                FlushBuffer();
            }

            _encoder.Convert(source, _byteBuffer.AsSpan(_bufferedByteCount), flush: false, out var charsUsed,
                out var bytesUsed, out _);
            _bufferedByteCount += bytesUsed;

            if (charsUsed == 0)
            {
                FlushBuffer();
                continue;
            }

            source = source[charsUsed..];
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<char> source, CancellationToken cancellationToken)
    {
        while (!source.IsEmpty)
        {
            if (_bufferedByteCount == _byteBuffer.Length)
            {
                await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
            }

            _encoder.Convert(source.Span, _byteBuffer.AsSpan(_bufferedByteCount), flush: false, out var charsUsed,
                out var bytesUsed, out _);
            _bufferedByteCount += bytesUsed;

            if (charsUsed == 0)
            {
                await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            source = source[charsUsed..];
        }
    }

    public void WriteUtf8(ReadOnlySpan<byte> source)
    {
        while (!source.IsEmpty)
        {
            var writable = _byteBuffer.Length - _bufferedByteCount;
            if (writable == 0)
            {
                FlushBuffer();
                writable = _byteBuffer.Length;
            }

            var toCopy = Math.Min(writable, source.Length);
            source[..toCopy].CopyTo(_byteBuffer.AsSpan(_bufferedByteCount));
            _bufferedByteCount += toCopy;
            source = source[toCopy..];
        }
    }

    public async ValueTask WriteUtf8Async(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
    {
        while (!source.IsEmpty)
        {
            var writable = _byteBuffer.Length - _bufferedByteCount;
            if (writable == 0)
            {
                await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
                writable = _byteBuffer.Length;
            }

            var toCopy = Math.Min(writable, source.Length);
            source.Span[..toCopy].CopyTo(_byteBuffer.AsSpan(_bufferedByteCount));
            _bufferedByteCount += toCopy;
            source = source[toCopy..];
        }
    }

    public void WriteByte(byte value)
    {
        if (_bufferedByteCount == _byteBuffer.Length)
        {
            FlushBuffer();
        }

        _byteBuffer[_bufferedByteCount++] = value;
    }

    public ValueTask WriteByteAsync(byte value, CancellationToken cancellationToken)
    {
        if (_bufferedByteCount == _byteBuffer.Length)
        {
            return WriteByteSlowAsync(value, cancellationToken);
        }

        _byteBuffer[_bufferedByteCount++] = value;
        return default;
    }

    public void Flush()
    {
        FlushEncoder();
        FlushBuffer();
        stream.Flush();
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        await FlushEncoderAsync(cancellationToken).ConfigureAwait(false);
        await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        FlushEncoder();
        ArrayPool<byte>.Shared.Return(_byteBuffer);
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushEncoderAsync(CancellationToken.None).ConfigureAwait(false);
        ArrayPool<byte>.Shared.Return(_byteBuffer);
        if (!leaveOpen)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void FlushEncoder()
    {
        Span<char> emptyChars = [];
        while (true)
        {
            _encoder.Convert(emptyChars, _byteBuffer.AsSpan(_bufferedByteCount), flush: true, out _, out var bytesUsed,
                out var completed);
            _bufferedByteCount += bytesUsed;

            if (!completed && bytesUsed == 0)
            {
                FlushBuffer();
                continue;
            }

            if (completed)
            {
                break;
            }
        }
    }

    private async ValueTask FlushEncoderAsync(CancellationToken cancellationToken)
    {
        ReadOnlyMemory<char> emptyChars = ReadOnlyMemory<char>.Empty;
        while (true)
        {
            _encoder.Convert(emptyChars.Span, _byteBuffer.AsSpan(_bufferedByteCount), flush: true, out _,
                out var bytesUsed,
                out var completed);
            _bufferedByteCount += bytesUsed;

            if (!completed && bytesUsed == 0)
            {
                await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (completed)
            {
                break;
            }
        }
    }

    private async ValueTask WriteByteSlowAsync(byte value, CancellationToken cancellationToken)
    {
        await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
        _byteBuffer[_bufferedByteCount++] = value;
    }

    private void FlushBuffer()
    {
        if (_bufferedByteCount == 0)
        {
            return;
        }

        stream.Write(_byteBuffer.AsSpan(0, _bufferedByteCount));
        _bufferedByteCount = 0;
    }

    private async ValueTask FlushBufferAsync(CancellationToken cancellationToken)
    {
        if (_bufferedByteCount == 0)
        {
            return;
        }

        await stream.WriteAsync(_byteBuffer.AsMemory(0, _bufferedByteCount), cancellationToken).ConfigureAwait(false);
        _bufferedByteCount = 0;
    }
}
