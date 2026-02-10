using System.Globalization;

namespace CsvToolkit.Core.TypeConversion;

public readonly record struct CsvConverterContext(
    CultureInfo CultureInfo,
    long RowIndex,
    int FieldIndex,
    string? ColumnName);
