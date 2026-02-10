namespace CsvToolkit.Core;

[System.Flags]
public enum CsvTrimOptions
{
    None = 0,
    TrimStart = 1,
    TrimEnd = 2,
    Trim = TrimStart | TrimEnd
}
