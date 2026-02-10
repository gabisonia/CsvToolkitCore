namespace CsvToolkit.Core;

public sealed class CsvException(string message, long rowIndex, long lineNumber, int fieldIndex)
    : Exception(message)
{
    public long RowIndex { get; } = rowIndex;

    public long LineNumber { get; } = lineNumber;

    public int FieldIndex { get; } = fieldIndex;
}