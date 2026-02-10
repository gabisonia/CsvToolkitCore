using CsvToolkit.Core.Internal;
using System.Runtime.CompilerServices;

namespace CsvToolkit.Core;

public readonly struct CsvRow
{
    private readonly ReadOnlyMemory<char> _buffer;
    private readonly CsvFieldToken[]? _fields;
    private readonly int _fieldCount;

    internal CsvRow(ReadOnlyMemory<char> buffer, CsvFieldToken[] fields, int fieldCount, long rowIndex, long lineNumber)
    {
        _buffer = buffer;
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
        return _buffer.Slice(token.Start, token.Length);
    }

    public string GetFieldString(int index)
    {
        return GetFieldMemory(index).ToString();
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