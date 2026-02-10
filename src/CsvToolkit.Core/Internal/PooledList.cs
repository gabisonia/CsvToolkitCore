using System.Buffers;
using System.Runtime.CompilerServices;

namespace CsvToolkit.Core.Internal;

internal sealed class PooledList<T>(int initialCapacity) : IDisposable
{
    private T[] _buffer = ArrayPool<T>.Shared.Rent(Math.Max(8, initialCapacity));

    public int Count { get; private set; }

    public T[] Buffer => _buffer;

    public T this[int index] => _buffer[index];

    public void Clear() => Count = 0;

    public void Add(T value)
    {
        EnsureCapacity(Count + 1);
        _buffer[Count++] = value;
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

        var resized = ArrayPool<T>.Shared.Rent(newSize);
        _buffer.AsSpan(0, Count).CopyTo(resized);
        ArrayPool<T>.Shared.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _buffer = resized;
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _buffer = [];
        Count = 0;
    }
}
