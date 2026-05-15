namespace CsvToolkit.Core.Internal;

internal enum CsvFieldSource : byte
{
    RowBuffer,
    InputBuffer
}

internal readonly struct CsvFieldToken(int start, int length, bool wasQuoted, CsvFieldSource source = CsvFieldSource.RowBuffer)
{
    public int Start { get; } = start;

    public int Length { get; } = length;

    public bool WasQuoted { get; } = wasQuoted;

    public CsvFieldSource Source { get; } = source;
}
