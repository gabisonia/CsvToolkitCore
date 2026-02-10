using System.Buffers;
using System.Text;

namespace CsvToolkit.Core.Internal;

internal sealed class Utf8StreamOutput(Stream stream, int byteBufferSize, bool leaveOpen) : ICsvCharOutput
{
    private readonly Encoder _encoder = Encoding.UTF8.GetEncoder();
    private readonly byte[] _byteBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(32, byteBufferSize));

    public void Write(ReadOnlySpan<char> source)
    {
        while (!source.IsEmpty)
        {
            _encoder.Convert(source, _byteBuffer.AsSpan(), flush: false, out var charsUsed, out var bytesUsed, out _);
            if (bytesUsed > 0)
            {
                stream.Write(_byteBuffer.AsSpan(0, bytesUsed));
            }

            source = source[charsUsed..];
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<char> source, CancellationToken cancellationToken)
    {
        while (!source.IsEmpty)
        {
            _encoder.Convert(source.Span, _byteBuffer.AsSpan(), flush: false, out var charsUsed, out var bytesUsed,
                out _);
            if (bytesUsed > 0)
            {
                await stream.WriteAsync(_byteBuffer.AsMemory(0, bytesUsed), cancellationToken).ConfigureAwait(false);
            }

            source = source[charsUsed..];
        }
    }

    public void Flush()
    {
        FlushEncoder();
        stream.Flush();
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        await FlushEncoderAsync(cancellationToken).ConfigureAwait(false);
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
            _encoder.Convert(emptyChars, _byteBuffer.AsSpan(), flush: true, out _, out var bytesUsed,
                out var completed);
            if (bytesUsed > 0)
            {
                stream.Write(_byteBuffer.AsSpan(0, bytesUsed));
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
            _encoder.Convert(emptyChars.Span, _byteBuffer.AsSpan(), flush: true, out _, out var bytesUsed,
                out var completed);
            if (bytesUsed > 0)
            {
                await stream.WriteAsync(_byteBuffer.AsMemory(0, bytesUsed), cancellationToken).ConfigureAwait(false);
            }

            if (completed)
            {
                break;
            }
        }
    }
}