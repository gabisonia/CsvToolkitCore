namespace CsvToolkit.Core.Internal;

internal interface ICsvCharOutput : IDisposable, IAsyncDisposable
{
    void Write(ReadOnlySpan<char> source);

    ValueTask WriteAsync(ReadOnlyMemory<char> source, CancellationToken cancellationToken);

    void Flush();

    ValueTask FlushAsync(CancellationToken cancellationToken);
}
