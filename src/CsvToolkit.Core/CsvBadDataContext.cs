namespace CsvToolkit.Core;

public readonly record struct CsvBadDataContext(
    long RowIndex,
    long LineNumber,
    int FieldIndex,
    string Message,
    ReadOnlyMemory<char> RawField);