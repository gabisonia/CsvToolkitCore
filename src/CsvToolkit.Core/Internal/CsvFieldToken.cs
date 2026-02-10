namespace CsvToolkit.Core.Internal;

internal readonly struct CsvFieldToken(int start, int length, bool wasQuoted)
{
    public int Start { get; } = start;

    public int Length { get; } = length;

    public bool WasQuoted { get; } = wasQuoted;
}
