namespace CsvToolkit.Core.Internal;

internal sealed class CsvRowBuffer(int initialCharCapacity) : IDisposable
{
    private readonly PooledCharBuffer _chars = new(initialCharCapacity);
    private readonly PooledList<CsvFieldToken> _fields = new(32);
    private int _currentFieldStart;

    public int FieldCount => _fields.Count;

    public int CurrentFieldLength => _chars.Length - _currentFieldStart;

    public ReadOnlyMemory<char> CurrentFieldMemory => _chars.GetMemory(_currentFieldStart, CurrentFieldLength);

    public void Reset()
    {
        _chars.Clear();
        _fields.Clear();
        _currentFieldStart = 0;
    }

    public void Append(char value) => _chars.Append(value);

    public void CompleteField(bool wasQuoted, CsvTrimOptions trimOptions)
    {
        // Trimming is represented by offset/length metadata so we avoid copying field data.
        var start = _currentFieldStart;
        var length = _chars.Length - _currentFieldStart;

        if ((trimOptions & CsvTrimOptions.TrimStart) != 0)
        {
            while (length > 0 && char.IsWhiteSpace(_chars.Buffer[start]))
            {
                start++;
                length--;
            }
        }

        if ((trimOptions & CsvTrimOptions.TrimEnd) != 0)
        {
            while (length > 0 && char.IsWhiteSpace(_chars.Buffer[start + length - 1]))
            {
                length--;
            }
        }

        _fields.Add(new CsvFieldToken(start, length, wasQuoted));
        _currentFieldStart = _chars.Length;
    }

    public bool IsBlankLine()
    {
        return _fields.Count == 1 &&
               _fields[0].Length == 0 &&
               !_fields[0].WasQuoted;
    }

    public CsvRow ToRow(long rowIndex, long lineNumber)
    {
        return new CsvRow(_chars.WrittenMemory, _fields.Buffer, _fields.Count, rowIndex, lineNumber);
    }

    public void Dispose()
    {
        _chars.Dispose();
        _fields.Dispose();
    }
}
