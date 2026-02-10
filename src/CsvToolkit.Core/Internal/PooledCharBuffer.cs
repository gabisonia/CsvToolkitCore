using System.Buffers;

namespace CsvToolkit.Core.Internal;

internal sealed class PooledCharBuffer(int initialCapacity) : IDisposable
{
    private char[] _buffer = ArrayPool<char>.Shared.Rent(Math.Max(16, initialCapacity));

    public int Length { get; private set; }

    public char[] Buffer => _buffer;

    public ReadOnlyMemory<char> WrittenMemory => _buffer.AsMemory(0, Length);

    public void Clear() => Length = 0;

    public void Append(char value)
    {
        EnsureCapacity(Length + 1);
        _buffer[Length++] = value;
    }

    public void Append(ReadOnlySpan<char> value)
    {
        EnsureCapacity(Length + value.Length);
        value.CopyTo(_buffer.AsSpan(Length));
        Length += value.Length;
    }

    public ReadOnlyMemory<char> GetMemory(int start, int length)
    {
        return _buffer.AsMemory(start, length);
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length)
        {
            return;
        }

        var newSize = _buffer.Length;
        while (newSize < required)
        {
            newSize *= 2;
        }

        var resized = ArrayPool<char>.Shared.Rent(newSize);
        _buffer.AsSpan(0, Length).CopyTo(resized);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = resized;
    }

    public void Dispose()
    {
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = [];
        Length = 0;
    }
}
