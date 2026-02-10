namespace CsvToolkit.Core.Internal;

internal interface ICsvCharInput : IDisposable, IAsyncDisposable
{
    int Read(Span<char> destination);

    ValueTask<int> ReadAsync(Memory<char> destination, CancellationToken cancellationToken);
}
