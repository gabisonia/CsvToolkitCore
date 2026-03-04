namespace CsvToolkit.Core;

public readonly record struct CsvHeaderValidationContext(
    long RowIndex,
    long LineNumber,
    IReadOnlyList<string> Headers);
