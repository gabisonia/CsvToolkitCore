namespace CsvToolkit.Core;

public readonly record struct CsvReadingExceptionContext(
    Exception Exception,
    long RowIndex,
    long LineNumber,
    int FieldIndex,
    ReadOnlyMemory<char> RawField);
