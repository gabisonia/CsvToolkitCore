using CsvToolkit.Core.Internal;
using System.Runtime.CompilerServices;

namespace CsvToolkit.Core;

public readonly struct CsvRow
{
    private readonly ReadOnlyMemory<char> _buffer;
    private readonly ReadOnlyMemory<char> _inputBuffer;
    private readonly CsvFieldToken[]? _fields;
    private readonly int _fieldCount;

    internal CsvRow(ReadOnlyMemory<char> buffer, CsvFieldToken[] fields, int fieldCount, long rowIndex, long lineNumber)
        : this(buffer, ReadOnlyMemory<char>.Empty, fields, fieldCount, rowIndex, lineNumber)
    {
    }

    internal CsvRow(
        ReadOnlyMemory<char> buffer,
        ReadOnlyMemory<char> inputBuffer,
        CsvFieldToken[] fields,
        int fieldCount,
        long rowIndex,
        long lineNumber)
    {
        _buffer = buffer;
        _inputBuffer = inputBuffer;
        _fields = fields;
        _fieldCount = fieldCount;
        RowIndex = rowIndex;
        LineNumber = lineNumber;
    }

    public long RowIndex { get; }

    public long LineNumber { get; }

    public int FieldCount => _fieldCount;

    public ReadOnlySpan<char> this[int index] => GetFieldSpan(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<char> GetFieldSpan(int index)
    {
        var memory = GetFieldMemory(index);
        return memory.Span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<char> GetFieldMemory(int index)
    {
        EnsureIndex(index);
        var token = _fields![index];
        return token.Source == CsvFieldSource.InputBuffer
            ? _inputBuffer.Slice(token.Start, token.Length)
            : _buffer.Slice(token.Start, token.Length);
    }

    public string GetFieldString(int index)
    {
        return GetFieldMemory(index).ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<char> GetFieldSpanUnchecked(int index)
    {
        var token = _fields![index];
        return token.Source == CsvFieldSource.InputBuffer
            ? _inputBuffer.Span.Slice(token.Start, token.Length)
            : _buffer.Span.Slice(token.Start, token.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlyMemory<char> GetFieldMemoryUnchecked(int index)
    {
        var token = _fields![index];
        return token.Source == CsvFieldSource.InputBuffer
            ? _inputBuffer.Slice(token.Start, token.Length)
            : _buffer.Slice(token.Start, token.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureIndex(int index)
    {
        if (_fields is null || index < 0 || index >= _fieldCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
