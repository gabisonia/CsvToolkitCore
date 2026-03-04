namespace CsvToolkit.Core;

public readonly record struct CsvMissingFieldContext(
    long RowIndex,
    long LineNumber,
    int FieldIndex,
    string MemberName,
    string Message);
